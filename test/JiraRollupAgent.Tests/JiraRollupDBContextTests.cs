using FluentAssertions;
using JiraRollupAgent.DAL.Context;
using JiraRollupAgent.DAL.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace JiraRollupAgent.Tests
{
    /// <summary>
    /// Unit tests for <see cref="JiraRollupDBContext.OnModelCreating"/>: the collation annotation,
    /// every foreign key's configured <see cref="DeleteBehavior"/> (see CLAUDE.md's FK relationship
    /// table for the documented reasoning behind each), and one live save+requery round trip proving
    /// the resulting schema actually works end to end. Backed by a real SQLite in-memory database -
    /// not EF Core's InMemory provider, which can't translate <c>UnitOfWork.DeleteAllRowsAsync</c>'s
    /// raw SQL and doesn't meaningfully enforce FK/cascade configuration.
    /// </summary>
    public class JiraRollupDBContextTests
    {
        /// <summary>Creates a fresh SQLite in-memory-backed context with the schema already created. The caller must dispose both the context and the connection.</summary>
        private static (SqliteConnection Connection, JiraRollupDBContext Context) CreateContext()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<JiraRollupDBContext>().UseSqlite(connection).Options;
            var context = new JiraRollupDBContext(options);
            context.Database.EnsureCreated();
            return (connection, context);
        }

        /// <summary>OnModelCreating sets the SQL Server collation annotation on the model.</summary>
        [Fact]
        public void OnModelCreating_SetsCollationAnnotation()
        {
            // Arrange
            var (connection, context) = CreateContext();
            try
            {
                // Act
                var designTimeModel = context.GetService<IDesignTimeModel>().Model;
                var collation = designTimeModel.GetCollation();

                // Assert
                collation.Should().Be("SQL_Latin1_General_CP1_CI_AS");
            }
            finally
            {
                context.Dispose();
                connection.Dispose();
            }
        }

        /// <summary>
        /// Every configured foreign key has the documented <see cref="DeleteBehavior"/>: Epic-&gt;Initiative
        /// cascades (the one non-nullable parent FK); WorkItem-&gt;Epic is the EF convention default for an
        /// optional FK; WorkItem-&gt;WorkItem (self-referencing) and all three Comment FKs are explicitly
        /// Restrict (self-reference / multiple-cascade-paths); the three summary tables cascade with their
        /// one-to-one owner.
        /// </summary>
        [Theory]
        [InlineData(typeof(Epic), "InitiativeId", DeleteBehavior.Cascade)]
        [InlineData(typeof(WorkItem), "EpicId", DeleteBehavior.ClientSetNull)]
        [InlineData(typeof(WorkItem), "ParentWorkItemId", DeleteBehavior.Restrict)]
        [InlineData(typeof(Comment), "InitiativeId", DeleteBehavior.Restrict)]
        [InlineData(typeof(Comment), "EpicId", DeleteBehavior.Restrict)]
        [InlineData(typeof(Comment), "WorkItemId", DeleteBehavior.Restrict)]
        [InlineData(typeof(WorkItemSummary), "WorkItemId", DeleteBehavior.Cascade)]
        [InlineData(typeof(EpicEngineeringSummary), "EpicId", DeleteBehavior.Cascade)]
        [InlineData(typeof(InitiativeBusinessSummary), "InitiativeId", DeleteBehavior.Cascade)]
        public void OnModelCreating_ConfiguresExpectedDeleteBehavior(Type dependentType, string foreignKeyPropertyName, DeleteBehavior expected)
        {
            // Arrange
            var (connection, context) = CreateContext();
            try
            {
                // Act
                var foreignKey = context.Model.FindEntityType(dependentType)!.GetForeignKeys()
                    .Single(fk => fk.Properties.Single().Name == foreignKeyPropertyName);

                // Assert
                foreignKey.DeleteBehavior.Should().Be(expected);
            }
            finally
            {
                context.Dispose();
                connection.Dispose();
            }
        }

        /// <summary>The configured schema accepts and correctly persists/rehydrates a full Initiative -&gt; Epic -&gt; WorkItem -&gt; Comment graph.</summary>
        [Fact]
        public async Task EnsureCreated_AcceptsFullEntityGraphRoundTrip()
        {
            // Arrange
            var (connection, context) = CreateContext();
            try
            {
                var initiative = new Initiative { JiraId = "INIT-1", Title = "Cockpit Avionics", PriorityRank = 1, Status = "In Progress" };
                var epic = new Epic { JiraId = "EPIC-1", Title = "Glass Cockpit", Initiative = initiative };
                var workItem = new WorkItem { JiraId = "BUG-1", Type = "Bug", Title = "B", Assignee = "Dev", Status = "Open", Epic = epic };
                var comment = new Comment { Author = "Dev One", Role = "Dev", Timestamp = new DateTime(2026, 7, 10), Text = "hi", WorkItem = workItem };

                context.Initiatives.Add(initiative);
                context.Epics.Add(epic);
                context.WorkItems.Add(workItem);
                context.Comments.Add(comment);

                // Act
                await context.SaveChangesAsync();

                using var freshContext = new JiraRollupDBContext(new DbContextOptionsBuilder<JiraRollupDBContext>().UseSqlite(connection).Options);
                var reloadedEpic = await freshContext.Epics.SingleAsync();
                var reloadedWorkItem = await freshContext.WorkItems.SingleAsync();
                var reloadedComment = await freshContext.Comments.SingleAsync();

                // Assert
                reloadedEpic.InitiativeId.Should().Be(initiative.Id);
                reloadedWorkItem.EpicId.Should().Be(epic.Id);
                reloadedComment.WorkItemId.Should().Be(workItem.Id);
            }
            finally
            {
                context.Dispose();
                connection.Dispose();
            }
        }
    }
}
