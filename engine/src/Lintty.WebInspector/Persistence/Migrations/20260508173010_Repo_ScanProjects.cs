using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lintty.WebInspector.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Repo_ScanProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "scan_projects",
                table: "repos",
                type: "text[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "scan_projects",
                table: "repos");
        }
    }
}
