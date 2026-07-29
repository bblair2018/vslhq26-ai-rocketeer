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
    /// Unit tests for the generic <see cref="Repository{T}"/>, exercised against
    /// <see cref="TeamMember"/> (a simple entity with no foreign key dependencies), backed by a real
    /// SQLite in-memory database - these methods are thin wrappers around a real EF Core
    /// <see cref="DbSet{TEntity}"/> that mocking <c>IRepository&lt;T&gt;</c> elsewhere would trivially bypass.
    /// </summary>
    public class RepositoryTests
    {
        /// <summary>Creates a fresh SQLite in-memory-backed context with the schema already created, plus a Repository&lt;TeamMember&gt; bound to it. The caller must dispose both the context and the connection.</summary>
        private static (SqliteConnection Connection, JiraRollupDBContext Context, Repository<TeamMember> Repository) CreateRepository()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<JiraRollupDBContext>().UseSqlite(connection).Options;
            var context = new JiraRollupDBContext(options);
            context.Database.EnsureCreated();
            return (connection, context, new Repository<TeamMember>(context));
        }

        /// <summary>Builds a minimal <see cref="TeamMember"/> fixture with the given external id/name.</summary>
        private static TeamMember MakeTeamMember(string externalId, string name)
            => new() { ExternalId = externalId, Name = name, Role = "Dev", JobTitle = "Software Engineer", Email = $"{externalId}@example.com" };

        /// <summary>AddAsync, followed by SaveChanges, persists the entity so it's returned by GetAllAsync.</summary>
        [Fact]
        public async Task AddAsync_ThenSaveChanges_PersistsTheEntity()
        {
            // Arrange
            var (connection, context, repository) = CreateRepository();
            try
            {
                var member = MakeTeamMember("USR-1", "Dev One");

                // Act
                await repository.AddAsync(member);
                await context.SaveChangesAsync();

                // Assert
                var all = await repository.GetAllAsync();
                all.Should().ContainSingle(m => m.ExternalId == "USR-1");
            }
            finally
            {
                context.Dispose();
                connection.Dispose();
            }
        }

        /// <summary>GetByIdAsync returns the entity matching an existing primary key.</summary>
        [Fact]
        public async Task GetByIdAsync_WithExistingId_ReturnsEntity()
        {
            // Arrange
            var (connection, context, repository) = CreateRepository();
            try
            {
                var member = MakeTeamMember("USR-1", "Dev One");
                await repository.AddAsync(member);
                await context.SaveChangesAsync();

                // Act
                var result = await repository.GetByIdAsync(member.Id);

                // Assert
                result.Should().NotBeNull();
                result!.ExternalId.Should().Be("USR-1");
            }
            finally
            {
                context.Dispose();
                connection.Dispose();
            }
        }

        /// <summary>GetByIdAsync returns null rather than throwing when no row matches the given primary key.</summary>
        [Fact]
        public async Task GetByIdAsync_WithMissingId_ReturnsNull()
        {
            // Arrange
            var (connection, context, repository) = CreateRepository();
            try
            {
                // Act
                var result = await repository.GetByIdAsync(9999);

                // Assert
                result.Should().BeNull();
            }
            finally
            {
                context.Dispose();
                connection.Dispose();
            }
        }

        /// <summary>FindAsync returns only the rows matching the given predicate.</summary>
        [Fact]
        public async Task FindAsync_FiltersByPredicate()
        {
            // Arrange
            var (connection, context, repository) = CreateRepository();
            try
            {
                await repository.AddRangeAsync([MakeTeamMember("USR-1", "Dev One"), MakeTeamMember("USR-2", "QA One")]);
                await context.SaveChangesAsync();

                // Act
                var result = await repository.FindAsync(m => m.ExternalId == "USR-2");

                // Assert
                result.Should().ContainSingle().Which.Name.Should().Be("QA One");
            }
            finally
            {
                context.Dispose();
                connection.Dispose();
            }
        }

        /// <summary>AddRangeAsync, followed by SaveChanges, persists every entity in the batch.</summary>
        [Fact]
        public async Task AddRangeAsync_ThenSaveChanges_PersistsAllEntities()
        {
            // Arrange
            var (connection, context, repository) = CreateRepository();
            try
            {
                // Act
                await repository.AddRangeAsync([MakeTeamMember("USR-1", "Dev One"), MakeTeamMember("USR-2", "QA One")]);
                await context.SaveChangesAsync();

                // Assert
                var all = await repository.GetAllAsync();
                all.Should().HaveCount(2);
            }
            finally
            {
                context.Dispose();
                connection.Dispose();
            }
        }

        /// <summary>Update, followed by SaveChanges, persists the modified field.</summary>
        [Fact]
        public async Task Update_ThenSaveChanges_PersistsTheChange()
        {
            // Arrange
            var (connection, context, repository) = CreateRepository();
            try
            {
                var member = MakeTeamMember("USR-1", "Dev One");
                await repository.AddAsync(member);
                await context.SaveChangesAsync();
                member.JobTitle = "Senior Software Engineer";

                // Act
                repository.Update(member);
                await context.SaveChangesAsync();

                // Assert
                var reloaded = await repository.GetByIdAsync(member.Id);
                reloaded!.JobTitle.Should().Be("Senior Software Engineer");
            }
            finally
            {
                context.Dispose();
                connection.Dispose();
            }
        }

        /// <summary>Remove, followed by SaveChanges, deletes the entity from the database.</summary>
        [Fact]
        public async Task Remove_ThenSaveChanges_DeletesTheEntity()
        {
            // Arrange
            var (connection, context, repository) = CreateRepository();
            try
            {
                var member = MakeTeamMember("USR-1", "Dev One");
                await repository.AddAsync(member);
                await context.SaveChangesAsync();

                // Act
                repository.Remove(member);
                await context.SaveChangesAsync();

                // Assert
                (await repository.GetByIdAsync(member.Id)).Should().BeNull();
            }
            finally
            {
                context.Dispose();
                connection.Dispose();
            }
        }

        /// <summary>RemoveRange, followed by SaveChanges, deletes every entity in the batch.</summary>
        [Fact]
        public async Task RemoveRange_ThenSaveChanges_DeletesAllGivenEntities()
        {
            // Arrange
            var (connection, context, repository) = CreateRepository();
            try
            {
                var members = new[] { MakeTeamMember("USR-1", "Dev One"), MakeTeamMember("USR-2", "QA One") };
                await repository.AddRangeAsync(members);
                await context.SaveChangesAsync();

                // Act
                repository.RemoveRange(members);
                await context.SaveChangesAsync();

                // Assert
                (await repository.GetAllAsync()).Should().BeEmpty();
            }
            finally
            {
                context.Dispose();
                connection.Dispose();
            }
        }
    }
}
