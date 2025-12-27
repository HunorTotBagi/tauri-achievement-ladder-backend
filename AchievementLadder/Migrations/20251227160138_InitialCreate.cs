using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AchievementLadder.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Race = table.Column<int>(type: "integer", nullable: false),
                    Gender = table.Column<int>(type: "integer", nullable: false),
                    Class = table.Column<int>(type: "integer", nullable: false),
                    Realm = table.Column<string>(type: "text", nullable: false),
                    Guild = table.Column<string>(type: "text", nullable: false),
                    AchievementPoints = table.Column<int>(type: "integer", nullable: false),
                    HonorableKills = table.Column<int>(type: "integer", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Players_Name_Race_Gender_Class_Realm_Guild_AchievementPoint~",
                table: "Players",
                columns: new[] { "Name", "Race", "Gender", "Class", "Realm", "Guild", "AchievementPoints", "HonorableKills", "LastUpdated" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Players");
        }
    }
}
