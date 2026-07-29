using System.Diagnostics.CodeAnalysis;
using JiraRollupAgent.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace JiraRollupAgent.DAL.Context
{
    /// <summary>
    /// Used to create our Jira Rollup Table Context.
    /// Migrations:
    /// 1 - dotnet tool install --global dotnet-ef
    /// 2 - dotnet ef migrations add InitialCreate
    /// 3 - dotnet ef database update
    /// </summary>
    public partial class JiraRollupDBContext : DbContext
    {
        /// <summary>Parameterless constructor used by EF Core tooling (e.g. migrations); falls back to <see cref="OnConfiguring"/> for its connection.</summary>
        [ExcludeFromCodeCoverage]
        public JiraRollupDBContext()
        {
        }

        /// <summary>Creates the context with explicit options, as registered via dependency injection in <c>Program.cs</c>.</summary>
        /// <param name="options">The DbContext options (connection string, provider, etc.) to use.</param>
        public JiraRollupDBContext(DbContextOptions<JiraRollupDBContext> options) : base(options)
        {
        }

        /// <summary>Top-level Initiatives, ordered in reports by <see cref="Initiative.PriorityRank"/>.</summary>
        public virtual DbSet<Initiative> Initiatives { get; set; } = null!;

        /// <summary>Epics, each belonging to exactly one Initiative.</summary>
        public virtual DbSet<Epic> Epics { get; set; } = null!;

        /// <summary>All work items (Story/Bug/Task/Spike/Subtask/StoryBug), discriminated by <see cref="WorkItem.Type"/>.</summary>
        public virtual DbSet<WorkItem> WorkItems { get; set; } = null!;

        /// <summary>Comments, each attached to exactly one of Initiative/Epic/WorkItem.</summary>
        public virtual DbSet<Comment> Comments { get; set; } = null!;

        /// <summary>The flat team roster, joined to <see cref="Comment"/>/<see cref="WorkItem"/> only by name/role string match in application logic.</summary>
        public virtual DbSet<TeamMember> TeamMembers { get; set; } = null!;

        /// <summary>Generated summaries, one per WorkItem, produced by <c>SummarizationService</c>.</summary>
        public virtual DbSet<WorkItemSummary> WorkItemSummaries { get; set; } = null!;

        /// <summary>Generated Dev/QA-weighted Engineering Summaries, one per Epic, produced by <c>SummarizationService</c>.</summary>
        public virtual DbSet<EpicEngineeringSummary> EpicEngineeringSummaries { get; set; } = null!;

        /// <summary>Generated ScrumMaster/Stakeholder/EngineeringManager-weighted Business Summaries, one per Initiative, produced by <c>SummarizationService</c>.</summary>
        public virtual DbSet<InitiativeBusinessSummary> InitiativeBusinessSummaries { get; set; } = null!;

        /// <summary>Falls back to a hardcoded local SQL Server connection when no options were supplied via DI (e.g. by EF Core tooling).</summary>
        /// <param name="optionsBuilder">The options builder to configure if not already configured.</param>
        [ExcludeFromCodeCoverage]
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                /*
                 * Configures the DbContext to use SQL Server as the database provider with the following settings:
                 * - Server: localhost (connects to the local SQL Server instance)
                 * - Database: VSLiveJiraRollup (specifies the target database)
                 * - User Id: sa (SQL Server authentication using the 'sa' user)
                 * - Password: Dallas1! (password for the 'sa' user)
                 * - TrustServerCertificate: True (bypasses SSL certificate validation, trusting the server's certificate even if it
                 *   is not issued by a trusted certificate authority; useful for local development or when using self-signed certificates)
                 */
                optionsBuilder.UseSqlServer("Server=localhost;Database=VSLiveJiraRollup;User Id=sa;Password=Dallas1!;TrustServerCertificate=True;");
            }
        }

        /// <summary>Configures collation and every relationship/delete-behavior in the entity graph - see CLAUDE.md's FK relationship table for the full reasoning.</summary>
        /// <param name="modelBuilder">The model builder to configure.</param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasAnnotation("Relational:Collation", "SQL_Latin1_General_CP1_CI_AS");

            // Initiative -> Epic
            modelBuilder.Entity<Epic>()
                .HasOne(e => e.Initiative)
                .WithMany(i => i.Epics)
                .HasForeignKey(e => e.InitiativeId);

            // Epic -> WorkItem (direct children: Story/Bug/Task/Spike)
            modelBuilder.Entity<WorkItem>()
                .HasOne(w => w.Epic)
                .WithMany(e => e.WorkItems)
                .HasForeignKey(w => w.EpicId);

            // WorkItem -> WorkItem (Subtask/StoryBug nested under a Story).
            // Self-referencing FKs can't cascade delete in SQL Server, so this is Restrict.
            modelBuilder.Entity<WorkItem>()
                .HasOne(w => w.ParentWorkItem)
                .WithMany(w => w.Children)
                .HasForeignKey(w => w.ParentWorkItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // Comment attaches to exactly one of Initiative/Epic/WorkItem. Restrict avoids the
            // multiple-cascade-paths error SQL Server raises since all three roots can reach Comment.
            modelBuilder.Entity<Comment>()
                .HasOne(c => c.Initiative)
                .WithMany(i => i.Comments)
                .HasForeignKey(c => c.InitiativeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Comment>()
                .HasOne(c => c.Epic)
                .WithMany(e => e.Comments)
                .HasForeignKey(c => c.EpicId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Comment>()
                .HasOne(c => c.WorkItem)
                .WithMany(w => w.Comments)
                .HasForeignKey(c => c.WorkItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // Summary tables: one-to-one via the FK, which EF Core enforces as unique automatically -
            // exactly one summary row per WorkItem/Epic/Initiative, overwritten fresh on every run.
            modelBuilder.Entity<WorkItemSummary>()
                .HasOne(s => s.WorkItem)
                .WithOne(w => w.Summary)
                .HasForeignKey<WorkItemSummary>(s => s.WorkItemId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EpicEngineeringSummary>()
                .HasOne(s => s.Epic)
                .WithOne(e => e.EngineeringSummary)
                .HasForeignKey<EpicEngineeringSummary>(s => s.EpicId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<InitiativeBusinessSummary>()
                .HasOne(s => s.Initiative)
                .WithOne(i => i.BusinessSummary)
                .HasForeignKey<InitiativeBusinessSummary>(s => s.InitiativeId)
                .OnDelete(DeleteBehavior.Cascade);

            OnModelCreatingPartial(modelBuilder);
        }

        /// <summary>Extension point for additional model configuration in another partial file; currently unused.</summary>
        /// <param name="modelBuilder">The model builder to configure.</param>
        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
