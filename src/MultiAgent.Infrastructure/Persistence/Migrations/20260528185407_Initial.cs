using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MultiAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentTraces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Step = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Input = table.Column<string>(type: "TEXT", nullable: false),
                    Output = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTraces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompanyResearch",
                columns: table => new
                {
                    Website = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CompanyDescription = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    KnownPainPoints = table.Column<string>(type: "TEXT", nullable: false),
                    RecentNews = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyResearch", x => x.Website);
                });

            migrationBuilder.CreateTable(
                name: "Leads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContactName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContactEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    Website = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Industry = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CrmNotes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Stage = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: true),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    ScoreReason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToAddress = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    FinalScoreJson = table.Column<string>(type: "TEXT", nullable: true),
                    FinalDraftJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTraces_RunId",
                table: "AgentTraces",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTraces_Timestamp",
                table: "AgentTraces",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_UpdatedAt",
                table: "Leads",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Timestamp",
                table: "Notifications",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxItems_GeneratedAt",
                table: "OutboxItems",
                column: "GeneratedAt");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxItems_RunId",
                table: "OutboxItems",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRuns_LeadId",
                table: "WorkflowRuns",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRuns_StartedAt",
                table: "WorkflowRuns",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentTraces");

            migrationBuilder.DropTable(
                name: "CompanyResearch");

            migrationBuilder.DropTable(
                name: "Leads");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "OutboxItems");

            migrationBuilder.DropTable(
                name: "WorkflowRuns");
        }
    }
}
