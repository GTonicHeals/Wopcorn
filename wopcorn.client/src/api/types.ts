/**
 * Wire types, transcribed from plans/API-CONTRACT.md.
 *
 * This file is the single client-side declaration of the HTTP contract. Every
 * other module imports from here — do not redeclare these shapes anywhere else,
 * and do not change one without editing API-CONTRACT.md first.
 */

// ---------------------------------------------------------------- shared DTOs

export type UserSummary = {
  id: string; // GUID
  displayName: string;
  avatarUrl: string | null;
};

/**
 * The signed-in user's own view of themself, from `GET /api/me`. `UserSummary`
 * deliberately does not carry these — it also describes friends, and a friend's
 * region and subscriptions are their business.
 */
export type Me = UserSummary & {
  region: string | null; // ISO-3166-1 alpha-2; null until set
  providerIds: number[];
};

export type ListMembership = {
  watched: boolean;
  watchlist: boolean;
  queue: boolean;
};

export type MediaType = 'movie' | 'series' | 'season';

/**
 * The unit rendered in every grid, search result, and list row.
 *
 * `key` is the identifier everywhere — see `@/lib/titleKey`. `tmdbId` is the
 * **series'** id on a season, and is never an identifier on its own: film and TV
 * ids are separate namespaces that collide.
 *
 * `runtimeMinutes` is null far more often than it was before series existed:
 * TMDB's `episode_run_time` is frequently empty. Treat null as ordinary.
 */
export type TitleCard = {
  key: string;
  mediaType: MediaType;
  tmdbId: number;
  seasonNumber: number | null; // season only
  parentKey: string | null; // season only — its series
  title: string;
  releaseYear: number | null;
  posterPath: string | null;
  tmdbVoteAverage: number | null; // 0–10, one decimal
  runtimeMinutes: number | null;
  episodeCount: number | null; // series and season
  seasonCount: number | null; // series only
  /**
   * Series only, and non-null only once at least one season has been watched —
   * never `0 / 5`, which is not progress. Nothing cascades between a series and
   * its seasons; this is the honest summary rendered in place of a rule the data
   * cannot support, so `5 / 5` does **not** mean the series itself is watched.
   */
  seasonProgress: { watched: number; total: number } | null;
  genreIds: number[];
  lists: ListMembership; // for the authenticated user
  myRating: number | null;
  /**
   * The provider ids **the viewer themself** subscribes to that carry this title
   * on subscription in their region. Empty means one of three things — no
   * services configured, availability not fetched, or on none of them — and the
   * card must not claim to know which, so an empty array renders nothing at all.
   *
   * Flatrate only. Rent and buy are deliberately absent: "I can watch this now"
   * and "I can pay to watch this now" are different claims.
   */
  availableOn: number[];
};

/** One row of a series' Seasons section, already decorated for the viewer. */
export type SeasonSummary = {
  key: string;
  seasonNumber: number;
  name: string;
  episodeCount: number | null;
  airDate: string | null;
  posterPath: string | null;
  lists: ListMembership;
  myRating: number | null;
};

export type TitleDetail = TitleCard & {
  backdropPath: string | null;
  overview: string | null;
  releaseDate: string | null;
  genres: Genre[];
  director: string | null; // films; null for series and seasons
  creators: string[]; // series; empty for films
  cast: { name: string; character: string | null; profilePath: string | null }[]; // max 12
  seasons: SeasonSummary[]; // series only; empty otherwise
  friendsWatched: { user: UserSummary; rating: number | null }[]; // FR-G4
  stale: boolean; // cached copy served while a refresh failed
};

// ------------------------------------------------------- streaming availability

/** One streaming service, as TMDB (via JustWatch) knows it. */
export type WatchProvider = {
  id: number;
  name: string;
  logoPath: string | null; // bare TMDB path, rendered like a poster
};

export type OfferKind = 'flatrate' | 'free' | 'ads' | 'rent' | 'buy';

/**
 * Where one title can be watched, in the viewer's region.
 *
 * `fetchedAt: null` means we have never looked — render "unknown", never an
 * empty section. It is a different answer from a timestamp with no `offers`
 * beside it, which means we looked and nobody carries it.
 */
export type TitleAvailability = {
  region: string;
  fetchedAt: string | null;
  link: string | null; // the JustWatch page, the one outbound link
  offers: { kind: OfferKind; providers: WatchProvider[] }[];
};

/** Body and response of `PUT /api/me/services` — the complete set. */
export type ServicesRequest = {
  region: string;
  providerIds: number[];
};

// -------------------------------------------------------------------- config

export type Attribution = {
  text: string;
  logoUrl: string;
  /** JustWatch's attribution, to be rendered wherever availability is. */
  availabilityText: string;
};

export type AppConfig = {
  imageBaseUrl: string;
  posterSizes: string[];
  backdropSizes: string[];
  profileSizes: string[];
  logoSizes: string[];
  attribution: Attribution;
};

// ---------------------------------------------------------------------- auth

export type RegisterRequest = {
  email: string;
  password: string;
  displayName: string;
};

export type LoginRequest = {
  email: string;
  password: string;
};

export type AvatarResponse = {
  avatarUrl: string | null;
};

export type ForgotPasswordRequest = {
  email: string;
};

export type ResetPasswordRequest = {
  email: string;
  token: string;
  password: string;
};

// ------------------------------------------------------------------ passkeys

