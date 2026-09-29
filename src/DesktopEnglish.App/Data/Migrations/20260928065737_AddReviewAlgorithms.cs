using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DesktopEnglish.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewAlgorithms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Algorithm",
                table: "WordReviewStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AlgorithmVersion",
                table: "WordReviewStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<double>(
                name: "FsrsDifficulty",
                table: "WordReviewStates",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "FsrsStability",
                table: "WordReviewStates",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Sm2EaseFactor",
                table: "WordReviewStates",
                type: "REAL",
                nullable: false,
                defaultValue: 2.5);

            migrationBuilder.AddColumn<double>(
                name: "Sm2IntervalDays",
                table: "WordReviewStates",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "Sm2Repetitions",
                table: "WordReviewStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "FsrsDesiredRetention",
                table: "UserSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.90000000000000002);

            migrationBuilder.AddColumn<int>(
                name: "ReviewAlgorithm",
                table: "UserSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Algorithm",
                table: "LearningRecords",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Algorithm",
                table: "WordReviewStates");

            migrationBuilder.DropColumn(
                name: "AlgorithmVersion",
                table: "WordReviewStates");

            migrationBuilder.DropColumn(
                name: "FsrsDifficulty",
                table: "WordReviewStates");

            migrationBuilder.DropColumn(
                name: "FsrsStability",
                table: "WordReviewStates");

            migrationBuilder.DropColumn(
                name: "Sm2EaseFactor",
                table: "WordReviewStates");

            migrationBuilder.DropColumn(
                name: "Sm2IntervalDays",
                table: "WordReviewStates");

            migrationBuilder.DropColumn(
                name: "Sm2Repetitions",
                table: "WordReviewStates");

            migrationBuilder.DropColumn(
                name: "FsrsDesiredRetention",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "ReviewAlgorithm",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "Algorithm",
                table: "LearningRecords");
        }
    }
}
