using FluentAssertions;
using JiraRollupAgent.DAL.Models;
using JiraRollupAgent.DAL.Repositories.Interfaces;
using JiraRollupAgent.Models.JiraHierarchyLoaderService;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;
using JiraHierarchyLoaderServiceType = JiraRollupAgent.Services.JiraHierarchyLoaderService.JiraHierarchyLoaderService;

namespace JiraRollupAgent.Tests
{
    /// <summary>
    /// Unit tests for <see cref="JiraHierarchyLoaderServiceType"/>: the mock-DTO-to-entity mapping
    /// helpers, and the <c>Run()</c> skip/success paths. The success path reads this test project's
    /// own small fixture <c>MockData/jira-hierarchy.json</c>/<c>team-roster.json</c> (copied to the
    /// test binary's output directory), not the real 5100-line hierarchy under
    /// <c>src/JiraRollupAgent/MockData/</c>.
    /// </summary>
    public class JiraHierarchyLoaderServiceTests
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

        /// <summary>Builds a <see cref="MockComment"/> fixture with the given role/text.</summary>
        private static MockComment MakeMockComment(string role, string text, string author = "Test Author")
            => new() { Author = author, Role = role, Timestamp = new DateTime(2026, 7, 10, 9, 0, 0), Text = text };

        #region MapComment

        /// <summary>MapComment maps all four comment fields straight through and leaves every parent foreign key unset.</summary>
        [Fact]
        public void MapComment_MapsAllFieldsWithNoParentForeignKeySet()
        {
            // Arrange
            var mockComment = new MockComment { Author = "Dev One", Role = "Dev", Timestamp = new DateTime(2026, 7, 10, 9, 0, 0), Text = "Some text" };

            // Act
            var result = JiraHierarchyLoaderServiceType.MapComment(mockComment);

            // Assert
            result.Author.Should().Be("Dev One");
            result.Role.Should().Be("Dev");
            result.Timestamp.Should().Be(new DateTime(2026, 7, 10, 9, 0, 0));
            result.Text.Should().Be("Some text");
            result.InitiativeId.Should().BeNull();
            result.EpicId.Should().BeNull();
            result.WorkItemId.Should().BeNull();
        }

        #endregion

        #region MapChildWorkItem

        /// <summary>MapChildWorkItem assigns whichever type discriminator ("Subtask" or "StoryBug") the caller passes in, since the source JSON has no type field of its own for these.</summary>
        [Theory]
        [InlineData("Subtask")]
        [InlineData("StoryBug")]
        public void MapChildWorkItem_AssignsGivenTypeDiscriminator(string type)
        {
            // Arrange
            var mockSubItem = new MockSubItem
            {
                Id = "SUB-1",
                Title = "T",
                Assignee = "Dev",
                Status = "Open",
                Comments = [MakeMockComment("Dev", "hi")]
            };

            // Act
            var result = JiraHierarchyLoaderServiceType.MapChildWorkItem(mockSubItem, type);

            // Assert
            result.Type.Should().Be(type);
            result.JiraId.Should().Be("SUB-1");
            result.Comments.Should().HaveCount(1);
        }

        #endregion

        #region MapWorkItem

        /// <summary>MapWorkItem puts both Subtasks and StoryBugs into Children with the correct type discriminator, and maps its own comments.</summary>
        [Fact]
        public void MapWorkItem_MapsSubtasksAndStoryBugsIntoChildrenWithCorrectTypes()
        {
            // Arrange
            var mockItem = new MockWorkItem
            {
                Type = "Story",
                Id = "STORY-1",
                Title = "S",
                Assignee = "Dev",
                Status = "In Progress",
                Comments = [MakeMockComment("Dev", "story comment")],
                Subtasks = [new MockSubItem { Id = "SUB-1", Title = "Sub", Assignee = "Dev", Status = "Resolved" }],
                StoryBugs = [new MockSubItem { Id = "SBUG-1", Title = "Bug", Assignee = "QA", Status = "Open" }]
            };

            // Act
            var result = JiraHierarchyLoaderServiceType.MapWorkItem(mockItem);

            // Assert
            result.Type.Should().Be("Story");
            result.Comments.Should().HaveCount(1);
            result.Children.Should().HaveCount(2);
            result.Children.Should().Contain(c => c.JiraId == "SUB-1" && c.Type == "Subtask");
            result.Children.Should().Contain(c => c.JiraId == "SBUG-1" && c.Type == "StoryBug");
        }

        #endregion

        #region MapEpic

        /// <summary>MapEpic maps its own comments and nests its mapped WorkItems.</summary>
        [Fact]
        public void MapEpic_MapsOwnCommentsAndNestedWorkItems()
        {
            // Arrange
            var mockEpic = new MockEpic
            {
                Id = "EPIC-1",
                Title = "E",
                Comments = [MakeMockComment("EngineeringManager", "epic comment")],
                Items = [new MockWorkItem { Type = "Bug", Id = "BUG-1", Title = "B", Assignee = "Dev", Status = "Open" }]
            };

            // Act
            var result = JiraHierarchyLoaderServiceType.MapEpic(mockEpic);

            // Assert
            result.JiraId.Should().Be("EPIC-1");
            result.Comments.Should().HaveCount(1);
            result.WorkItems.Should().HaveCount(1);
            result.WorkItems.Single().JiraId.Should().Be("BUG-1");
        }

