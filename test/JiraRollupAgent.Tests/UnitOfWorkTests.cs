using FluentAssertions;
using JiraRollupAgent.DAL.Context;
using JiraRollupAgent.DAL.Models;
using JiraRollupAgent.DAL.Repositories.Implementations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JiraRollupAgent.Tests
{
    /// <summary>
    /// Unit tests for <see cref="UnitOfWork"/>, backed by a real SQLite in-memory database: named and
    /// generic repository caching, <see cref="UnitOfWork.DeleteAllRowsAsync"/>/<see cref="UnitOfWork.DeleteAllSummariesAsync"/>'s
    /// raw SQL (which EF Core's InMemory provider can't translate), <see cref="UnitOfWork.SaveAsync"/>/<see cref="UnitOfWork.CompleteAsync"/>,
    /// and <see cref="UnitOfWork.Dispose"/>.
    /// </summary>
    public class UnitOfWorkTests
    {
        /// <summary>Creates a fresh SQLite in-memory-backed UnitOfWork with the schema already created. The caller must dispose both the unit of work and the connection.</summary>
        private static (SqliteConnection Connection, UnitOfWork UnitOfWork) CreateUnitOfWork()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<JiraRollupDBContext>().UseSqlite(connection).Options;
            var context = new JiraRollupDBContext(options);
            context.Database.EnsureCreated();
            return (connection, new UnitOfWork(context));
        }

        #region Repository caching

        /// <summary>Accessing a named repository property twice returns the same cached instance rather than constructing a new one each time.</summary>
        [Fact]
        public void Initiatives_ReturnsSameCachedInstanceOnRepeatedAccess()
        {
            // Arrange
            var (connection, unitOfWork) = CreateUnitOfWork();
            try
            {
                // Act
                var first = unitOfWork.Initiatives;
                var second = unitOfWork.Initiatives;

                // Assert
                second.Should().BeSameAs(first);
            }
            finally
            {
                unitOfWork.Dispose();
                connection.Dispose();
            }
        }

        /// <summary>The generic Repository&lt;T&gt;() accessor caches its result per entity type, same as the named properties.</summary>
        [Fact]
        public void Repository_GenericAccessor_CachesPerType()
        {
            // Arrange
            var (connection, unitOfWork) = CreateUnitOfWork();
            try
            {
                // Act
                var first = unitOfWork.Repository<TeamMember>();
                var second = unitOfWork.Repository<TeamMember>();

                // Assert
                second.Should().BeSameAs(first);
            }
            finally
            {
                unitOfWork.Dispose();
                connection.Dispose();
            }
        }

        #endregion

        #region DeleteAllRowsAsync / DeleteAllSummariesAsync

        /// <summary>DeleteAllRowsAsync empties all five hierarchy tables (Comments, WorkItems, TeamMembers, Epics, Initiatives) in one call.</summary>
        [Fact]
        public async Task DeleteAllRowsAsync_DeletesAllFiveHierarchyTables()
        {
            // Arrange
            var (connection, unitOfWork) = CreateUnitOfWork();
            try
            {
                var initiative = new Initiative { JiraId = "INIT-1", Title = "I", PriorityRank = 1, Status = "Open" };
                var epic = new Epic { JiraId = "EPIC-1", Title = "E", Initiative = initiative };
                var workItem = new WorkItem { JiraId = "BUG-1", Type = "Bug", Title = "B", Assignee = "Dev", Status = "Open", Epic = epic };
                var comment = new Comment { Author = "Dev", Role = "Dev", Timestamp = DateTime.UtcNow, Text = "hi", WorkItem = workItem };
                var teamMember = new TeamMember { ExternalId = "USR-1", Name = "Dev One", Role = "Dev", JobTitle = "Engineer", Email = "dev@example.com" };

                await unitOfWork.Initiatives.AddAsync(initiative);
                await unitOfWork.Epics.AddAsync(epic);
                await unitOfWork.WorkItems.AddAsync(workItem);
                await unitOfWork.Comments.AddAsync(comment);
                await unitOfWork.TeamMembers.AddAsync(teamMember);
                await unitOfWork.CompleteAsync();

                // Act
                await unitOfWork.DeleteAllRowsAsync();

                // Assert
                (await unitOfWork.Initiatives.GetAllAsync()).Should().BeEmpty();
                (await unitOfWork.Epics.GetAllAsync()).Should().BeEmpty();
                (await unitOfWork.WorkItems.GetAllAsync()).Should().BeEmpty();
                (await unitOfWork.Comments.GetAllAsync()).Should().BeEmpty();
                (await unitOfWork.TeamMembers.GetAllAsync()).Should().BeEmpty();
            }
            finally
            {
                unitOfWork.Dispose();
                connection.Dispose();
            }
        }

        /// <summary>DeleteAllSummariesAsync empties only the three summary tables, leaving the loaded hierarchy (Initiatives/Epics/WorkItems) untouched.</summary>
        [Fact]
        public async Task DeleteAllSummariesAsync_DeletesOnlyTheThreeSummaryTables()
        {
            // Arrange
            var (connection, unitOfWork) = CreateUnitOfWork();
            try
            {
                var initiative = new Initiative { JiraId = "INIT-1", Title = "I", PriorityRank = 1, Status = "Open" };
                var epic = new Epic { JiraId = "EPIC-1", Title = "E", Initiative = initiative };
                var workItem = new WorkItem { JiraId = "BUG-1", Type = "Bug", Title = "B", Assignee = "Dev", Status = "Open", Epic = epic };
                await unitOfWork.Initiatives.AddAsync(initiative);
                await unitOfWork.Epics.AddAsync(epic);
                await unitOfWork.WorkItems.AddAsync(workItem);
                await unitOfWork.CompleteAsync();

                var now = DateTime.UtcNow;
                await unitOfWork.WorkItemSummaries.AddAsync(new WorkItemSummary { WorkItemId = workItem.Id, SummaryText = "s", RangeStart = now, RangeEnd = now, GeneratedAt = now });
                await unitOfWork.EpicEngineeringSummaries.AddAsync(new EpicEngineeringSummary { EpicId = epic.Id, SummaryText = "s", RangeStart = now, RangeEnd = now, GeneratedAt = now });
                await unitOfWork.InitiativeBusinessSummaries.AddAsync(new InitiativeBusinessSummary { InitiativeId = initiative.Id, SummaryText = "s", RangeStart = now, RangeEnd = now, GeneratedAt = now });
                await unitOfWork.CompleteAsync();

                // Act
                await unitOfWork.DeleteAllSummariesAsync();

                // Assert
                (await unitOfWork.WorkItemSummaries.GetAllAsync()).Should().BeEmpty();
                (await unitOfWork.EpicEngineeringSummaries.GetAllAsync()).Should().BeEmpty();
                (await unitOfWork.InitiativeBusinessSummaries.GetAllAsync()).Should().BeEmpty();
                (await unitOfWork.Initiatives.GetAllAsync()).Should().ContainSingle();
                (await unitOfWork.Epics.GetAllAsync()).Should().ContainSingle();
                (await unitOfWork.WorkItems.GetAllAsync()).Should().ContainSingle();
            }
            finally
            {
                unitOfWork.Dispose();
                connection.Dispose();
            }
        }

        #endregion

        #region SaveAsync / CompleteAsync

        /// <summary>SaveAsync persists pending changes and returns the number of affected rows.</summary>
        [Fact]
        public async Task SaveAsync_PersistsChangesAndReturnsAffectedRowCount()
        {
            // Arrange
            var (connection, unitOfWork) = CreateUnitOfWork();
            try
            {
                await unitOfWork.TeamMembers.AddRangeAsync(
                [
                    new TeamMember { ExternalId = "USR-1", Name = "Dev One", Role = "Dev", JobTitle = "Engineer", Email = "dev1@example.com" },
                    new TeamMember { ExternalId = "USR-2", Name = "QA One", Role = "QA", JobTitle = "QA Engineer", Email = "qa1@example.com" }
                ]);

                // Act
                var affected = await unitOfWork.SaveAsync();

                // Assert
                affected.Should().Be(2);
                (await unitOfWork.TeamMembers.GetAllAsync()).Should().HaveCount(2);
            }
            finally
            {
                unitOfWork.Dispose();
                connection.Dispose();
            }
        }

        /// <summary>CompleteAsync (used by every service instead of calling SaveAsync directly) also persists pending changes.</summary>
        [Fact]
        public async Task CompleteAsync_PersistsPendingChanges()
        {
            // Arrange
            var (connection, unitOfWork) = CreateUnitOfWork();
            try
            {
                await unitOfWork.TeamMembers.AddAsync(new TeamMember { ExternalId = "USR-1", Name = "Dev One", Role = "Dev", JobTitle = "Engineer", Email = "dev1@example.com" });

                // Act
                var affected = await unitOfWork.CompleteAsync();

                // Assert
                affected.Should().Be(1);
                (await unitOfWork.TeamMembers.GetAllAsync()).Should().ContainSingle(m => m.ExternalId == "USR-1");
            }
            finally
            {
                unitOfWork.Dispose();
                connection.Dispose();
            }
        }

        #endregion

        #region Dispose

        /// <summary>Dispose disposes the underlying EF Core context, so any subsequent use of a repository obtained from it throws.</summary>
        [Fact]
        public async Task Dispose_DisposesTheUnderlyingContext()
        {
            // Arrange
            var (connection, unitOfWork) = CreateUnitOfWork();
            try
            {
                // Act
                unitOfWork.Dispose();

                // Assert
                Func<Task> act = () => unitOfWork.Initiatives.GetAllAsync();
                await act.Should().ThrowAsync<ObjectDisposedException>();
            }
            finally
            {
                connection.Dispose();
            }
        }

        #endregion
    }
}
