using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lintty.WebInspector.Persistence.Migrations
{
    /// <summary>
    /// Sprint 3 PR S2 — per-scan target. Adds <c>scans.target</c> (nullable
    /// text). One target per scan row; triggering with N targets now creates
    /// N rows, each producing its own PDF. Replaces the prior model where a
    /// multi-csproj selection was merged into a single combined scan via a
    /// runtime <c>.lintty-runtime.yml</c>.
    ///
    /// <para>
    /// <b>Reversible by design.</b> <c>Down()</c> drops the column. Existing
    /// rows lose their target on rollback; that's acceptable because the
    /// column is opt-in (NULL means auto-detect, which was the prior
    /// default for single-target scans). Multi-target scans created post-S2
    /// would still survive as multiple completed rows — only their
    /// per-row target attribution is lost on rollback.
    /// </para>
    /// </summary>
    public partial class Scan_Target : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "target",
                table: "scans",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "target",
                table: "scans");
        }
    }
}