        #endregion

        #region Run

        /// <summary>Run() returns true and never touches the unit of work when AppSettings:RunHierarchyLoad is false.</summary>
        [Fact]
        public async Task Run_WhenFlagIsFalse_ReturnsTrueWithoutTouchingUnitOfWork()
        {
            // Arrange
            var config = BuildConfig(("AppSettings:RunHierarchyLoad", "false"));
            var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);
            var service = new JiraHierarchyLoaderServiceType(config, unitOfWork.Object);

            // Act
            var result = await service.Run();

            // Assert
            result.Should().BeTrue();
        }

        /// <summary>Run() loads this test project's fixture MockData, wipes existing rows first, and adds the mapped team roster and full Initiative/Epic/WorkItem/Children graph.</summary>
        [Fact]
        public async Task Run_WithFlagTrueAndFixtureMockData_LoadsHierarchyAndTeamMembers()
        {
            // Arrange
            var config = BuildConfig(("AppSettings:RunHierarchyLoad", "true"));
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.DeleteAllRowsAsync()).Returns(Task.CompletedTask);
            unitOfWork.Setup(u => u.CompleteAsync()).ReturnsAsync(1);

            List<TeamMember>? addedTeamMembers = null;
            unitOfWork.Setup(u => u.TeamMembers.AddRangeAsync(It.IsAny<IEnumerable<TeamMember>>()))
                .Callback<IEnumerable<TeamMember>>(t => addedTeamMembers = t.ToList())
                .Returns(Task.CompletedTask);

            Initiative? addedInitiative = null;
            unitOfWork.Setup(u => u.Initiatives.AddAsync(It.IsAny<Initiative>()))
                .Callback<Initiative>(i => addedInitiative = i)
                .Returns(Task.CompletedTask);

            var service = new JiraHierarchyLoaderServiceType(config, unitOfWork.Object);

            // Act
            var result = await service.Run();

            // Assert
            result.Should().BeTrue();
            addedTeamMembers.Should().HaveCount(2);
            addedInitiative.Should().NotBeNull();
            addedInitiative!.JiraId.Should().Be("INIT-TEST-1");
            addedInitiative.Epics.Should().HaveCount(1);

            var epic = addedInitiative.Epics.Single();
            epic.WorkItems.Should().HaveCount(2);

            var story = epic.WorkItems.Single(w => w.Type == "Story");
            story.Children.Should().HaveCount(2);
            story.Children.Should().Contain(c => c.Type == "Subtask");
            story.Children.Should().Contain(c => c.Type == "StoryBug");

            unitOfWork.Verify(u => u.DeleteAllRowsAsync(), Times.Once());
            unitOfWork.Verify(u => u.CompleteAsync(), Times.Once());
        }

        /// <summary>Run() catches an exception thrown by a dependency, logs it, and returns false rather than propagating it.</summary>
        [Fact]
        public async Task Run_WhenUnitOfWorkThrows_ReturnsFalse()
        {
            // Arrange
            var config = BuildConfig(("AppSettings:RunHierarchyLoad", "true"));
            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.DeleteAllRowsAsync()).ThrowsAsync(new InvalidOperationException("boom"));
            var service = new JiraHierarchyLoaderServiceType(config, unitOfWork.Object);

            // Act
            var result = await service.Run();

            // Assert
            result.Should().BeFalse();
        }

        #endregion

        #region DisableHierarchyLoadFlagAsync

        /// <summary>DisableHierarchyLoadFlagAsync leaves the file untouched and just logs a warning when the JSON has no "AppSettings" section.</summary>
        [Fact]
        public async Task DisableHierarchyLoadFlagAsync_WithNoAppSettingsSection_LeavesFileUnchanged()
        {
            // Arrange
            var path = Path.GetTempFileName();
            try
            {
                const string original = "{\"SomethingElse\":{}}";
                await File.WriteAllTextAsync(path, original);
                var service = new JiraHierarchyLoaderServiceType(BuildConfig(), Mock.Of<IUnitOfWork>());

                // Act
                await service.DisableHierarchyLoadFlagAsync(path);

                // Assert
                (await File.ReadAllTextAsync(path)).Should().Be(original);
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>DisableHierarchyLoadFlagAsync swallows a read/parse failure (e.g. malformed JSON) rather than propagating it.</summary>
        [Fact]
        public async Task DisableHierarchyLoadFlagAsync_WithMalformedJson_DoesNotThrow()
        {
            // Arrange
            var path = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(path, "{ not valid json");
                var service = new JiraHierarchyLoaderServiceType(BuildConfig(), Mock.Of<IUnitOfWork>());

                // Act
                Func<Task> act = () => service.DisableHierarchyLoadFlagAsync(path);

                // Assert
                await act.Should().NotThrowAsync();
            }
            finally
            {
                File.Delete(path);
            }
        }

        #endregion
    }
}
