using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AchievementLadder.Migrations
{
    /// <inheritdoc />
    public partial class AddMountCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MountCount",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MountCount",
                table: "Players");
        }
    }
}
