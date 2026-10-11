using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RepoAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositoryStoryboards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repository_storyboards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommitSha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StoryboardJson = table.Column<string>(type: "jsonb", nullable: false),
                    GeneratedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repository_storyboards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_repository_storyboards_repository_snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "repository_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_repository_storyboards_SnapshotId",
                table: "repository_storyboards",
                column: "SnapshotId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repository_storyboards");
        }
    }
}
