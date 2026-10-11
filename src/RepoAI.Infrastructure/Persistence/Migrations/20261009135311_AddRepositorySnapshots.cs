using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RepoAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositorySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repository_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepositoryUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CommitSha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ResolvedRef = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DefaultBranch = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FileCount = table.Column<int>(type: "integer", nullable: false),
                    TextFileCount = table.Column<int>(type: "integer", nullable: false),
                    BinaryFileCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedFileCount = table.Column<int>(type: "integer", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repository_snapshots", x => x.Id);
                    table.CheckConstraint("CK_repository_snapshots_counts", "\"FileCount\" >= 0 AND \"TextFileCount\" >= 0 AND \"BinaryFileCount\" >= 0 AND \"SkippedFileCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_repository_snapshots_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repository_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    DetectedLanguage = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    IsBinary = table.Column<bool>(type: "boolean", nullable: false),
                    ContentStorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    SkipReason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repository_files", x => x.Id);
                    table.CheckConstraint("CK_repository_files_size", "\"SizeBytes\" >= 0");
                    table.ForeignKey(
                        name: "FK_repository_files_repository_snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "repository_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_repository_files_SnapshotId_DetectedLanguage",
                table: "repository_files",
                columns: new[] { "SnapshotId", "DetectedLanguage" });

            migrationBuilder.CreateIndex(
                name: "IX_repository_snapshots_ProjectId_ImportedAtUtc",
                table: "repository_snapshots",
                columns: new[] { "ProjectId", "ImportedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repository_files");

            migrationBuilder.DropTable(
                name: "repository_snapshots");
        }
    }
}
