using FluentAssertions;
using JiraRollupAgent.DAL.Models;
using JiraRollupAgent.DAL.Repositories.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;
using SummarizationServiceType = JiraRollupAgent.Services.SummarizationService.SummarizationService;

namespace JiraRollupAgent.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SummarizationServiceType"/>: date-range validation, the
    /// no-activity/header/user-message building helpers, the LLM call wrapper, the bottom-up
    /// per-ticket summarization methods (mocked <see cref="IChatClient"/>), persistence, and the
    /// <c>Run()</c> skip/success paths (mocked <see cref="IUnitOfWork"/>).
    /// </summary>
    public class SummarizationServiceTests
    {
        /// <summary>Builds an <see cref="IConfiguration"/> backed by an in-memory dictionary of the given key/value pairs.</summary>
        /// <param name="values">The <c>AppSettings:*</c> key/value pairs to seed.</param>
        private static IConfiguration BuildConfig(params (string Key, string Value)[] values)
        {
            var dict = new Dictionary<string, string?>();
            foreach (var (key, value) in values)
                dict[key] = value;

            return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        }

        /// <summary>Builds a <see cref="Comment"/> with the given role/text/timestamp for test fixtures.</summary>
        private static Comment MakeComment(string role, string text, DateTime timestamp, string author = "Test Author")
            => new() { Author = author, Role = role, Timestamp = timestamp, Text = text };

        /// <summary>Creates a service instance, defaulting the unit of work/chat client to loose mocks when not supplied.</summary>
        private static SummarizationServiceType CreateService(IConfiguration config, IChatClient? chatClient = null, IUnitOfWork? unitOfWork = null)
            => new(config, unitOfWork ?? Mock.Of<IUnitOfWork>(), chatClient ?? Mock.Of<IChatClient>());

        #region ValidateDateRange

        /// <summary>A valid range with an in-range comment returns the start as-is and the end normalized to the last tick of that calendar day.</summary>
        [Fact]
        public void ValidateDateRange_WithValidRangeAndInRangeComments_ReturnsNormalizedRange()
        {
            // Arrange
            var config = BuildConfig(("AppSettings:SummaryRangeStart", "2026-07-01"), ("AppSettings:SummaryRangeEnd", "2026-07-31"));
            var service = CreateService(config);
            var comments = new List<Comment> { MakeComment("Dev", "hi", new DateTime(2026, 7, 15)) };

            // Act
            var (start, end) = service.ValidateDateRange(comments);

            // Assert
            start.Should().Be(new DateTime(2026, 7, 1));
            end.Should().Be(new DateTime(2026, 7, 31).AddDays(1).AddTicks(-1));
        }

        /// <summary>A start date after the end date is rejected rather than silently swapped.</summary>
        [Fact]
        public void ValidateDateRange_WithStartAfterEnd_Throws()
        {
            // Arrange
            var config = BuildConfig(("AppSettings:SummaryRangeStart", "2026-08-01"), ("AppSettings:SummaryRangeEnd", "2026-07-01"));
            var service = CreateService(config);
            var comments = new List<Comment> { MakeComment("Dev", "hi", new DateTime(2026, 7, 15)) };

            // Act
            Action act = () => service.ValidateDateRange(comments);

            // Assert
            act.Should().Throw<InvalidOperationException>().WithMessage("*after*");
        }

        /// <summary>A missing SummaryRangeStart config key fails fast with a clear message rather than defaulting.</summary>
        [Fact]
        public void ValidateDateRange_WithMissingStartKey_Throws()
        {
            // Arrange
            var config = BuildConfig(("AppSettings:SummaryRangeEnd", "2026-07-31"));
            var service = CreateService(config);
            var comments = new List<Comment> { MakeComment("Dev", "hi", new DateTime(2026, 7, 15)) };

            // Act
            Action act = () => service.ValidateDateRange(comments);

            // Assert
            act.Should().Throw<InvalidOperationException>().WithMessage("*SummaryRangeStart*");
        }

        /// <summary>An unparseable SummaryRangeStart value fails fast with a clear message rather than throwing a raw FormatException later.</summary>
        [Fact]
        public void ValidateDateRange_WithUnparsableStartDate_Throws()
        {
            // Arrange
            var config = BuildConfig(("AppSettings:SummaryRangeStart", "not-a-date"), ("AppSettings:SummaryRangeEnd", "2026-07-31"));
            var service = CreateService(config);
            var comments = new List<Comment> { MakeComment("Dev", "hi", new DateTime(2026, 7, 15)) };

            // Act
            Action act = () => service.ValidateDateRange(comments);

            // Assert
            act.Should().Throw<InvalidOperationException>().WithMessage("*SummaryRangeStart*not a valid date*");
        }

        /// <summary>An unparseable SummaryRangeEnd value fails fast with a clear message rather than throwing a raw FormatException later.</summary>
        [Fact]
        public void ValidateDateRange_WithUnparsableEndDate_Throws()
        {
            // Arrange
            var config = BuildConfig(("AppSettings:SummaryRangeStart", "2026-07-01"), ("AppSettings:SummaryRangeEnd", "not-a-date"));
            var service = CreateService(config);
            var comments = new List<Comment> { MakeComment("Dev", "hi", new DateTime(2026, 7, 15)) };

            // Act
            Action act = () => service.ValidateDateRange(comments);

            // Assert
            act.Should().Throw<InvalidOperationException>().WithMessage("*SummaryRangeEnd*not a valid date*");
        }

        /// <summary>An empty comment list (hierarchy not loaded yet) fails fast rather than proceeding with zero data.</summary>
        [Fact]
        public void ValidateDateRange_WithNoCommentsInDatabase_Throws()
        {
            // Arrange
            var config = BuildConfig(("AppSettings:SummaryRangeStart", "2026-07-01"), ("AppSettings:SummaryRangeEnd", "2026-07-31"));
            var service = CreateService(config);

            // Act
            Action act = () => service.ValidateDateRange(new List<Comment>());

            // Assert
            act.Should().Throw<InvalidOperationException>().WithMessage("*hierarchy been loaded*");
        }

        /// <summary>A configured range entirely outside the data's actual timestamp span fails fast instead of silently generating empty summaries.</summary>
        [Fact]
        public void ValidateDateRange_WithRangeOutsideDataSpan_Throws()
        {
            // Arrange
            var config = BuildConfig(("AppSettings:SummaryRangeStart", "2026-01-01"), ("AppSettings:SummaryRangeEnd", "2026-01-31"));
            var service = CreateService(config);
            var comments = new List<Comment> { MakeComment("Dev", "hi", new DateTime(2026, 7, 15)) };

            // Act
            Action act = () => service.ValidateDateRange(comments);

            // Assert
            act.Should().Throw<InvalidOperationException>().WithMessage("*No comments found between*");
        }

        #endregion

        #region HasNoActivity

        /// <summary>No own comments and no children means there is nothing to summarize.</summary>
        [Fact]
        public void HasNoActivity_WithNoOwnCommentsAndNoChildren_ReturnsTrue()
        {
            // Arrange
            var ownComments = new List<Comment>();
            var childSummaries = new List<(string Label, string SummaryText)>();

            // Act
            var result = SummarizationServiceType.HasNoActivity(ownComments, childSummaries);

            // Assert
            result.Should().BeTrue();
        }

        /// <summary>Any own comments mean there is activity to summarize, regardless of children.</summary>
        [Fact]
        public void HasNoActivity_WithOwnComments_ReturnsFalse()
        {
            // Arrange
            var ownComments = new List<Comment> { MakeComment("Dev", "hi", new DateTime(2026, 7, 10)) };
            var childSummaries = new List<(string Label, string SummaryText)>();

            // Act
            var result = SummarizationServiceType.HasNoActivity(ownComments, childSummaries);

            // Assert
            result.Should().BeFalse();
        }

        /// <summary>When every child summary is itself the placeholder and there are no own comments, there is nothing to summarize.</summary>
        [Fact]
        public void HasNoActivity_WithAllChildSummariesPlaceholder_ReturnsTrue()
        {
            // Arrange
            var ownComments = new List<Comment>();
            var childSummaries = new List<(string Label, string SummaryText)>
            {
                ("A", SummarizationServiceType.NoActivityPlaceholder),
                ("B", SummarizationServiceType.NoActivityPlaceholder)
            };

            // Act
            var result = SummarizationServiceType.HasNoActivity(ownComments, childSummaries);

            // Assert
            result.Should().BeTrue();
        }

        /// <summary>A single non-placeholder child summary is enough to count as real activity.</summary>
        [Fact]
        public void HasNoActivity_WithOneNonPlaceholderChildSummary_ReturnsFalse()
        {
            // Arrange
            var ownComments = new List<Comment>();
            var childSummaries = new List<(string Label, string SummaryText)>
            {
                ("A", SummarizationServiceType.NoActivityPlaceholder),
                ("B", "Real work happened.")
            };

            // Act
            var result = SummarizationServiceType.HasNoActivity(ownComments, childSummaries);

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region Header builders

        /// <summary>BuildTicketHeader formats the type, Jira id, title, status, and assignee into one identity line.</summary>
        [Fact]
        public void BuildTicketHeader_FormatsTypeIdTitleStatusAssignee()
        {
            // Arrange
            var item = new WorkItem { Type = "Bug", JiraId = "BUG-1", Title = "Something broke", Status = "Open", Assignee = "Dev One" };

            // Act
            var result = SummarizationServiceType.BuildTicketHeader(item);

            // Assert
            result.Should().Be("Ticket: Bug BUG-1 — \"Something broke\" (Status: Open, Assignee: Dev One)");
        }

        /// <summary>BuildEpicHeader formats the Jira id and title into one identity line.</summary>
        [Fact]
        public void BuildEpicHeader_FormatsIdAndTitle()
        {
            // Arrange
            var epic = new Epic { JiraId = "EPIC-1", Title = "Glass Cockpit" };

            // Act
            var result = SummarizationServiceType.BuildEpicHeader(epic);

            // Assert
            result.Should().Be("Epic EPIC-1 — \"Glass Cockpit\"");
        }

        /// <summary>BuildInitiativeHeader formats the Jira id, title, priority rank, and status into one identity line.</summary>
        [Fact]
        public void BuildInitiativeHeader_FormatsIdTitlePriorityStatus()
        {
            // Arrange
            var initiative = new Initiative { JiraId = "INIT-1", Title = "Cockpit Avionics", PriorityRank = 1, Status = "In Progress" };

            // Act
            var result = SummarizationServiceType.BuildInitiativeHeader(initiative);

            // Assert
            result.Should().Be("Initiative INIT-1 — \"Cockpit Avionics\" (Priority Rank: 1, Status: In Progress)");
        }

        #endregion

        #region User message builders

        /// <summary>BuildLeafUserMessage orders comments oldest-first in the prompt text regardless of the order they were passed in.</summary>
        [Fact]
        public void BuildLeafUserMessage_OrdersCommentsOldestFirstRegardlessOfInputOrder()
        {
            // Arrange
            var header = "Ticket: Bug BUG-1 — \"X\" (Status: Open, Assignee: Dev)";
            var comments = new List<Comment>
            {
                MakeComment("Dev", "third", new DateTime(2026, 7, 20)),
                MakeComment("Dev", "first", new DateTime(2026, 7, 10)),
                MakeComment("Dev", "second", new DateTime(2026, 7, 15))
            };

            // Act
            var result = SummarizationServiceType.BuildLeafUserMessage(header, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), comments);

            // Assert
            var firstIndex = result.IndexOf("first", StringComparison.Ordinal);
            var secondIndex = result.IndexOf("second", StringComparison.Ordinal);
            var thirdIndex = result.IndexOf("third", StringComparison.Ordinal);
            firstIndex.Should().BeLessThan(secondIndex);
            secondIndex.Should().BeLessThan(thirdIndex);
            result.Should().Contain(header);
            result.Should().Contain("2026-07-01");
            result.Should().Contain("2026-07-31");
        }

        /// <summary>BuildRollupUserMessage includes the ticket header, own comments under their label, and child summaries under their label.</summary>
        [Fact]
        public void BuildRollupUserMessage_IncludesOwnCommentsAndLabeledChildSummaries()
        {
            // Arrange
            var header = "Epic EPIC-1 — \"Glass Cockpit\"";
            var ownComments = new List<Comment> { MakeComment("Dev", "own comment text", new DateTime(2026, 7, 10)) };
            var childSummaries = new List<(string Label, string SummaryText)> { ("Story \"S1\"", "Child summary text") };

            // Act
            var result = SummarizationServiceType.BuildRollupUserMessage(
                header, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31),
                "Comments on this Epic directly:", ownComments,
                "Work item summaries:", childSummaries);

            // Assert
            result.Should().Contain(header);
            result.Should().Contain("Comments on this Epic directly:");
            result.Should().Contain("own comment text");
            result.Should().Contain("Work item summaries:");
            result.Should().Contain("- [Story \"S1\"] Child summary text");
        }

        #endregion

        #region GetSummaryAsync

        /// <summary>GetSummaryAsync sends exactly a System message (the prompt) and a User message (the built prompt text), and returns the model's response text.</summary>
        [Fact]
        public async Task GetSummaryAsync_SendsSystemAndUserMessages_ReturnsResponseText()
        {
            // Arrange
            List<ChatMessage>? captured = null;
            var chatClient = new Mock<IChatClient>();
            chatClient
                .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((msgs, _, _) => captured = msgs.ToList())
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Canned summary text.")));
            var service = CreateService(BuildConfig(), chatClient.Object);

            // Act
            var result = await service.GetSummaryAsync("SYSTEM PROMPT", "USER MESSAGE");

            // Assert
            result.Should().Be("Canned summary text.");
            captured.Should().HaveCount(2);
            captured![0].Role.Should().Be(ChatRole.System);
            captured[0].Text.Should().Be("SYSTEM PROMPT");
            captured[1].Role.Should().Be(ChatRole.User);
            captured[1].Text.Should().Be("USER MESSAGE");
        }

        #endregion

        #region Summarize chain

        /// <summary>A leaf ticket with zero in-range comments skips the LLM call entirely and returns the placeholder.</summary>
        [Fact]
        public async Task SummarizeLeafAsync_WithNoComments_ReturnsPlaceholderWithoutCallingLlm()
        {
            // Arrange
            var chatClient = new Mock<IChatClient>(MockBehavior.Strict);
            var service = CreateService(BuildConfig(), chatClient.Object);
            var item = new WorkItem { Id = 1, Type = "Bug", JiraId = "BUG-1", Title = "X", Assignee = "Dev", Status = "Open" };
            var data = new SummarizationServiceType.HierarchyData
            {
                Initiatives = [],
                EpicsByInitiativeId = [],
                TopLevelItemsByEpicId = [],
                ChildrenByParentWorkItemId = [],
                CommentsByInitiativeId = [],
                CommentsByEpicId = [],
                CommentsByWorkItemId = []
            };

            // Act
            var result = await service.SummarizeLeafAsync(item, data, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31));

            // Assert
            result.Should().Be(SummarizationServiceType.NoActivityPlaceholder);
        }

        /// <summary>A leaf ticket with in-range comments calls the LLM exactly once and returns its response.</summary>
        [Fact]
        public async Task SummarizeLeafAsync_WithComments_CallsLlmOnceAndReturnsSummary()
        {
            // Arrange
            var chatClient = new Mock<IChatClient>();
            chatClient
                .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Leaf summary.")));
            var service = CreateService(BuildConfig(), chatClient.Object);
            var item = new WorkItem { Id = 1, Type = "Bug", JiraId = "BUG-1", Title = "X", Assignee = "Dev", Status = "Open" };
            var data = new SummarizationServiceType.HierarchyData
            {
                Initiatives = [],
                EpicsByInitiativeId = [],
                TopLevelItemsByEpicId = [],
                ChildrenByParentWorkItemId = [],
                CommentsByInitiativeId = [],
                CommentsByEpicId = [],
                CommentsByWorkItemId = new Dictionary<int, List<Comment>> { [1] = [MakeComment("Dev", "hi", new DateTime(2026, 7, 10))] }
            };

            // Act
            var result = await service.SummarizeLeafAsync(item, data, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31));

            // Assert
            result.Should().Be("Leaf summary.");
            chatClient.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once());
        }

        /// <summary>A Story summarizes its Subtask/StoryBug children first (writing their summaries into the shared accumulator) before rolling those up into its own summary.</summary>
        [Fact]
        public async Task SummarizeStoryAsync_WithChildrenAndOwnComments_SummarizesChildrenFirstThenRollsUp()
        {
            // Arrange
            var chatClient = new Mock<IChatClient>();
            chatClient
                .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Rollup summary.")));
            var service = CreateService(BuildConfig(), chatClient.Object);
            var story = new WorkItem { Id = 100, Type = "Story", JiraId = "STORY-1", Title = "S", Assignee = "Dev", Status = "In Progress" };
            var subtask = new WorkItem { Id = 101, Type = "Subtask", JiraId = "SUB-1", Title = "Sub", Assignee = "Dev", Status = "Resolved" };
            var data = new SummarizationServiceType.HierarchyData
            {
                Initiatives = [],
                EpicsByInitiativeId = [],
                TopLevelItemsByEpicId = [],
                ChildrenByParentWorkItemId = new Dictionary<int, List<WorkItem>> { [100] = [subtask] },
                CommentsByInitiativeId = [],
                CommentsByEpicId = [],
                CommentsByWorkItemId = new Dictionary<int, List<Comment>>
                {
                    [100] = [MakeComment("Dev", "story comment", new DateTime(2026, 7, 10))],
                    [101] = [MakeComment("Dev", "subtask comment", new DateTime(2026, 7, 11))]
                }
            };
            var workItemSummaries = new Dictionary<int, string>();

            // Act
            var result = await service.SummarizeStoryAsync(story, data, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), workItemSummaries);

            // Assert
            result.Should().Be("Rollup summary.");
            workItemSummaries.Should().ContainKey(101).WhoseValue.Should().Be("Rollup summary.");
            chatClient.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>A Story with no own comments and no children skips the LLM call entirely and returns the placeholder.</summary>
        [Fact]
        public async Task SummarizeStoryAsync_WithNoActivity_ReturnsPlaceholderWithoutCallingLlm()
        {
            // Arrange
            var chatClient = new Mock<IChatClient>(MockBehavior.Strict);
            var service = CreateService(BuildConfig(), chatClient.Object);
            var story = new WorkItem { Id = 100, Type = "Story", JiraId = "STORY-1", Title = "S", Assignee = "Dev", Status = "In Progress" };
            var data = new SummarizationServiceType.HierarchyData
            {
                Initiatives = [],
                EpicsByInitiativeId = [],
                TopLevelItemsByEpicId = [],
                ChildrenByParentWorkItemId = [],
                CommentsByInitiativeId = [],
                CommentsByEpicId = [],
                CommentsByWorkItemId = []
            };
            var workItemSummaries = new Dictionary<int, string>();

            // Act
            var result = await service.SummarizeStoryAsync(story, data, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), workItemSummaries);

            // Assert
            result.Should().Be(SummarizationServiceType.NoActivityPlaceholder);
        }

        /// <summary>An Epic with real activity calls the LLM using the Engineering Summary (Dev/QA-weighted) system prompt.</summary>
        [Fact]
        public async Task SummarizeEpicAsync_WithActivity_SelectsEngineeringSystemPrompt()
        {
            // Arrange
            List<ChatMessage>? captured = null;
            var chatClient = new Mock<IChatClient>();
            chatClient
                .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((msgs, _, _) => captured = msgs.ToList())
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Engineering summary text.")));
            var service = CreateService(BuildConfig(), chatClient.Object);
            var epic = new Epic { Id = 10, JiraId = "EPIC-1", Title = "Glass Cockpit", InitiativeId = 1 };
            var bug = new WorkItem { Id = 20, Type = "Bug", JiraId = "BUG-1", Title = "B", Assignee = "Dev", Status = "Open" };
            var data = new SummarizationServiceType.HierarchyData
            {
                Initiatives = [],
                EpicsByInitiativeId = [],
                TopLevelItemsByEpicId = new Dictionary<int, List<WorkItem>> { [10] = [bug] },
                ChildrenByParentWorkItemId = [],
                CommentsByInitiativeId = [],
                CommentsByEpicId = new Dictionary<int, List<Comment>> { [10] = [MakeComment("EngineeringManager", "epic comment", new DateTime(2026, 7, 10))] },
                CommentsByWorkItemId = new Dictionary<int, List<Comment>> { [20] = [MakeComment("Dev", "bug comment", new DateTime(2026, 7, 11))] }
            };
            var workItemSummaries = new Dictionary<int, string>();

            // Act
            var result = await service.SummarizeEpicAsync(epic, data, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), workItemSummaries);

            // Assert
            result.Should().Be("Engineering summary text.");
            captured.Should().NotBeNull();
            captured!.First(m => m.Role == ChatRole.System).Text.Should().Contain("Engineering Summary");
        }

        /// <summary>An Epic with no own comments and no work items skips the LLM call entirely and returns the placeholder.</summary>
        [Fact]
        public async Task SummarizeEpicAsync_WithNoActivity_ReturnsPlaceholderWithoutCallingLlm()
        {
            // Arrange
            var chatClient = new Mock<IChatClient>(MockBehavior.Strict);
            var service = CreateService(BuildConfig(), chatClient.Object);
            var epic = new Epic { Id = 10, JiraId = "EPIC-1", Title = "Glass Cockpit", InitiativeId = 1 };
            var data = new SummarizationServiceType.HierarchyData
            {
                Initiatives = [],
                EpicsByInitiativeId = [],
                TopLevelItemsByEpicId = [],
                ChildrenByParentWorkItemId = [],
                CommentsByInitiativeId = [],
                CommentsByEpicId = [],
                CommentsByWorkItemId = []
            };
            var workItemSummaries = new Dictionary<int, string>();

            // Act
            var result = await service.SummarizeEpicAsync(epic, data, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), workItemSummaries);

            // Assert
            result.Should().Be(SummarizationServiceType.NoActivityPlaceholder);
        }

        /// <summary>An Initiative with real activity calls the LLM using the Business Summary (ScrumMaster/Stakeholder/EM-weighted) system prompt.</summary>
        [Fact]
        public async Task SummarizeInitiativeAsync_WithActivity_SelectsBusinessSystemPrompt()
        {
            // Arrange
            List<ChatMessage>? captured = null;
            var chatClient = new Mock<IChatClient>();
            chatClient
                .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((msgs, _, _) => captured = msgs.ToList())
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Business summary text.")));
            var service = CreateService(BuildConfig(), chatClient.Object);
            var initiative = new Initiative { Id = 1, JiraId = "INIT-1", Title = "Cockpit Avionics", PriorityRank = 1, Status = "In Progress" };
            var epic = new Epic { Id = 10, JiraId = "EPIC-1", Title = "Glass Cockpit", InitiativeId = 1 };
            var data = new SummarizationServiceType.HierarchyData
            {
                Initiatives = [],
                EpicsByInitiativeId = new Dictionary<int, List<Epic>> { [1] = [epic] },
                TopLevelItemsByEpicId = [],
                ChildrenByParentWorkItemId = [],
                CommentsByInitiativeId = new Dictionary<int, List<Comment>> { [1] = [MakeComment("Stakeholder", "initiative comment", new DateTime(2026, 7, 10))] },
                CommentsByEpicId = new Dictionary<int, List<Comment>> { [10] = [MakeComment("Dev", "epic comment", new DateTime(2026, 7, 11))] },
                CommentsByWorkItemId = []
            };
            var workItemSummaries = new Dictionary<int, string>();
            var epicSummaries = new Dictionary<int, string>();

            // Act
            var result = await service.SummarizeInitiativeAsync(initiative, data, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), workItemSummaries, epicSummaries);

            // Assert
            result.Should().Be("Business summary text.");
            captured.Should().NotBeNull();
            captured!.First(m => m.Role == ChatRole.System).Text.Should().Contain("Business Summary");
            epicSummaries.Should().ContainKey(10);
        }

        /// <summary>An Initiative with no own comments and no Epics skips the LLM call entirely and returns the placeholder.</summary>
        [Fact]
        public async Task SummarizeInitiativeAsync_WithNoActivity_ReturnsPlaceholderWithoutCallingLlm()
        {
            // Arrange
            var chatClient = new Mock<IChatClient>(MockBehavior.Strict);
            var service = CreateService(BuildConfig(), chatClient.Object);
            var initiative = new Initiative { Id = 1, JiraId = "INIT-1", Title = "Cockpit Avionics", PriorityRank = 1, Status = "In Progress" };
            var data = new SummarizationServiceType.HierarchyData
            {
                Initiatives = [],
                EpicsByInitiativeId = [],
                TopLevelItemsByEpicId = [],
                ChildrenByParentWorkItemId = [],
                CommentsByInitiativeId = [],
                CommentsByEpicId = [],
                CommentsByWorkItemId = []
            };
            var workItemSummaries = new Dictionary<int, string>();
            var epicSummaries = new Dictionary<int, string>();

            // Act
            var result = await service.SummarizeInitiativeAsync(initiative, data, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), workItemSummaries, epicSummaries);

            // Assert
            result.Should().Be(SummarizationServiceType.NoActivityPlaceholder);
        }

        #endregion

        #region PersistSummariesAsync

        /// <summary>PersistSummariesAsync clears the three summary tables before inserting, adds exactly one row per dictionary entry, and commits once.</summary>
        [Fact]
        public async Task PersistSummariesAsync_DeletesThenInsertsOneRowPerSummary()
        {
            // Arrange
            var unitOfWork = new Mock<IUnitOfWork>();
            var callOrder = new List<string>();
            unitOfWork.Setup(u => u.DeleteAllSummariesAsync())
                .Callback(() => callOrder.Add("delete"))
                .Returns(Task.CompletedTask);
            unitOfWork.Setup(u => u.WorkItemSummaries.AddAsync(It.IsAny<WorkItemSummary>()))
                .Callback(() => callOrder.Add("addWorkItem"))
                .Returns(Task.CompletedTask);
            unitOfWork.Setup(u => u.EpicEngineeringSummaries.AddAsync(It.IsAny<EpicEngineeringSummary>()))
                .Callback(() => callOrder.Add("addEpic"))
                .Returns(Task.CompletedTask);
            unitOfWork.Setup(u => u.InitiativeBusinessSummaries.AddAsync(It.IsAny<InitiativeBusinessSummary>()))
                .Callback(() => callOrder.Add("addInitiative"))
                .Returns(Task.CompletedTask);
            unitOfWork.Setup(u => u.CompleteAsync()).ReturnsAsync(1);
            var service = CreateService(BuildConfig(), unitOfWork: unitOfWork.Object);
            var workItemSummaries = new Dictionary<int, string> { [1] = "wi summary" };
            var epicSummaries = new Dictionary<int, string> { [2] = "epic summary" };
            var initiativeSummaries = new Dictionary<int, string> { [3] = "init summary" };

            // Act
            await service.PersistSummariesAsync(workItemSummaries, epicSummaries, initiativeSummaries, new DateTime(2026, 7, 1), new DateTime(2026, 7, 31));

            // Assert
            callOrder.First().Should().Be("delete");
            unitOfWork.Verify(u => u.WorkItemSummaries.AddAsync(It.IsAny<WorkItemSummary>()), Times.Once());
            unitOfWork.Verify(u => u.EpicEngineeringSummaries.AddAsync(It.IsAny<EpicEngineeringSummary>()), Times.Once());
            unitOfWork.Verify(u => u.InitiativeBusinessSummaries.AddAsync(It.IsAny<InitiativeBusinessSummary>()), Times.Once());
            unitOfWork.Verify(u => u.CompleteAsync(), Times.Once());
        }

        #endregion

        #region DisableSummarizationFlagAsync

        /// <summary>DisableSummarizationFlagAsync leaves the file untouched and just logs a warning when the JSON has no "AppSettings" section.</summary>
        [Fact]
        public async Task DisableSummarizationFlagAsync_WithNoAppSettingsSection_LeavesFileUnchanged()
        {
            // Arrange
            var path = Path.GetTempFileName();
            try
            {
                const string original = "{\"SomethingElse\":{}}";
                await File.WriteAllTextAsync(path, original);
                var service = CreateService(BuildConfig());

                // Act
                await service.DisableSummarizationFlagAsync(path);

                // Assert
                (await File.ReadAllTextAsync(path)).Should().Be(original);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>DisableSummarizationFlagAsync swallows a read/parse failure (e.g. malformed JSON) rather than propagating it.</summary>
        [Fact]
        public async Task DisableSummarizationFlagAsync_WithMalformedJson_DoesNotThrow()
        {
            // Arrange
            var path = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(path, "{ not valid json");
                var service = CreateService(BuildConfig());

                // Act
                Func<Task> act = () => service.DisableSummarizationFlagAsync(path);

                // Assert
                await act.Should().NotThrowAsync();
            }
            finally
            {
                File.Delete(path);
            }
        }

        #endregion

        #region Run

        /// <summary>Run() returns true and never touches its dependencies when AppSettings:RunSummarization is false.</summary>
        [Fact]
        public async Task Run_WhenFlagIsFalse_ReturnsTrueWithoutTouchingDependencies()
        {
            // Arrange
            var config = BuildConfig(("AppSettings:RunSummarization", "false"));
            var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
            var chatClient = new Mock<IChatClient>(MockBehavior.Strict);
            var service = new SummarizationServiceType(config, unitOfWork.Object, chatClient.Object);

            // Act
            var result = await service.Run();

            // Assert
            result.Should().BeTrue();
        }

        /// <summary>Run() catches an exception thrown by a dependency, logs it, and returns false rather than propagating it.</summary>
        [Fact]
        public async Task Run_WhenDependencyThrows_ReturnsFalse()
        {
            // Arrange
            var config = BuildConfig(
                ("AppSettings:RunSummarization", "true"),
                ("AppSettings:SummaryRangeStart", "2026-07-01"),
                ("AppSettings:SummaryRangeEnd", "2026-07-31"));
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.Comments.GetAllAsync()).ThrowsAsync(new InvalidOperationException("boom"));
            var service = new SummarizationServiceType(config, unitOfWork.Object, Mock.Of<IChatClient>());

            // Act
            var result = await service.Run();

            // Assert
            result.Should().BeFalse();
        }

        /// <summary>Run() walks a full 1-Initiative/1-Epic/1-WorkItem hierarchy end-to-end, persisting exactly one summary at each level and returning true.</summary>
        [Fact]
        public async Task Run_WithFullHierarchyAndFlagTrue_PersistsSummariesAndReturnsTrue()
        {
            // Arrange
            var config = BuildConfig(
                ("AppSettings:RunSummarization", "true"),
                ("AppSettings:SummaryRangeStart", "2026-07-01"),
                ("AppSettings:SummaryRangeEnd", "2026-07-31"));

            var initiative = new Initiative { Id = 1, JiraId = "INIT-1", Title = "Cockpit Avionics", PriorityRank = 1, Status = "In Progress" };
            var epic = new Epic { Id = 10, JiraId = "EPIC-1", Title = "Glass Cockpit", InitiativeId = 1 };
            var story = new WorkItem { Id = 100, Type = "Story", JiraId = "STORY-1", Title = "S", Assignee = "Dev", Status = "In Progress", EpicId = 10 };
            var subtask = new WorkItem { Id = 101, Type = "Subtask", JiraId = "SUB-1", Title = "Sub", Assignee = "Dev", Status = "Resolved", ParentWorkItemId = 100 };

            // One comment at every level (Initiative/Epic/WorkItem/Subtask) so LoadHierarchyDataAsync's
            // grouping lambdas for each of CommentsByInitiativeId/CommentsByEpicId/CommentsByWorkItemId
            // and ChildrenByParentWorkItemId all actually run, not just the WorkItem-level one.
            var initiativeComment = MakeComment("Stakeholder", "initiative-level comment", new DateTime(2026, 7, 12));
            initiativeComment.InitiativeId = 1;
            var epicComment = MakeComment("EngineeringManager", "epic-level comment", new DateTime(2026, 7, 13));
            epicComment.EpicId = 10;
            var storyComment = MakeComment("Dev", "story comment", new DateTime(2026, 7, 14));
            storyComment.WorkItemId = 100;
            var subtaskComment = MakeComment("Dev", "subtask comment", new DateTime(2026, 7, 15));
            subtaskComment.WorkItemId = 101;

            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.Comments.GetAllAsync())
                .ReturnsAsync(new List<Comment> { initiativeComment, epicComment, storyComment, subtaskComment });
            unitOfWork.Setup(u => u.Initiatives.GetAllAsync()).ReturnsAsync(new List<Initiative> { initiative });
            unitOfWork.Setup(u => u.Epics.GetAllAsync()).ReturnsAsync(new List<Epic> { epic });
            unitOfWork.Setup(u => u.WorkItems.GetAllAsync()).ReturnsAsync(new List<WorkItem> { story, subtask });
            unitOfWork.Setup(u => u.DeleteAllSummariesAsync()).Returns(Task.CompletedTask);
            unitOfWork.Setup(u => u.CompleteAsync()).ReturnsAsync(1);

            var addedWorkItemSummaries = new List<WorkItemSummary>();
            var addedEpicSummaries = new List<EpicEngineeringSummary>();
            var addedInitiativeSummaries = new List<InitiativeBusinessSummary>();
            unitOfWork.Setup(u => u.WorkItemSummaries.AddAsync(It.IsAny<WorkItemSummary>()))
                .Callback<WorkItemSummary>(s => addedWorkItemSummaries.Add(s))
                .Returns(Task.CompletedTask);
            unitOfWork.Setup(u => u.EpicEngineeringSummaries.AddAsync(It.IsAny<EpicEngineeringSummary>()))
                .Callback<EpicEngineeringSummary>(s => addedEpicSummaries.Add(s))
                .Returns(Task.CompletedTask);
            unitOfWork.Setup(u => u.InitiativeBusinessSummaries.AddAsync(It.IsAny<InitiativeBusinessSummary>()))
                .Callback<InitiativeBusinessSummary>(s => addedInitiativeSummaries.Add(s))
                .Returns(Task.CompletedTask);

            var chatClient = new Mock<IChatClient>();
            chatClient
                .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Canned summary.")));

            var service = new SummarizationServiceType(config, unitOfWork.Object, chatClient.Object);

            // Act
            var result = await service.Run();

            // Assert
            result.Should().BeTrue();
            addedWorkItemSummaries.Should().HaveCount(2);
            addedEpicSummaries.Should().HaveCount(1);
            addedInitiativeSummaries.Should().HaveCount(1);
            unitOfWork.Verify(u => u.DeleteAllSummariesAsync(), Times.Once());
            unitOfWork.Verify(u => u.CompleteAsync(), Times.Once());
        }

        #endregion
    }
}
