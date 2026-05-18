using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SakugaVault.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    AnimeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "int", nullable: false),
                    PreferredLanguage = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    Quality = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadRequests_Anime_AnimeId",
                        column: x => x.AnimeId,
                        principalTable: "Anime",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadRequests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadRequests_AnimeId",
                table: "DownloadRequests",
                column: "AnimeId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadRequests_UserId_AnimeId_EpisodeNumber_PreferredLangu~",
                table: "DownloadRequests",
                columns: new[] { "UserId", "AnimeId", "EpisodeNumber", "PreferredLanguage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadRequests_UserId_CreatedAtUtc",
                table: "DownloadRequests",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadRequests");
        }
    }
}
