using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JiraRollupAgent.DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddSummaryTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EpicEngineeringSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EpicId = table.Column<int>(type: "int", nullable: false),
                    SummaryText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RangeStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RangeEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpicEngineeringSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpicEngineeringSummaries_Epics_EpicId",
                        column: x => x.EpicId,
                        principalTable: "Epics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InitiativeBusinessSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InitiativeId = table.Column<int>(type: "int", nullable: false),
                    SummaryText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RangeStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RangeEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InitiativeBusinessSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InitiativeBusinessSummaries_Initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "Initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkItemId = table.Column<int>(type: "int", nullable: false),
                    SummaryText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RangeStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RangeEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemSummaries_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EpicEngineeringSummaries_EpicId",
                table: "EpicEngineeringSummaries",
                column: "EpicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeBusinessSummaries_InitiativeId",
                table: "InitiativeBusinessSummaries",
                column: "InitiativeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemSummaries_WorkItemId",
                table: "WorkItemSummaries",
                column: "WorkItemId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EpicEngineeringSummaries");

            migrationBuilder.DropTable(
                name: "InitiativeBusinessSummaries");

            migrationBuilder.DropTable(
                name: "WorkItemSummaries");
        }
    }
}
