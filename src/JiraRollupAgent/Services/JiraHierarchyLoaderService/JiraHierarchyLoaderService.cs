using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using JiraRollupAgent.DAL.Repositories.Interfaces;
using JiraRollupAgent.Extensions;
using JiraRollupAgent.Models.JiraHierarchyLoaderService;
using Microsoft.Extensions.Configuration;
using Entities = JiraRollupAgent.DAL.Models;

namespace JiraRollupAgent.Services.JiraHierarchyLoaderService
{
    /// <summary>
    /// The "Extract" stage: deserializes MockData/jira-hierarchy.json + team-roster.json and loads them
    /// into VSLiveJiraRollup, standing in for a real Jira ingestion pipeline. Self-disables after a
    /// successful run - see <see cref="DisableHierarchyLoadFlagAsync"/>.
    /// </summary>
    public class JiraHierarchyLoaderService : IJiraHierarchyLoaderService
    {
        //This is required to enable logging for this class.
        private readonly Serilog.ILogger _log = Serilog.Log.ForContext<JiraHierarchyLoaderService>();
        private readonly IConfiguration _config;
        private readonly IUnitOfWork _unitOfWork;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>Creates the loader service.</summary>
        /// <param name="config">Application configuration, used to read the <c>AppSettings:RunHierarchyLoad</c> flag.</param>
        /// <param name="unitOfWork">Provides access to the repositories the loaded hierarchy is written to.</param>
        public JiraHierarchyLoaderService(IConfiguration config, IUnitOfWork unitOfWork)
        {
            _config = config;
            _unitOfWork = unitOfWork;
        }

        #region Run

        /// <inheritdoc/>
        public async Task<bool> Run()
        {
            var runStage = _config.GetValue<bool>("AppSettings:RunHierarchyLoad");

            try
            {
                if (!runStage)
                {
                    _log.Here().Information("Process to load the mocked Jira hierarchy completed ** SKIPPED **");
                    return true;
                }

                _log.Here().Information("Starting process to load the mocked Jira hierarchy from MockData/jira-hierarchy.json...");

                var mockDataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MockData");
                var hierarchy = await ReadJsonAsync<MockJiraHierarchy>(Path.Combine(mockDataDirectory, "jira-hierarchy.json"));
                var roster = await ReadJsonAsync<MockTeamRoster>(Path.Combine(mockDataDirectory, "team-roster.json"));

                await _unitOfWork.DeleteAllRowsAsync();

                await LoadTeamMembersAsync(roster);
                await LoadInitiativesAsync(hierarchy);

                await _unitOfWork.CompleteAsync();

                _log.Here().Information(
                    "Process to load the mocked Jira hierarchy completed successfully: {InitiativeCount} initiatives, {TeamMemberCount} team members.",
                    hierarchy.Initiatives.Count, roster.Team.Count);

                await DisableHierarchyLoadFlagAsync();

                return true;
            }
            catch (Exception ex)
            {
                _log.Here().Warning(ex, "Process to load the mocked Jira hierarchy ** FAILED! **");
                return false;
            }
        }

        #endregion

        #region Self-disable

        /// <summary>
        /// Flips AppSettings:RunHierarchyLoad to false in appsettings.json after a successful load, so the
        /// hierarchy is loaded once and subsequent runs skip it. Setting it back to true by hand causes the
        /// next run to wipe and reload everything from the mock JSON again.
        /// </summary>
        /// <param name="appSettingsPathOverride">Overrides the appsettings.json path to read/write; <c>null</c> uses the real build output path. Used by tests to exercise the "missing AppSettings section" and read/parse-failure branches against an isolated temp file.</param>
        internal async Task DisableHierarchyLoadFlagAsync(string? appSettingsPathOverride = null)
        {
            try
            {
                var appSettingsPath = appSettingsPathOverride ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                var json = await File.ReadAllTextAsync(appSettingsPath);
                var root = JsonNode.Parse(json)?.AsObject();

                if (root?["AppSettings"] is not JsonObject appSettings)
                {
                    _log.Here().Warning("Could not find an \"AppSettings\" section in appsettings.json - leaving RunHierarchyLoad as-is.");
                    return;
                }

                appSettings["RunHierarchyLoad"] = false;

                var writeOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                await File.WriteAllTextAsync(appSettingsPath, root!.ToJsonString(writeOptions));

                _log.Here().Information("Set AppSettings:RunHierarchyLoad to false so the next run skips reloading the hierarchy.");
            }
            catch (Exception ex)
            {
                _log.Here().Warning(ex, "Failed to disable AppSettings:RunHierarchyLoad after a successful load - it will run again next time.");
            }
        }

        #endregion

        #region Loading

