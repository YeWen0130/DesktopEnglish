using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DesktopEnglish.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRemindersPaused : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RemindersPaused",
                table: "UserSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemindersPaused",
                table: "UserSettings");
        }
    }
}
