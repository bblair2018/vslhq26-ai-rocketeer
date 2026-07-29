namespace JiraRollupAgent.Services.JiraHierarchyLoaderService
{
    /// <summary>
    /// Loads the mocked Jira hierarchy (Initiatives/Epics/Stories/Bugs/Tasks/Spikes/Subtasks/StoryBugs)
    /// that stands in for the real Jira ingestion pipeline. See MockData/jira-hierarchy.json.
    /// </summary>
    public interface IJiraHierarchyLoaderService
    {
        /// <summary>Runs the load stage, gated by <c>AppSettings:RunHierarchyLoad</c>.</summary>
        /// <returns><c>true</c> on success or a skipped run; <c>false</c> if the load failed.</returns>
        Task<bool> Run();
    }
}
