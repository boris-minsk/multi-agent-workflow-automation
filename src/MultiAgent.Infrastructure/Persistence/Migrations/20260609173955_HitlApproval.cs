using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MultiAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HitlApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingStateJson",
                table: "WorkflowRuns",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingStateJson",
                table: "WorkflowRuns");
        }
    }
}
