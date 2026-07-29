namespace JiraRollupAgent.Models.JiraHierarchyLoaderService
{
    /// <summary>
    /// Deserialization targets for MockData/jira-hierarchy.json and MockData/team-roster.json.
    /// Property names match the JSON fields case-insensitively (System.Text.Json PropertyNameCaseInsensitive).
    /// </summary>
    /// <summary>The root of jira-hierarchy.json: <c>{ "initiatives": [ ... ] }</c>.</summary>
    public class MockJiraHierarchy
    {
        /// <summary>Every Initiative in the mock hierarchy.</summary>
        public List<MockInitiative> Initiatives { get; set; } = new();
    }

    /// <summary>Mirrors one entry in jira-hierarchy.json's "initiatives[]".</summary>
    public class MockInitiative
    {
        /// <summary>The Jira key, e.g. "INIT-PFD".</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>The Initiative's display title.</summary>
        public string Title { get; set; } = string.Empty;
        /// <summary>Business priority rank; 1 = highest.</summary>
        public int PriorityRank { get; set; }
        /// <summary>The Jira workflow status.</summary>
        public string Status { get; set; } = string.Empty;
        /// <summary>Comments attached directly to this Initiative.</summary>
        public List<MockComment> Comments { get; set; } = new();
        /// <summary>The Epics beneath this Initiative.</summary>
        public List<MockEpic> Epics { get; set; } = new();
    }

    /// <summary>Mirrors one entry in an Initiative's "epics[]".</summary>
    public class MockEpic
    {
        /// <summary>The Jira key, e.g. "EPIC-PFD-1".</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>The Epic's display title.</summary>
        public string Title { get; set; } = string.Empty;
        /// <summary>Comments attached directly to this Epic.</summary>
        public List<MockComment> Comments { get; set; } = new();
        /// <summary>The Story/Bug/Task/Spike items directly under this Epic.</summary>
        public List<MockWorkItem> Items { get; set; } = new();
    }

    /// <summary>A direct child of an Epic: Story, Bug, Task, or Spike.</summary>
    public class MockWorkItem
    {
        /// <summary>The Jira issue type: "Story", "Bug", "Task", or "Spike".</summary>
        public string Type { get; set; } = string.Empty;
        /// <summary>The Jira key, e.g. "STORY-PFD-1-1".</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>The item's display title.</summary>
        public string Title { get; set; } = string.Empty;
        /// <summary>The person assigned to this item.</summary>
        public string Assignee { get; set; } = string.Empty;
        /// <summary>The Jira workflow status.</summary>
        public string Status { get; set; } = string.Empty;
        /// <summary>Comments attached directly to this item.</summary>
        public List<MockComment> Comments { get; set; } = new();

        /// <summary>Only ever populated when <see cref="Type"/> is "Story".</summary>
        public List<MockSubItem> Subtasks { get; set; } = new();

        /// <summary>Only ever populated when <see cref="Type"/> is "Story".</summary>
        public List<MockSubItem> StoryBugs { get; set; } = new();
    }

    /// <summary>A Subtask or StoryBug nested under a Story. The JSON has no "type" field here —
    /// which one it is comes from whether it was read out of "subtasks" or "storyBugs".</summary>
    public class MockSubItem
    {
        /// <summary>The Jira key, e.g. "SUB-PFD-1-1-1" or "SBUG-PFD-1-1-1".</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>The item's display title.</summary>
        public string Title { get; set; } = string.Empty;
        /// <summary>The person assigned to this item.</summary>
        public string Assignee { get; set; } = string.Empty;
        /// <summary>The Jira workflow status.</summary>
        public string Status { get; set; } = string.Empty;
        /// <summary>Comments attached directly to this item.</summary>
        public List<MockComment> Comments { get; set; } = new();
    }

    /// <summary>Mirrors one entry in any "comments[]" array.</summary>
    public class MockComment
    {
        /// <summary>The commenter's display name.</summary>
        public string Author { get; set; } = string.Empty;
        /// <summary>Dev, QA, ScrumMaster, Stakeholder, or EngineeringManager.</summary>
        public string Role { get; set; } = string.Empty;
        /// <summary>When the comment was posted.</summary>
        public DateTime Timestamp { get; set; }
        /// <summary>The comment body.</summary>
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>The root of team-roster.json: <c>{ "team": [ ... ] }</c>.</summary>
    public class MockTeamRoster
    {
        /// <summary>Every team member in the mock roster.</summary>
        public List<MockTeamMember> Team { get; set; } = new();
    }

    /// <summary>Mirrors one entry in team-roster.json's "team[]".</summary>
    public class MockTeamMember
    {
        /// <summary>The source id, e.g. "USR-01".</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>The team member's display name.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Dev, QA, ScrumMaster, Stakeholder, or EngineeringManager.</summary>
        public string Role { get; set; } = string.Empty;
        /// <summary>A human-readable job title, not used for filtering.</summary>
        public string JobTitle { get; set; } = string.Empty;
        /// <summary>The team member's email address.</summary>
        public string Email { get; set; } = string.Empty;
    }
}