        /// <summary>Deserializes a mock data JSON file into <typeparamref name="T"/>, case-insensitively.</summary>
        /// <typeparam name="T">The deserialization target type.</typeparam>
        /// <param name="path">The absolute path to the JSON file.</param>
        /// <returns>The deserialized object, or a new empty <typeparamref name="T"/> if deserialization produced <c>null</c>.</returns>
        private static async Task<T> ReadJsonAsync<T>(string path) where T : new()
        {
            await using var stream = File.OpenRead(path);
            var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
            return result ?? new T();
        }

        /// <summary>Maps the mock team roster into <see cref="Entities.TeamMember"/> entities and marks them added.</summary>
        /// <param name="roster">The deserialized team roster.</param>
        private async Task LoadTeamMembersAsync(MockTeamRoster roster)
        {
            var teamMembers = roster.Team.Select(t => new Entities.TeamMember
            {
                ExternalId = t.Id,
                Name = t.Name,
                Role = t.Role,
                JobTitle = t.JobTitle,
                Email = t.Email
            });

            await _unitOfWork.TeamMembers.AddRangeAsync(teamMembers);
        }

        /// <summary>Maps every mock Initiative into its full entity graph (Epics/WorkItems/Children/Comments) and marks each added.</summary>
        /// <param name="hierarchy">The deserialized mock Jira hierarchy.</param>
        private async Task LoadInitiativesAsync(MockJiraHierarchy hierarchy)
        {
            foreach (var mockInitiative in hierarchy.Initiatives)
            {
                var initiative = new Entities.Initiative
                {
                    JiraId = mockInitiative.Id,
                    Title = mockInitiative.Title,
                    PriorityRank = mockInitiative.PriorityRank,
                    Status = mockInitiative.Status,
                    Comments = mockInitiative.Comments.Select(MapComment).ToList()
                };

                foreach (var mockEpic in mockInitiative.Epics)
                    initiative.Epics.Add(MapEpic(mockEpic));

                // Adding the Initiative marks its whole reachable graph (Epics, WorkItems,
                // Subtask/StoryBug children, Comments) as Added; EF Core inserts them in
                // dependency order on CompleteAsync().
                await _unitOfWork.Initiatives.AddAsync(initiative);
            }
        }

        /// <summary>Maps a mock Epic, including its own comments and its WorkItems, into an <see cref="Entities.Epic"/>.</summary>
        /// <param name="mockEpic">The deserialized mock Epic.</param>
        internal static Entities.Epic MapEpic(MockEpic mockEpic)
        {
            var epic = new Entities.Epic
            {
                JiraId = mockEpic.Id,
                Title = mockEpic.Title,
                Comments = mockEpic.Comments.Select(MapComment).ToList()
            };

            foreach (var mockItem in mockEpic.Items)
                epic.WorkItems.Add(MapWorkItem(mockItem));

            return epic;
        }

        /// <summary>Maps a direct Epic child (Story/Bug/Task/Spike), including its Subtask/StoryBug children, into a <see cref="Entities.WorkItem"/>.</summary>
        /// <param name="mockItem">The deserialized mock work item.</param>
        internal static Entities.WorkItem MapWorkItem(MockWorkItem mockItem)
        {
            var workItem = new Entities.WorkItem
            {
                JiraId = mockItem.Id,
                Type = mockItem.Type,
                Title = mockItem.Title,
                Assignee = mockItem.Assignee,
                Status = mockItem.Status,
                Comments = mockItem.Comments.Select(MapComment).ToList()
            };

            foreach (var mockSubtask in mockItem.Subtasks)
                workItem.Children.Add(MapChildWorkItem(mockSubtask, "Subtask"));

            foreach (var mockStoryBug in mockItem.StoryBugs)
                workItem.Children.Add(MapChildWorkItem(mockStoryBug, "StoryBug"));

            return workItem;
        }

        /// <summary>Maps a Subtask or StoryBug into a <see cref="Entities.WorkItem"/> with the given type discriminator.</summary>
        /// <param name="mockSubItem">The deserialized mock sub-item.</param>
        /// <param name="type">The type discriminator to assign: "Subtask" or "StoryBug".</param>
        internal static Entities.WorkItem MapChildWorkItem(MockSubItem mockSubItem, string type)
        {
            return new Entities.WorkItem
            {
                JiraId = mockSubItem.Id,
                Type = type,
                Title = mockSubItem.Title,
                Assignee = mockSubItem.Assignee,
                Status = mockSubItem.Status,
                Comments = mockSubItem.Comments.Select(MapComment).ToList()
            };
        }

        /// <summary>Maps a mock comment into a <see cref="Entities.Comment"/> (parent FK left unset - the caller assigns it via navigation).</summary>
        /// <param name="mockComment">The deserialized mock comment.</param>
        internal static Entities.Comment MapComment(MockComment mockComment)
        {
            return new Entities.Comment
            {
                Author = mockComment.Author,
                Role = mockComment.Role,
                Timestamp = mockComment.Timestamp,
                Text = mockComment.Text
            };
        }

        #endregion
    }
}