/** One registered WebAuthn credential belonging to the signed-in user. */
export type PasskeySummary = {
  id: string; // base64url credential id — also the DELETE path segment
  name: string;
  createdAt: string; // ISO-8601 UTC
  isBackedUp: boolean; // synced to a provider, so it survives losing the device
};

/**
 * `optionsJson` is a JSON-encoded *string*, not an object — it is passed through
 * from Identity untouched and parsed here before it reaches `navigator
 * .credentials`. See API-CONTRACT.md, "Passkeys".
 */
export type PasskeyOptionsResponse = {
  optionsJson: string;
};

export type PasskeyCredentialRequest = {
  credentialJson: string;
};

export type RegisterPasskeyRequest = PasskeyCredentialRequest & {
  name?: string;
};

// --------------------------------------------------------------------- lists

export type ListName = 'watched' | 'watchlist' | 'queue';

export type ListEntry = {
  title: TitleCard;
  addedAt: string;
  position: number | null;
  watchedOn: string | null;
  /**
   * The rating of the person whose list this is. On your own lists it repeats
   * `title.myRating`; on `GET /api/friends/{userId}/lists/{list}` it is the
   * **friend's** rating, while `title.lists` and `title.myRating` still describe
   * the authenticated user. That split is what lets "my friend gave this 9, and
   * it's already on my watchlist" render in one pass.
   */
  rating: number | null;
};

export type ListResponse = {
  count: number;
  entries: ListEntry[];
};

export type ListSort = 'added' | 'title' | 'year' | 'runtime' | 'score' | 'rating';
export type SortDirection = 'asc' | 'desc';

/** The authoritative queue order — and the body of `409 queue_out_of_sync`. */
export type QueueOrder = {
  keys: string[];
};

export type QueueSortPreset = 'added' | 'title' | 'runtime' | 'score';

// ------------------------------------------------------------------- catalog

export type Paged<T> = {
  page: number;
  totalPages: number;
  totalResults: number;
  results: T[];
};

export type DiscoverFeed = 'popular' | 'top-rated' | 'now-playing';

/** `mediaTypes` says which side of TMDB's catalog a genre came from. */
export type Genre = { id: number; name: string; mediaTypes: MediaType[] };

// ------------------------------------------------------------------- ratings

/** `distribution` has length 10; index 0 = 1 half-star. */
export type RatingStats = {
  count: number;
  average: number | null;
  distribution: number[];
};

// ------------------------------------------------------------------- friends

export type TasteMatch = {
  score: number | null;
  sharedCount: number;
  /** `false` below the overlap threshold — never headline `score` then (FR-G6). */
  qualified: boolean;
};

export type Friend = {
  user: UserSummary;
  friendsSince: string;
  tasteMatch: TasteMatch;
};

export type FriendRequest = {
  id: string;
  user: UserSummary;
  sentAt: string;
};

export type FriendsResponse = {
  friends: Friend[];
  incoming: FriendRequest[];
  outgoing: FriendRequest[];
};

export type UserSearchResult = UserSummary & {
  relationship: 'none' | 'friends' | 'request_sent' | 'request_received';
};

/** How many watched titles carry a genre. Most-watched first, at most five. */
export type GenreAffinity = { id: number; name: string; count: number };

/**
 * The watched list's runtime, split into what is known and what is not.
 *
 * `minutes` sums **only** the runtimes TMDB gave us. A series with an empty
 * `episode_run_time` contributes nothing and is counted in `unknownTitles`
 * instead, which is what lets the profile say "at least 214h" rather than
 * passing an understatement off as a total.
 */
export type RuntimeOnRecord = {
  minutes: number;
  knownTitles: number;
  unknownTitles: number;
};

/**
 * The whole profile screen, for whoever is being looked at.
 *
 * Your profile and a friend's are the same page and therefore the same payload:
 * `GET /api/me/profile` and `GET /api/friends/{userId}/profile` differ only in
 * `isSelf` and `tasteMatch`, which is null when there is nobody to compare you
 * against.
 *
 * `recentActivity` is the owner's **own** events — exactly what `GET /api/feed`
 * excludes, because the feed is other people's news and a profile is this
 * person's.
 */
export type Profile = {
  user: UserSummary;
  isSelf: boolean;
  memberSince: string;
  stats: RatingStats;
  counts: { watched: number; watchlist: number; queue: number };
  favorites: TitleCard[];
  topGenres: GenreAffinity[];
  runtime: RuntimeOnRecord;
  friendCount: number;
  recentActivity: ActivityItem[];
  tasteMatch: TasteMatch | null;
};

/** The body of `PUT /api/me/favorites` — the complete showcase, in order. */
export type FavoritesRequest = {
  keys: string[];
};

// ---------------------------------------------------------------------- feed

export type ActivityItem = {
  id: string;
  user: UserSummary;
  /** Unchanged across media types — "watched" reads right for all three. */
  kind: 'rated' | 'watched' | 'added_watchlist' | 'added_queue';
  title: TitleCard;
  rating: number | null; // set when kind === 'rated'
  occurredAt: string;
};

export type FeedResponse = {
  items: ActivityItem[];
  nextCursor: string | null;
};

// --------------------------------------------------------------------- error

/** The body of every non-2xx response. */
export type ApiErrorBody = {
  code: string;
  message: string;
  errors?: Record<string, string[]>;
};
