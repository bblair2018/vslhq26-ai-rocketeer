using System.Diagnostics.CodeAnalysis;
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
    [ExcludeFromCodeCoverage]
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

        public JiraHierarchyLoaderService(IConfiguration config, IUnitOfWork unitOfWork)
        {
            _config = config;
            _unitOfWork = unitOfWork;
        }

        #region Run

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
        private async Task DisableHierarchyLoadFlagAsync()
        {
            try
            {
                var appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
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

        private static async Task<T> ReadJsonAsync<T>(string path) where T : new()
        {
            await using var stream = File.OpenRead(path);
            var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
            return result ?? new T();
        }

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

        private static Entities.Epic MapEpic(MockEpic mockEpic)
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

        private static Entities.WorkItem MapWorkItem(MockWorkItem mockItem)
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

        private static Entities.WorkItem MapChildWorkItem(MockSubItem mockSubItem, string type)
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

        private static Entities.Comment MapComment(MockComment mockComment)
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
