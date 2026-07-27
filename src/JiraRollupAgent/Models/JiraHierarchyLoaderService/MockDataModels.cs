using System.Diagnostics.CodeAnalysis;

namespace JiraRollupAgent.Models.JiraHierarchyLoaderService
{
    /// <summary>
    /// Deserialization targets for MockData/jira-hierarchy.json and MockData/team-roster.json.
    /// Property names match the JSON fields case-insensitively (System.Text.Json PropertyNameCaseInsensitive).
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class MockJiraHierarchy
    {
        public List<MockInitiative> Initiatives { get; set; } = new();
    }

    [ExcludeFromCodeCoverage]
    public class MockInitiative
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int PriorityRank { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<MockComment> Comments { get; set; } = new();
        public List<MockEpic> Epics { get; set; } = new();
    }

    [ExcludeFromCodeCoverage]
    public class MockEpic
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public List<MockComment> Comments { get; set; } = new();
        public List<MockWorkItem> Items { get; set; } = new();
    }

    /// <summary>A direct child of an Epic: Story, Bug, Task, or Spike.</summary>
    [ExcludeFromCodeCoverage]
    public class MockWorkItem
    {
        public string Type { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Assignee { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<MockComment> Comments { get; set; } = new();

        /// <summary>Only ever populated when <see cref="Type"/> is "Story".</summary>
        public List<MockSubItem> Subtasks { get; set; } = new();

        /// <summary>Only ever populated when <see cref="Type"/> is "Story".</summary>
        public List<MockSubItem> StoryBugs { get; set; } = new();
    }

    /// <summary>A Subtask or StoryBug nested under a Story. The JSON has no "type" field here —
    /// which one it is comes from whether it was read out of "subtasks" or "storyBugs".</summary>
    [ExcludeFromCodeCoverage]
    public class MockSubItem
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Assignee { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public List<MockComment> Comments { get; set; } = new();
    }

    [ExcludeFromCodeCoverage]
    public class MockComment
    {
        public string Author { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    [ExcludeFromCodeCoverage]
    public class MockTeamRoster
    {
        public List<MockTeamMember> Team { get; set; } = new();
    }

    [ExcludeFromCodeCoverage]
    public class MockTeamMember
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }
}
