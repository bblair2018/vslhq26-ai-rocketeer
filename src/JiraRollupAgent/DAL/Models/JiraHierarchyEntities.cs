namespace JiraRollupAgent.DAL.Models
{
    /// <summary>
    /// A top-level Initiative, ordered in reports by <see cref="PriorityRank"/>.
    /// Mirrors the "initiatives[]" shape in MockData/jira-hierarchy.json.
    /// </summary>
    public class Initiative
    {
        /// <summary>The database primary key.</summary>
        public int Id { get; set; }

        /// <summary>The Jira key from the source data, e.g. "INIT-PFD".</summary>
        public string JiraId { get; set; } = string.Empty;

        /// <summary>The Initiative's display title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Business priority rank used for final report ordering; 1 = highest priority.</summary>
        public int PriorityRank { get; set; }

        /// <summary>The Jira workflow status, e.g. "In Progress".</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>The Epics belonging to this Initiative.</summary>
        public virtual ICollection<Epic> Epics { get; set; } = new List<Epic>();

        /// <summary>Comments attached directly to this Initiative (not to one of its Epics/WorkItems).</summary>
        public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();

        /// <summary>The generated Business Summary for this Initiative, if one has been produced yet.</summary>
        public virtual InitiativeBusinessSummary? BusinessSummary { get; set; }
    }

    /// <summary>
    /// An Epic beneath an Initiative, containing Story/Bug/Task/Spike work items.
    /// </summary>
    public class Epic
    {
        /// <summary>The database primary key.</summary>
        public int Id { get; set; }

        /// <summary>The Jira key from the source data, e.g. "EPIC-PFD-1".</summary>
        public string JiraId { get; set; } = string.Empty;

        /// <summary>The Epic's display title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Foreign key to the owning <see cref="Initiative"/>.</summary>
        public int InitiativeId { get; set; }

        /// <summary>The owning Initiative.</summary>
        public virtual Initiative Initiative { get; set; } = null!;

        /// <summary>The WorkItems (Story/Bug/Task/Spike) that are direct children of this Epic.</summary>
        public virtual ICollection<WorkItem> WorkItems { get; set; } = new List<WorkItem>();

        /// <summary>Comments attached directly to this Epic (not to one of its WorkItems).</summary>
        public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();

        /// <summary>The generated Engineering Summary for this Epic, if one has been produced yet.</summary>
        public virtual EpicEngineeringSummary? EngineeringSummary { get; set; }
    }

    /// <summary>
    /// A single Jira work item. <see cref="Type"/> is the Jira issue type discriminator:
    /// "Story", "Bug", "Task", "Spike" (direct children of an Epic, via <see cref="EpicId"/>),
    /// or "Subtask"/"StoryBug" (children of a Story, via <see cref="ParentWorkItemId"/>).
    /// </summary>
    public class WorkItem
    {
        /// <summary>The database primary key.</summary>
        public int Id { get; set; }

        /// <summary>The Jira key from the source data, e.g. "STORY-PFD-1-1".</summary>
        public string JiraId { get; set; } = string.Empty;

        /// <summary>The Jira issue type discriminator: "Story", "Bug", "Task", "Spike", "Subtask", or "StoryBug".</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>The item's display title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>The person assigned to this item.</summary>
        public string Assignee { get; set; } = string.Empty;

        /// <summary>The Jira workflow status, e.g. "Resolved".</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Set when this item is a direct child of an Epic (Story/Bug/Task/Spike).</summary>
        public int? EpicId { get; set; }

        /// <summary>The owning Epic, if this item is a direct Epic child.</summary>
        public virtual Epic? Epic { get; set; }

        /// <summary>Set when this item is a Subtask/StoryBug nested under a Story.</summary>
        public int? ParentWorkItemId { get; set; }

        /// <summary>The owning Story, if this item is a Subtask/StoryBug.</summary>
        public virtual WorkItem? ParentWorkItem { get; set; }

        /// <summary>This Story's Subtasks/StoryBugs, if this item is a Story; otherwise empty.</summary>
        public virtual ICollection<WorkItem> Children { get; set; } = new List<WorkItem>();

        /// <summary>Comments attached directly to this item.</summary>
        public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();

        /// <summary>The generated summary for this item, if one has been produced yet.</summary>
        public virtual WorkItemSummary? Summary { get; set; }
    }

    /// <summary>
    /// A comment attached to exactly one of Initiative, Epic, or WorkItem.
    /// <see cref="Role"/> drives the Epic/Initiative role-weighted summarization.
    /// </summary>
    public class Comment
    {
        /// <summary>The database primary key.</summary>
        public int Id { get; set; }

        /// <summary>The commenter's display name.</summary>
        public string Author { get; set; } = string.Empty;

        /// <summary>Dev, QA, ScrumMaster, Stakeholder, or EngineeringManager.</summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>When the comment was posted.</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>The comment body.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Set when this comment is attached directly to an Initiative.</summary>
        public int? InitiativeId { get; set; }

        /// <summary>The owning Initiative, if this comment is attached directly to one.</summary>
        public virtual Initiative? Initiative { get; set; }

        /// <summary>Set when this comment is attached directly to an Epic.</summary>
        public int? EpicId { get; set; }

        /// <summary>The owning Epic, if this comment is attached directly to one.</summary>
        public virtual Epic? Epic { get; set; }

        /// <summary>Set when this comment is attached to a WorkItem.</summary>
        public int? WorkItemId { get; set; }

        /// <summary>The owning WorkItem, if this comment is attached to one.</summary>
        public virtual WorkItem? WorkItem { get; set; }
    }

    /// <summary>
    /// Mirrors MockData/team-roster.json. <see cref="Role"/> is the same enum used on <see cref="Comment.Role"/>.
    /// </summary>
    public class TeamMember
    {
        /// <summary>The database primary key.</summary>
        public int Id { get; set; }

        /// <summary>The source id from team-roster.json, e.g. "USR-01".</summary>
        public string ExternalId { get; set; } = string.Empty;

        /// <summary>The team member's display name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Dev, QA, ScrumMaster, Stakeholder, or EngineeringManager - same enum as <see cref="Comment.Role"/>.</summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>A human-readable job title, e.g. "Head of Underwriting" - not used for filtering.</summary>
        public string JobTitle { get; set; } = string.Empty;

        /// <summary>The team member's email address.</summary>
        public string Email { get; set; } = string.Empty;
    }

    /// <summary>
    /// The generated summary for a single WorkItem (Story/Bug/Task/Spike/Subtask/StoryBug) - Type A
    /// (leaf) or Type B (Story, rolling up its Subtask/StoryBug summaries), never role-weighted.
    /// Exactly one row per WorkItem: overwritten fresh on every SummarizationService run, not a
    /// history table. Never printed directly in the report - consumed as input by the Story (if this
    /// is a Subtask/StoryBug) or Epic (if this is a Story/Bug/Task/Spike) summary above it.
    /// </summary>
    public class WorkItemSummary
    {
        /// <summary>The database primary key.</summary>
        public int Id { get; set; }

        /// <summary>Foreign key to the summarized WorkItem; unique, enforcing one summary per WorkItem.</summary>
        public int WorkItemId { get; set; }

        /// <summary>The summarized WorkItem.</summary>
        public virtual WorkItem WorkItem { get; set; } = null!;

        /// <summary>The generated summary text.</summary>
        public string SummaryText { get; set; } = string.Empty;

        /// <summary>The start of the date range whose comments produced this summary - provenance, not history.</summary>
        public DateTime RangeStart { get; set; }

        /// <summary>The end of the date range whose comments produced this summary - provenance, not history.</summary>
        public DateTime RangeEnd { get; set; }

        /// <summary>When this summary was generated.</summary>
        public DateTime GeneratedAt { get; set; }
    }

    /// <summary>
    /// The Dev/QA-weighted Engineering Summary for a single Epic (Type C, parameterized for
    /// engineering audiences) - one of the two summary levels actually printed in the report.
    /// Exactly one row per Epic: overwritten fresh on every SummarizationService run.
    /// </summary>
    public class EpicEngineeringSummary
    {
        /// <summary>The database primary key.</summary>
        public int Id { get; set; }

        /// <summary>Foreign key to the summarized Epic; unique, enforcing one summary per Epic.</summary>
        public int EpicId { get; set; }

        /// <summary>The summarized Epic.</summary>
        public virtual Epic Epic { get; set; } = null!;

        /// <summary>The generated summary text.</summary>
        public string SummaryText { get; set; } = string.Empty;

        /// <summary>The start of the date range whose comments produced this summary - provenance, not history.</summary>
        public DateTime RangeStart { get; set; }

        /// <summary>The end of the date range whose comments produced this summary - provenance, not history.</summary>
        public DateTime RangeEnd { get; set; }

        /// <summary>When this summary was generated.</summary>
        public DateTime GeneratedAt { get; set; }
    }

    /// <summary>
    /// The ScrumMaster/Stakeholder/EngineeringManager-weighted Business Summary for a single
    /// Initiative (Type C, parameterized for business audiences) - the other summary level actually
    /// printed in the report. Exactly one row per Initiative: overwritten fresh on every
    /// SummarizationService run.
    /// </summary>
    public class InitiativeBusinessSummary
    {
        /// <summary>The database primary key.</summary>
        public int Id { get; set; }

        /// <summary>Foreign key to the summarized Initiative; unique, enforcing one summary per Initiative.</summary>
        public int InitiativeId { get; set; }

        /// <summary>The summarized Initiative.</summary>
        public virtual Initiative Initiative { get; set; } = null!;

        /// <summary>The generated summary text.</summary>
        public string SummaryText { get; set; } = string.Empty;

        /// <summary>The start of the date range whose comments produced this summary - provenance, not history.</summary>
        public DateTime RangeStart { get; set; }

        /// <summary>The end of the date range whose comments produced this summary - provenance, not history.</summary>
        public DateTime RangeEnd { get; set; }

        /// <summary>When this summary was generated.</summary>
        public DateTime GeneratedAt { get; set; }
    }
}
