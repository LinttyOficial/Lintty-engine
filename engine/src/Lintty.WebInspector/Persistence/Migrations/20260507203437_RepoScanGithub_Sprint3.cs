using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lintty.WebInspector.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepoScanGithub_Sprint3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,");

            migrationBuilder.CreateTable(
                name: "github_orgs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    github_org_id = table.Column<long>(type: "bigint", nullable: false),
                    github_org_login = table.Column<string>(type: "text", nullable: false),
                    github_org_avatar_url = table.Column<string>(type: "text", nullable: true),
                    connected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_github_orgs", x => x.id);
                    table.ForeignKey(
                        name: "fk_github_orgs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "github_user_tokens",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    encrypted_token = table.Column<byte[]>(type: "bytea", nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    granted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_github_user_tokens", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_github_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    org_id = table.Column<long>(type: "bigint", nullable: false),
                    added_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    github_url = table.Column<string>(type: "text", nullable: false),
                    github_repo_id = table.Column<long>(type: "bigint", nullable: true),
                    github_org_login = table.Column<string>(type: "text", nullable: true),
                    default_branch = table.Column<string>(type: "text", nullable: true),
                    is_private = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repos", x => x.id);
                    table.ForeignKey(
                        name: "fk_repos_orgs_org_id",
                        column: x => x.org_id,
                        principalTable: "orgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_repos_users_added_by_user_id",
                        column: x => x.added_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scans",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    org_id = table.Column<long>(type: "bigint", nullable: false),
                    repo_id = table.Column<long>(type: "bigint", nullable: false),
                    triggered_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    @ref = table.Column<string>(name: "ref", type: "text", nullable: true),
                    canon_version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    queued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() at time zone 'utc'"),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    hash_content = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scans", x => x.id);
                    table.CheckConstraint("ck_scans_status", "status IN ('queued','running','completed','failed','cancelled')");
                    table.ForeignKey(
                        name: "fk_scans_orgs_org_id",
                        column: x => x.org_id,
                        principalTable: "orgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scans_repos_repo_id",
                        column: x => x.repo_id,
                        principalTable: "repos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_scans_users_triggered_by_user_id",
                        column: x => x.triggered_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_github_orgs_user_github_org",
                table: "github_orgs",
                columns: new[] { "user_id", "github_org_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_github_user_tokens_active",
                table: "github_user_tokens",
                column: "user_id",
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_repos_added_by_user_id",
                table: "repos",
                column: "added_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_repos_github_repo_id",
                table: "repos",
                column: "github_repo_id",
                filter: "github_repo_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "uq_repos_org_url_active",
                table: "repos",
                columns: new[] { "org_id", "github_url" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scans_org_repo_queued_at",
                table: "scans",
                columns: new[] { "org_id", "repo_id", "queued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_scans_repo_id",
                table: "scans",
                column: "repo_id");

            migrationBuilder.CreateIndex(
                name: "ix_scans_status_active",
                table: "scans",
                column: "status",
                filter: "status IN ('queued','running')");

            migrationBuilder.CreateIndex(
                name: "ix_scans_triggered_by_user_id",
                table: "scans",
                column: "triggered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "uq_scans_public_id",
                table: "scans",
                column: "public_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "github_orgs");

            migrationBuilder.DropTable(
                name: "github_user_tokens");

            migrationBuilder.DropTable(
                name: "scans");

            migrationBuilder.DropTable(
                name: "repos");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pgcrypto", ",,");
        }
    }
}
