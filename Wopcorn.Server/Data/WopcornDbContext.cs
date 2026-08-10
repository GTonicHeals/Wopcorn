using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Wopcorn.Server.Data.Entities;

namespace Wopcorn.Server.Data;

public class WopcornDbContext(DbContextOptions<WopcornDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Title> Titles => Set<Title>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<ListEntry> ListEntries => Set<ListEntry>();
    public DbSet<FavoriteTitle> Favorites => Set<FavoriteTitle>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<WatchProvider> WatchProviders => Set<WatchProvider>();
    public DbSet<TitleAvailability> TitleAvailability => Set<TitleAvailability>();
    public DbSet<TitleOffer> TitleOffers => Set<TitleOffer>();
    public DbSet<UserWatchProvider> UserWatchProviders => Set<UserWatchProvider>();

    /// <summary>
    /// Identity keeps its newer tables behind a schema version so that upgrading
    /// the package cannot silently rewrite an existing database. Version 3 is the
    /// one that maps <c>AspNetUserPasskeys</c>; without it the entity is simply
    /// absent from the model and <c>migrations add</c> produces an empty migration.
    /// </summary>
    /// <remarks>
    /// This has to agree with <c>IdentityOptions.Stores.SchemaVersion</c> in
    /// Program.cs. The context drives the schema, the options drive the store, and
    /// a mismatch fails at query time rather than at build time.
    /// </remarks>
    protected override Version SchemaVersion => IdentitySchemaVersions.Version3;

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>(e =>
        {
            // FR-A2: display names are unique across the instance.
            e.HasIndex(u => u.DisplayName).IsUnique();
        });

        b.Entity<Title>(e =>
        {
            e.HasKey(t => t.Key);
            e.Property(t => t.Key).ValueGeneratedNever();

            // `Name` on the entity, `Title` in the database: Title.Title inside a
            // class called Title is unreadable, and keeping the column name is what
            // lets the migration carry the data across instead of rewriting it.
            e.Property(t => t.Name).HasColumnName("Title");

            // The parts of the key, which the key itself is derived from. Unique
            // because two rows with the same identity would be two spellings of one
            // title — the key's own uniqueness already implies it, and this says so
            // to the database as well.
            e.HasIndex(t => new { t.MediaType, t.TmdbId, t.SeasonNumber }).IsUnique();
            e.HasIndex(t => t.ParentKey);

            // A season points at its series. Restrict rather than Cascade: deleting a
            // series out from under its seasons is not something any code path does,
            // and a self-referencing cascade is a trap worth not laying.
            e.HasOne(t => t.Parent)
                .WithMany()
                .HasForeignKey(t => t.ParentKey)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Genre>(e =>
        {
            e.HasKey(g => g.TmdbId);
            e.Property(g => g.TmdbId).ValueGeneratedNever();
        });

        b.Entity<TitleGenre>(e =>
        {
            e.HasKey(tg => new { tg.TitleKey, tg.GenreTmdbId });
            e.HasOne(tg => tg.Title)
                .WithMany(t => t.Genres)
                .HasForeignKey(tg => tg.TitleKey);
            e.HasOne(tg => tg.Genre)
                .WithMany()
                .HasForeignKey(tg => tg.GenreTmdbId);
        });

        b.Entity<ListEntry>(e =>
        {
            // `sort=added` orders by this column, which SQLite cannot do on a
            // DateTimeOffset — see UtcInstantConverter.
            e.Property(l => l.AddedAt).HasConversion<UtcInstantConverter>();
            e.HasIndex(l => new { l.UserId, l.TitleKey, l.Kind }).IsUnique();
            e.HasIndex(l => new { l.UserId, l.Kind });
            e.HasOne(l => l.User)
                .WithMany()
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.Title)
                .WithMany()
                .HasForeignKey(l => l.TitleKey)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FavoriteTitle>(e =>
        {
            // One title can hold one slot. The showcase is rewritten as a whole, so
            // this index is what stops a half-applied write leaving a duplicate.
            e.HasIndex(f => new { f.UserId, f.TitleKey }).IsUnique();
            e.HasIndex(f => new { f.UserId, f.Position });
            e.HasOne(f => f.User)
                .WithMany()
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.Title)
                .WithMany()
                .HasForeignKey(f => f.TitleKey)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Friendship>(e =>
        {
            // Same reason as ListEntry.AddedAt: an instant that anything orders or
            // compares has to be stored sortably — see UtcInstantConverter.
            e.Property(f => f.CreatedAt).HasConversion<UtcInstantConverter>();
            e.HasIndex(f => new { f.UserAId, f.UserBId }).IsUnique();
            // Both FKs point at AppUser; cascading either way would give SQLite
            // (and SQL Server) multiple cascade paths into the same table.
            e.HasOne(f => f.UserA)
                .WithMany()
                .HasForeignKey(f => f.UserAId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(f => f.UserB)
                .WithMany()
                .HasForeignKey(f => f.UserBId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<FriendRequest>(e =>
        {
            // GET /api/friends returns both request directions newest-first, so
            // SentAt is ordered on in SQL.
            e.Property(f => f.SentAt).HasConversion<UtcInstantConverter>();
            e.HasIndex(f => new { f.FromUserId, f.ToUserId }).IsUnique();
            e.HasOne(f => f.FromUser)
                .WithMany()
                .HasForeignKey(f => f.FromUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(f => f.ToUser)
                .WithMany()
                .HasForeignKey(f => f.ToUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ActivityEvent>(e =>
        {
            // Both indexes below exist to be ordered on, so the same conversion
            // applies — be-04's keyset paging depends on it.
            e.Property(a => a.OccurredAt).HasConversion<UtcInstantConverter>();
            e.HasIndex(a => new { a.UserId, a.OccurredAt }).IsDescending(false, true);
            // Keyset paging (FR-G3): newest first, Id as the tiebreaker.
            e.HasIndex(a => new { a.OccurredAt, a.Id }).IsDescending(true, false);
            e.HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Title)
                .WithMany()
                .HasForeignKey(a => a.TitleKey)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // --- streaming availability (09) -----------------------------------

        b.Entity<WatchProvider>(e =>
        {
            e.HasKey(p => p.TmdbProviderId);
            e.Property(p => p.TmdbProviderId).ValueGeneratedNever();
        });

        b.Entity<TitleAvailability>(e =>
        {
            e.HasKey(a => new { a.TitleKey, a.Region });

            // The warmer orders by this to pick the stalest rows first, which SQLite
            // cannot do on a DateTimeOffset — see UtcInstantConverter. It fails at
            // query time, not build time, which is exactly why it is written here
            // rather than left to be discovered.
            e.Property(a => a.FetchedAt).HasConversion<UtcInstantConverter>();
            e.HasIndex(a => a.FetchedAt);

            e.HasOne(a => a.Title)
                .WithMany()
                .HasForeignKey(a => a.TitleKey)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TitleOffer>(e =>
        {
            e.HasKey(o => new { o.TitleKey, o.Region, o.ProviderId, o.Kind });

            // The direction the Queue filter reads this table: "which titles does
            // provider 8 carry in BE", never "what carries this title".
            e.HasIndex(o => new { o.Region, o.ProviderId });

            e.HasOne<TitleAvailability>()
                .WithMany()
                .HasForeignKey(o => new { o.TitleKey, o.Region })
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(o => o.Provider)
                .WithMany()
                .HasForeignKey(o => o.ProviderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserWatchProvider>(e =>
        {
            e.HasKey(u => new { u.UserId, u.ProviderId });

            e.HasOne(u => u.User)
                .WithMany()
                .HasForeignKey(u => u.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(u => u.Provider)
                .WithMany()
                .HasForeignKey(u => u.ProviderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
