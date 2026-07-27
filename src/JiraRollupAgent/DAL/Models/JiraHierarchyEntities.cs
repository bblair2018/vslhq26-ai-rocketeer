using System.Diagnostics.CodeAnalysis;

namespace JiraRollupAgent.DAL.Models
{
    /// <summary>
    /// A top-level Initiative, ordered in reports by <see cref="PriorityRank"/>.
    /// Mirrors the "initiatives[]" shape in MockData/jira-hierarchy.json.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class Initiative
    {
        public int Id { get; set; }

        /// <summary>The Jira key from the source data, e.g. "INIT-PFD".</summary>
        public string JiraId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public int PriorityRank { get; set; }

        public string Status { get; set; } = string.Empty;

        public virtual ICollection<Epic> Epics { get; set; } = new List<Epic>();

        public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();
    }

    /// <summary>
    /// An Epic beneath an Initiative, containing Story/Bug/Task/Spike work items.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class Epic
    {
        public int Id { get; set; }

        public string JiraId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public int InitiativeId { get; set; }

        public virtual Initiative Initiative { get; set; } = null!;

        public virtual ICollection<WorkItem> WorkItems { get; set; } = new List<WorkItem>();

        public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();
    }

    /// <summary>
    /// A single Jira work item. <see cref="Type"/> is the Jira issue type discriminator:
    /// "Story", "Bug", "Task", "Spike" (direct children of an Epic, via <see cref="EpicId"/>),
    /// or "Subtask"/"StoryBug" (children of a Story, via <see cref="ParentWorkItemId"/>).
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class WorkItem
    {
        public int Id { get; set; }

        public string JiraId { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Assignee { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        /// <summary>Set when this item is a direct child of an Epic (Story/Bug/Task/Spike).</summary>
        public int? EpicId { get; set; }

        public virtual Epic? Epic { get; set; }

        /// <summary>Set when this item is a Subtask/StoryBug nested under a Story.</summary>
        public int? ParentWorkItemId { get; set; }

        public virtual WorkItem? ParentWorkItem { get; set; }

        public virtual ICollection<WorkItem> Children { get; set; } = new List<WorkItem>();

        public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();
    }

    /// <summary>
    /// A comment attached to exactly one of Initiative, Epic, or WorkItem.
    /// <see cref="Role"/> drives the Epic/Initiative role-weighted summarization.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class Comment
    {
        public int Id { get; set; }

        public string Author { get; set; } = string.Empty;

        /// <summary>Dev, QA, ScrumMaster, Stakeholder, or EngineeringManager.</summary>
        public string Role { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; }

        public string Text { get; set; } = string.Empty;

        public int? InitiativeId { get; set; }

        public virtual Initiative? Initiative { get; set; }

        public int? EpicId { get; set; }

        public virtual Epic? Epic { get; set; }

        public int? WorkItemId { get; set; }

        public virtual WorkItem? WorkItem { get; set; }
    }

    /// <summary>
    /// Mirrors MockData/team-roster.json. <see cref="Role"/> is the same enum used on <see cref="Comment.Role"/>.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class TeamMember
    {
        public int Id { get; set; }

        /// <summary>The source id from team-roster.json, e.g. "USR-01".</summary>
        public string ExternalId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public string JobTitle { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;
    }
}
