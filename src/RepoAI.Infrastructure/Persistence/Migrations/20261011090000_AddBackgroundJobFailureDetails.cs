using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RepoAI.Infrastructure.Persistence;

namespace RepoAI.Infrastructure.Persistence.Migrations;

[DbContext(typeof(RepoAIDbContext))]
[Migration("20261011090000_AddBackgroundJobFailureDetails")]
public sealed class AddBackgroundJobFailureDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "FailureMessage",
            table: "background_jobs",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(
            name: "FailureMessage",
            table: "background_jobs");
}
