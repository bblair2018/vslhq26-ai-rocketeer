namespace JiraRollupAgent.Services.JiraHierarchyLoaderService
{
    /// <summary>
    /// Loads the mocked Jira hierarchy (Initiatives/Epics/Stories/Bugs/Tasks/Spikes/Subtasks/StoryBugs)
    /// that stands in for the real Jira ingestion pipeline. See MockData/jira-hierarchy.json.
    /// </summary>
    public interface IJiraHierarchyLoaderService
    {
        Task<bool> Run();
    }
}
