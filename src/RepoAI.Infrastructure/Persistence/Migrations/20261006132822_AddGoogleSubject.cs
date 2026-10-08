using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RepoAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleSubject",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "IX_users_GoogleSubject",
                table: "users",
                column: "GoogleSubject",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_GoogleSubject",
                table: "users");

            migrationBuilder.DropColumn(
                name: "GoogleSubject",
                table: "users");
        }
    }
}
