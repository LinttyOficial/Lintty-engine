using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lintty.WebInspector.Persistence.Migrations
{
    /// <summary>
    /// Sprint 3 PR S3 — drops <c>scans.target</c>, reverting the PR S2
    /// per-scan target column. Goes back to "1 trigger = 1 scan row =
    /// 1 PDF" with the worker reading the user's saved selection from
    /// <c>repos.scan_projects</c> (PR S1) and synthesising a runtime
    /// <c>.lintty-runtime.yml</c> when the user picked multiple csprojs.
    ///
    /// <para>
    /// <b>Reversible.</b> <c>Down()</c> recreates the column nullable.
    /// Existing rows lose any <c>target</c> value on rollforward; that's
    /// acceptable — the column was opt-in (NULL meant auto-detect, the
    /// prior default), so the only loss is per-row target attribution
    /// for scans triggered between PR S2 and PR S3 in dev.
    /// </para>
    /// </summary>
    public partial class Drop_Scan_Target : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "target",
                table: "scans");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "target",
                table: "scans",
                type: "text",
                nullable: true);
        }
    }
}
