using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SakugaVault.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Anime",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Slug = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Synopsis = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    PosterImageUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    BackdropImageUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    EpisodeCount = table.Column<int>(type: "int", nullable: false),
                    RuntimeMinutes = table.Column<int>(type: "int", nullable: false),
                    SubAvailable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DubAvailable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TrendingRank = table.Column<int>(type: "int", nullable: false),
                    ExternalMetadataId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    MetadataProvider = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    MetadataLastSyncedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Anime", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    Slug = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AnimeGenres",
                columns: table => new
                {
                    AnimeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GenreId = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnimeGenres", x => new { x.AnimeId, x.GenreId });
                    table.ForeignKey(
                        name: "FK_AnimeGenres_Anime_AnimeId",
                        column: x => x.AnimeId,
                        principalTable: "Anime",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnimeGenres_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AnimeComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    AnimeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Body = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnimeComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnimeComments_Anime_AnimeId",
                        column: x => x.AnimeId,
                        principalTable: "Anime",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnimeComments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Token = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsRevoked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "WatchHistoryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    AnimeId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "int", nullable: false),
                    PositionSeconds = table.Column<int>(type: "int", nullable: false),
                    DurationSeconds = table.Column<int>(type: "int", nullable: false),
                    Completed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LastWatchedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchHistoryEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WatchHistoryEntries_Anime_AnimeId",
                        column: x => x.AnimeId,
                        principalTable: "Anime",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WatchHistoryEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Anime_Slug",
                table: "Anime",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Anime_TrendingRank",
                table: "Anime",
                column: "TrendingRank");

            migrationBuilder.CreateIndex(
                name: "IX_AnimeComments_AnimeId_CreatedAtUtc",
                table: "AnimeComments",
                columns: new[] { "AnimeId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AnimeComments_UserId",
                table: "AnimeComments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AnimeGenres_GenreId",
                table: "AnimeGenres",
                column: "GenreId");

            migrationBuilder.CreateIndex(
                name: "IX_Genres_Name",
                table: "Genres",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Genres_Slug",
                table: "Genres",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_ExpiresAtUtc",
                table: "RefreshTokens",
                columns: new[] { "UserId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedUserName",
                table: "Users",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchHistoryEntries_AnimeId",
                table: "WatchHistoryEntries",
                column: "AnimeId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchHistoryEntries_UserId_AnimeId_EpisodeNumber",
                table: "WatchHistoryEntries",
                columns: new[] { "UserId", "AnimeId", "EpisodeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchHistoryEntries_UserId_LastWatchedAtUtc",
                table: "WatchHistoryEntries",
                columns: new[] { "UserId", "LastWatchedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnimeComments");

            migrationBuilder.DropTable(
                name: "AnimeGenres");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "WatchHistoryEntries");

            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "Anime");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
