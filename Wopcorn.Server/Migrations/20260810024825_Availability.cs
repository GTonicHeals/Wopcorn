using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wopcorn.Server.Migrations
{
    /// <inheritdoc />
    public partial class Availability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TitleAvailability",
                columns: table => new
                {
                    TitleKey = table.Column<string>(type: "TEXT", nullable: false),
                    Region = table.Column<string>(type: "TEXT", nullable: false),
                    JustWatchLink = table.Column<string>(type: "TEXT", nullable: true),
                    FetchedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleAvailability", x => new { x.TitleKey, x.Region });
                    table.ForeignKey(
                        name: "FK_TitleAvailability_Titles_TitleKey",
                        column: x => x.TitleKey,
                        principalTable: "Titles",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WatchProviders",
                columns: table => new
                {
                    TmdbProviderId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LogoPath = table.Column<string>(type: "TEXT", nullable: true),
                    DisplayPriority = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchProviders", x => x.TmdbProviderId);
                });

            migrationBuilder.CreateTable(
                name: "TitleOffers",
                columns: table => new
                {
                    TitleKey = table.Column<string>(type: "TEXT", nullable: false),
                    Region = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleOffers", x => new { x.TitleKey, x.Region, x.ProviderId, x.Kind });
                    table.ForeignKey(
                        name: "FK_TitleOffers_TitleAvailability_TitleKey_Region",
                        columns: x => new { x.TitleKey, x.Region },
                        principalTable: "TitleAvailability",
                        principalColumns: new[] { "TitleKey", "Region" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TitleOffers_WatchProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "WatchProviders",
                        principalColumn: "TmdbProviderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserWatchProviders",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserWatchProviders", x => new { x.UserId, x.ProviderId });
                    table.ForeignKey(
                        name: "FK_UserWatchProviders_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserWatchProviders_WatchProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "WatchProviders",
                        principalColumn: "TmdbProviderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TitleAvailability_FetchedAt",
                table: "TitleAvailability",
                column: "FetchedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TitleOffers_ProviderId",
                table: "TitleOffers",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_TitleOffers_Region_ProviderId",
                table: "TitleOffers",
                columns: new[] { "Region", "ProviderId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserWatchProviders_ProviderId",
                table: "UserWatchProviders",
                column: "ProviderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TitleOffers");

            migrationBuilder.DropTable(
                name: "UserWatchProviders");

            migrationBuilder.DropTable(
                name: "TitleAvailability");

            migrationBuilder.DropTable(
                name: "WatchProviders");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "AspNetUsers");
        }
    }
}
