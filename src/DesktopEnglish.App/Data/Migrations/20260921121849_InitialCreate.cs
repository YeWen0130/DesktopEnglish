using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DesktopEnglish.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReminderInterval = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyWordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoStart = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotificationEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SoundEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    WordsPerReminder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Words",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WordText = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Phonetic = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Meaning = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    PartOfSpeech = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Example = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Words", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LearningRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Result = table.Column<int>(type: "INTEGER", nullable: false),
                    CorrectCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WrongCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MasteryLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    NextReviewTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearningRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearningRecords_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WordReviewStates",
                columns: table => new
                {
                    WordId = table.Column<int>(type: "INTEGER", nullable: false),
                    Stage = table.Column<int>(type: "INTEGER", nullable: false),
                    CorrectCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WrongCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MasteryLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    NextReviewTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordReviewStates", x => x.WordId);
                    table.ForeignKey(
                        name: "FK_WordReviewStates_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LearningRecords_ReviewTime",
                table: "LearningRecords",
                column: "ReviewTime");

            migrationBuilder.CreateIndex(
                name: "IX_LearningRecords_WordId",
                table: "LearningRecords",
                column: "WordId");

            migrationBuilder.CreateIndex(
                name: "IX_WordReviewStates_NextReviewTime",
                table: "WordReviewStates",
                column: "NextReviewTime");

            migrationBuilder.CreateIndex(
                name: "IX_Words_Category",
                table: "Words",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Words_Level",
                table: "Words",
                column: "Level");

            migrationBuilder.CreateIndex(
                name: "IX_Words_WordText",
                table: "Words",
                column: "WordText",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearningRecords");

            migrationBuilder.DropTable(
                name: "UserSettings");

            migrationBuilder.DropTable(
                name: "WordReviewStates");

            migrationBuilder.DropTable(
                name: "Words");
        }
    }
}
