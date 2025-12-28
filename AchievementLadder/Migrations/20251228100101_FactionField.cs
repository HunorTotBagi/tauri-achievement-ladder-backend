using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AchievementLadder.Migrations
{
    /// <inheritdoc />
    public partial class FactionField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Faction",
                table: "Players",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Faction",
                table: "Players");
        }
    }
}
