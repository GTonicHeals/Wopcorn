using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wopcorn.Server.Migrations
{
    /// <summary>
    /// Re-keys the catalog from a bare TMDB id to the title key, so films, series
    /// and seasons can coexist (plan 08).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written by hand.</b> The scaffolded version of this migration dropped
    /// <c>Films</c> and <c>FilmGenre</c> outright and added <c>TitleKey</c> to the
    /// two child tables with <c>defaultValue: ""</c> — schema-correct and a total
    /// loss of data. Every operation below that touches an existing table is
    /// therefore explicit: create the new shape, copy the rows across with the
    /// backfill applied, and only then drop the old one.
    /// </para>
    /// <para>
    /// The backfill is the whole of the conversion: every row that exists before
    /// this migration is a film, so its key is <c>'movie-' || TmdbId</c>,
    /// <c>MediaType = 1</c>, and no season number.
    /// </para>
    /// <para>
    /// The two child tables are rebuilt with raw SQL rather than
    /// <c>DropColumn</c>/<c>AddForeignKey</c>. SQLite cannot alter a foreign key in
    /// place, so those operations become a table rebuild either way; doing it here
    /// means the copy step is visible and reviewable instead of generated.
    /// </para>
    /// </remarks>
    public partial class SeriesAndSeasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- the catalog ------------------------------------------------

            migrationBuilder.CreateTable(
                name: "Titles",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    ParentKey = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    ReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PosterPath = table.Column<string>(type: "TEXT", nullable: true),
                    BackdropPath = table.Column<string>(type: "TEXT", nullable: true),
                    TmdbVoteAverage = table.Column<double>(type: "REAL", nullable: true),
                    RuntimeMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeCount = table.Column<int>(type: "INTEGER", nullable: true),
                    SeasonCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Overview = table.Column<string>(type: "TEXT", nullable: true),
                    Director = table.Column<string>(type: "TEXT", nullable: true),
                    CreatorsJson = table.Column<string>(type: "TEXT", nullable: true),
                    CastJson = table.Column<string>(type: "TEXT", nullable: true),
                    SummaryFetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DetailFetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Titles", x => x.Key);
                    table.ForeignKey(
                        name: "FK_Titles_Titles_ParentKey",
                        column: x => x.ParentKey,
                        principalTable: "Titles",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            // Every pre-existing row is a film: MediaType 1, no season number, and a
            // key of 'movie-' || TmdbId. The columns TV introduced stay null.
            migrationBuilder.Sql("""
                INSERT INTO "Titles" (
                    "Key", "MediaType", "TmdbId", "SeasonNumber", "ParentKey",
                    "Title", "ReleaseDate", "PosterPath", "BackdropPath", "TmdbVoteAverage",
                    "RuntimeMinutes", "EpisodeCount", "SeasonCount", "Overview",
                    "Director", "CreatorsJson", "CastJson", "SummaryFetchedAt", "DetailFetchedAt")
                SELECT
                    'movie-' || "TmdbId", 1, "TmdbId", NULL, NULL,
                    "Title", "ReleaseDate", "PosterPath", "BackdropPath", "TmdbVoteAverage",
                    "RuntimeMinutes", NULL, NULL, "Overview",
                    "Director", NULL, "CastJson", "SummaryFetchedAt", "DetailFetchedAt"
                FROM "Films";
                """);

            migrationBuilder.CreateTable(
                name: "TitleGenre",
                columns: table => new
                {
                    TitleKey = table.Column<string>(type: "TEXT", nullable: false),
                    GenreTmdbId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleGenre", x => new { x.TitleKey, x.GenreTmdbId });
                    table.ForeignKey(
                        name: "FK_TitleGenre_Genres_GenreTmdbId",
                        column: x => x.GenreTmdbId,
                        principalTable: "Genres",
                        principalColumn: "TmdbId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TitleGenre_Titles_TitleKey",
                        column: x => x.TitleKey,
                        principalTable: "Titles",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO "TitleGenre" ("TitleKey", "GenreTmdbId")
                SELECT 'movie-' || "FilmTmdbId", "GenreTmdbId" FROM "FilmGenre";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Titles_MediaType_TmdbId_SeasonNumber",
                table: "Titles",
                columns: new[] { "MediaType", "TmdbId", "SeasonNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Titles_ParentKey",
                table: "Titles",
                column: "ParentKey");

            migrationBuilder.CreateIndex(
                name: "IX_TitleGenre_GenreTmdbId",
                table: "TitleGenre",
                column: "GenreTmdbId");

            // --- the genre catalog gains a side -----------------------------

            migrationBuilder.AddColumn<bool>(
                name: "InMovies",
                table: "Genres",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "InTv",
                table: "Genres",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Everything mirrored so far came from /genre/movie/list. The TV flags
            // fill in on the next refresh; until then the filter sheet is exactly as
            // correct as it was before this migration.
            migrationBuilder.Sql("""UPDATE "Genres" SET "InMovies" = 1;""");

            // --- the two child tables ---------------------------------------

            // Rebuild rather than alter: SQLite cannot repoint a foreign key in
            // place. The SELECT is where the rows are actually carried over.
            migrationBuilder.Sql("""
                CREATE TABLE "ListEntries_new" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_ListEntries" PRIMARY KEY,
                    "AddedAt" INTEGER NOT NULL,
                    "TitleKey" TEXT NOT NULL,
                    "Kind" INTEGER NOT NULL,
                    "Position" INTEGER NULL,
                    "Rating" INTEGER NULL,
                    "UserId" TEXT NOT NULL,
                    "WatchedOn" TEXT NULL,
                    CONSTRAINT "FK_ListEntries_AspNetUsers_UserId" FOREIGN KEY ("UserId")
                        REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ListEntries_Titles_TitleKey" FOREIGN KEY ("TitleKey")
                        REFERENCES "Titles" ("Key") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO "ListEntries_new" (
                    "Id", "AddedAt", "TitleKey", "Kind", "Position", "Rating", "UserId", "WatchedOn")
                SELECT
                    "Id", "AddedAt", 'movie-' || "FilmTmdbId", "Kind",
                    "Position", "Rating", "UserId", "WatchedOn"
                FROM "ListEntries";
                """);

            migrationBuilder.Sql("""DROP TABLE "ListEntries";""");
            migrationBuilder.Sql("""ALTER TABLE "ListEntries_new" RENAME TO "ListEntries";""");

            migrationBuilder.Sql("""
                CREATE TABLE "ActivityEvents_new" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_ActivityEvents" PRIMARY KEY,
                    "TitleKey" TEXT NOT NULL,
                    "Kind" INTEGER NOT NULL,
                    "OccurredAt" INTEGER NOT NULL,
                    "Rating" INTEGER NULL,
                    "UserId" TEXT NOT NULL,
                    CONSTRAINT "FK_ActivityEvents_AspNetUsers_UserId" FOREIGN KEY ("UserId")
                        REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ActivityEvents_Titles_TitleKey" FOREIGN KEY ("TitleKey")
                        REFERENCES "Titles" ("Key") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO "ActivityEvents_new" (
                    "Id", "TitleKey", "Kind", "OccurredAt", "Rating", "UserId")
                SELECT
                    "Id", 'movie-' || "FilmTmdbId", "Kind", "OccurredAt", "Rating", "UserId"
                FROM "ActivityEvents";
                """);

            migrationBuilder.Sql("""DROP TABLE "ActivityEvents";""");
            migrationBuilder.Sql("""ALTER TABLE "ActivityEvents_new" RENAME TO "ActivityEvents";""");

            // Dropping the tables took their indexes with them, so all four are
            // recreated — including the two that have nothing to do with this change.
            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_TitleKey",
                table: "ListEntries",
                column: "TitleKey");

            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_UserId_TitleKey_Kind",
                table: "ListEntries",
                columns: new[] { "UserId", "TitleKey", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_UserId_Kind",
                table: "ListEntries",
                columns: new[] { "UserId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_TitleKey",
                table: "ActivityEvents",
                column: "TitleKey");

            migrationBuilder.Sql("""
                CREATE INDEX "IX_ActivityEvents_UserId_OccurredAt"
                ON "ActivityEvents" ("UserId", "OccurredAt" DESC);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX "IX_ActivityEvents_OccurredAt_Id"
                ON "ActivityEvents" ("OccurredAt" DESC, "Id");
                """);

            // --- the old catalog, now empty of anything not copied ----------

            migrationBuilder.DropTable(name: "FilmGenre");
            migrationBuilder.DropTable(name: "Films");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The mirror image. Series and season rows have no representation in the
            // old schema, so they are dropped along with every entry pointing at one
            // — the only lossy step, and unavoidable: the old catalog cannot express
            // them.
            migrationBuilder.CreateTable(
                name: "Films",
                columns: table => new
                {
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    BackdropPath = table.Column<string>(type: "TEXT", nullable: true),
                    CastJson = table.Column<string>(type: "TEXT", nullable: true),
                    DetailFetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Director = table.Column<string>(type: "TEXT", nullable: true),
                    Overview = table.Column<string>(type: "TEXT", nullable: true),
                    PosterPath = table.Column<string>(type: "TEXT", nullable: true),
                    ReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    RuntimeMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    SummaryFetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    TmdbVoteAverage = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_Films", x => x.TmdbId));

            migrationBuilder.Sql("""
                INSERT INTO "Films" (
                    "TmdbId", "Title", "ReleaseDate", "PosterPath", "BackdropPath",
                    "TmdbVoteAverage", "RuntimeMinutes", "Overview", "Director",
                    "CastJson", "SummaryFetchedAt", "DetailFetchedAt")
                SELECT
                    "TmdbId", "Title", "ReleaseDate", "PosterPath", "BackdropPath",
                    "TmdbVoteAverage", "RuntimeMinutes", "Overview", "Director",
                    "CastJson", "SummaryFetchedAt", "DetailFetchedAt"
                FROM "Titles" WHERE "MediaType" = 1;
                """);

            migrationBuilder.CreateTable(
                name: "FilmGenre",
                columns: table => new
                {
                    FilmTmdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    GenreTmdbId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmGenre", x => new { x.FilmTmdbId, x.GenreTmdbId });
                    table.ForeignKey(
                        name: "FK_FilmGenre_Films_FilmTmdbId",
                        column: x => x.FilmTmdbId,
                        principalTable: "Films",
                        principalColumn: "TmdbId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FilmGenre_Genres_GenreTmdbId",
                        column: x => x.GenreTmdbId,
                        principalTable: "Genres",
                        principalColumn: "TmdbId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO "FilmGenre" ("FilmTmdbId", "GenreTmdbId")
                SELECT t."TmdbId", g."GenreTmdbId"
                FROM "TitleGenre" g
                JOIN "Titles" t ON t."Key" = g."TitleKey"
                WHERE t."MediaType" = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_FilmGenre_GenreTmdbId",
                table: "FilmGenre",
                column: "GenreTmdbId");

            migrationBuilder.Sql("""
                CREATE TABLE "ListEntries_old" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_ListEntries" PRIMARY KEY,
                    "AddedAt" INTEGER NOT NULL,
                    "FilmTmdbId" INTEGER NOT NULL,
                    "Kind" INTEGER NOT NULL,
                    "Position" INTEGER NULL,
                    "Rating" INTEGER NULL,
                    "UserId" TEXT NOT NULL,
                    "WatchedOn" TEXT NULL,
                    CONSTRAINT "FK_ListEntries_AspNetUsers_UserId" FOREIGN KEY ("UserId")
                        REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ListEntries_Films_FilmTmdbId" FOREIGN KEY ("FilmTmdbId")
                        REFERENCES "Films" ("TmdbId") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO "ListEntries_old" (
                    "Id", "AddedAt", "FilmTmdbId", "Kind", "Position", "Rating", "UserId", "WatchedOn")
                SELECT
                    e."Id", e."AddedAt", t."TmdbId", e."Kind",
                    e."Position", e."Rating", e."UserId", e."WatchedOn"
                FROM "ListEntries" e
                JOIN "Titles" t ON t."Key" = e."TitleKey"
                WHERE t."MediaType" = 1;
                """);

            migrationBuilder.Sql("""DROP TABLE "ListEntries";""");
            migrationBuilder.Sql("""ALTER TABLE "ListEntries_old" RENAME TO "ListEntries";""");

            migrationBuilder.Sql("""
                CREATE TABLE "ActivityEvents_old" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_ActivityEvents" PRIMARY KEY,
                    "FilmTmdbId" INTEGER NOT NULL,
                    "Kind" INTEGER NOT NULL,
                    "OccurredAt" INTEGER NOT NULL,
                    "Rating" INTEGER NULL,
                    "UserId" TEXT NOT NULL,
                    CONSTRAINT "FK_ActivityEvents_AspNetUsers_UserId" FOREIGN KEY ("UserId")
                        REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_ActivityEvents_Films_FilmTmdbId" FOREIGN KEY ("FilmTmdbId")
                        REFERENCES "Films" ("TmdbId") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO "ActivityEvents_old" (
                    "Id", "FilmTmdbId", "Kind", "OccurredAt", "Rating", "UserId")
                SELECT
                    a."Id", t."TmdbId", a."Kind", a."OccurredAt", a."Rating", a."UserId"
                FROM "ActivityEvents" a
                JOIN "Titles" t ON t."Key" = a."TitleKey"
                WHERE t."MediaType" = 1;
                """);

            migrationBuilder.Sql("""DROP TABLE "ActivityEvents";""");
            migrationBuilder.Sql("""ALTER TABLE "ActivityEvents_old" RENAME TO "ActivityEvents";""");

            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_FilmTmdbId",
                table: "ListEntries",
                column: "FilmTmdbId");

            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_UserId_FilmTmdbId_Kind",
                table: "ListEntries",
                columns: new[] { "UserId", "FilmTmdbId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListEntries_UserId_Kind",
                table: "ListEntries",
                columns: new[] { "UserId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEvents_FilmTmdbId",
                table: "ActivityEvents",
                column: "FilmTmdbId");

            migrationBuilder.Sql("""
                CREATE INDEX "IX_ActivityEvents_UserId_OccurredAt"
                ON "ActivityEvents" ("UserId", "OccurredAt" DESC);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX "IX_ActivityEvents_OccurredAt_Id"
                ON "ActivityEvents" ("OccurredAt" DESC, "Id");
                """);

            migrationBuilder.DropTable(name: "TitleGenre");
            migrationBuilder.DropTable(name: "Titles");

            migrationBuilder.DropColumn(name: "InMovies", table: "Genres");
            migrationBuilder.DropColumn(name: "InTv", table: "Genres");
        }
    }
}
