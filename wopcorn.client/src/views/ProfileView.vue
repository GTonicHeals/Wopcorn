<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { RouterLink } from 'vue-router';

import BaseButton from '@/components/BaseButton.vue';
import EmptyState from '@/components/EmptyState.vue';
import ErrorState from '@/components/ErrorState.vue';
import FavoriteShowcase from '@/components/FavoriteShowcase.vue';
import FavoritesEditor from '@/components/FavoritesEditor.vue';
import GenreAffinityBars from '@/components/GenreAffinityBars.vue';
import IconLayers from '@/components/icons/IconLayers.vue';
import ProfileActivity from '@/components/ProfileActivity.vue';
import ProfileHero from '@/components/ProfileHero.vue';
import ProfileStats from '@/components/ProfileStats.vue';
import RatingHistogram from '@/components/RatingHistogram.vue';
import SpinnerBlock from '@/components/SpinnerBlock.vue';
import TasteMatch from '@/components/TasteMatch.vue';
import TitleGrid from '@/components/TitleGrid.vue';
import { ApiError, api, jsonBody } from '@/api/client';
import { useFriendLists } from '@/composables/useFriendLists';
import { formatFullDate, titleCount } from '@/lib/format';
import { useAuthStore } from '@/stores/auth';
import { useFriendsStore } from '@/stores/friends';
import { useTitlesStore } from '@/stores/titles';
import type { ListName, Profile, TitleCard } from '@/api/types';

/**
 * One profile screen, whoever it belongs to.
 *
 * Your profile and a friend's are the **same page** — same payload, same
 * sections, same order — because a profile that looks different to its owner is
 * a profile its owner cannot judge. `GET /api/me/profile` and
 * `GET /api/friends/{userId}/profile` return the same DTO; `isSelf` decides
 * which of the two things differ, and both are honest ones: only you can edit
 * the showcase, and only someone else has a taste match with you.
 *
 * Three rules this screen has to hold:
 *
 * - **Whose state is whose.** The server decorates every title for the
 *   *requester*, so the cards' toggles and `myRating` are yours wherever they
 *   appear. The owner's own rating arrives separately and renders on its own
 *   attributed row, and gold stays reserved for you: on a friend's page their
 *   histogram, genre bars and stars are all neutral.
 * - **Order without numerals.** The showcase is ordered, and position one is
 *   marked by the marquee behind the header rather than by a numeral — numerals
 *   belong to the queue.
 * - **A mid-session unfriend.** Every friend-scoped route re-checks the
 *   friendship on the request (NFR-4), so a `403` here is not a generic failure:
 *   it means the friendship ended, and the screen says exactly that.
 */
const props = defineProps<{
  /** Absent on `/profile`, which is always your own. */
  userId?: string;
}>();

const auth = useAuthStore();
const titles = useTitlesStore();
const friends = useFriendsStore();

/**
 * Your own id when the route carries none — and also when it carries yours, so
 * a link to `/u/{your id}` from anywhere lands on the same page as `/profile`
 * rather than on a 403 from a route that refuses to befriend you with yourself.
 */
const ownerId = computed(() => props.userId ?? auth.user?.id ?? '');
const isSelf = computed(() => ownerId.value !== '' && ownerId.value === auth.user?.id);

const profile = ref<Profile | null>(null);
const status = ref<'loading' | 'ready' | 'error'>('loading');
const error = ref<ApiError | null>(null);

// Only a friend's lists are browsable here; your own have a screen of their own,
// which the counters in "On record" link to.
const lists = useFriendLists(ownerId);

const tabs: { list: ListName; label: string }[] = [
  { list: 'watched', label: 'Watched' },
  { list: 'watchlist', label: 'Watchlist' },
  { list: 'queue', label: 'Queue' }
];

const activeList = ref<ListName>('watched');

/** The profile 403s the same way its lists do; either is the end of the friendship. */
const noLongerFriends = computed(
  () => lists.forbidden.value || (status.value === 'error' && error.value?.status === 403)
);

async function loadProfile(): Promise<void> {
  status.value = 'loading';
  error.value = null;

  try {
    const path = isSelf.value ? '/api/me/profile' : `/api/friends/${ownerId.value}/profile`;
    const loaded = await api<Profile>(path);

    // Favourites and activity carry full cards decorated for you, so putting them
    // in the shared store keeps a toggle pressed here in step with the same title
    // on every other screen.
    titles.upsertMany(loaded.favorites);
    titles.upsertMany(loaded.recentActivity.map((item) => item.title));

    profile.value = loaded;
    status.value = 'ready';

    if (!isSelf.value) await lists.ensure(activeList.value);
  } catch (failure) {
    profile.value = null;
    error.value = failure instanceof ApiError ? failure : null;
    status.value = 'error';
  }
}

watch(
  ownerId,
  () => {
    activeList.value = 'watched';
    profile.value = null;
    void loadProfile();
  },
  { immediate: true }
);

function selectList(list: ListName): void {
  activeList.value = list;
  void lists.ensure(list);
}

function retry(): void {
  lists.reset();
  void loadProfile();
}

// ------------------------------------------------------------------ showcase

const editing = ref(false);
const savingFavorites = ref(false);
/** Reported inside the sheet, which is the only place the write starts from. */
const favoritesError = ref('');

// Closing the sheet — including cancelling after a failed save — clears the
// message with it; the next attempt reports its own outcome.
watch(editing, (open) => {
  if (!open) favoritesError.value = '';
});

async function saveFavorites(keys: string[]): Promise<void> {
  if (savingFavorites.value || !profile.value) return;

  savingFavorites.value = true;
  favoritesError.value = '';

  try {
    // The response is the authoritative showcase, in the order the server stored
    // it — the same reconcile-from-the-response rule the queue follows.
    const saved = await api<TitleCard[]>('/api/me/favorites', {
      method: 'PUT',
      body: jsonBody({ keys })
    });

    titles.upsertMany(saved);
    profile.value = { ...profile.value, favorites: saved };
    editing.value = false;
  } catch (failure) {
    favoritesError.value =
      failure instanceof ApiError ? failure.message : 'Those favourites did not save.';
  } finally {
    savingFavorites.value = false;
  }
}

// --------------------------------------------------------------------- their list

const listState = computed(() => lists.state[activeList.value]);

const listTitles = computed(() =>
  listState.value.entries
    .map((entry) => titles.get(entry.key))
    .filter((title): title is TitleCard => title !== null)
);

/** Title key → the friend's rating, handed to the grid as attributed extras. */
const theirRatings = computed(() => {
  const map: Record<string, number | null> = {};
  for (const entry of listState.value.entries) map[entry.key] = entry.rating;
  return map;
});

/**
 * The profile payload does not carry `friendsSince` — only the `Friend` rows
 * from `GET /api/friends` do, and the shell has already fetched those for the
 * badge. Absent (a deep link answered before that call lands) the line simply
 * does not render; it is not worth a second request.
 */
const friendsSince = computed(() => {
  const row = friends.friends.find((friend) => friend.user.id === ownerId.value);
  return formatFullDate(row?.friendsSince ?? null);
});

const memberSince = computed(() => formatFullDate(profile.value?.memberSince ?? null));

/** The first favourite is the marquee — the page's light source. */
const marquee = computed(() => profile.value?.favorites[0] ?? null);

const tone = computed<'accent' | 'neutral'>(() => (isSelf.value ? 'accent' : 'neutral'));

const ownerName = computed(() => profile.value?.user.displayName ?? '');

function countFor(list: ListName): number {
  return profile.value?.counts[list] ?? 0;
}

const emptyCopy: Record<ListName, string> = {
  watched: 'has not marked anything watched yet',
  watchlist: 'has nothing on their watchlist yet',
  queue: 'has nothing queued up yet'
};
</script>

<template>
  <div>
    <!-- The friendship ended, here or in another tab. Not a generic failure. -->
    <ErrorState
      v-if="noLongerFriends"
      :error="error"
      :retryable="false"
      headline="You're no longer friends with this person"
      body="Their lists, ratings, and activity are no longer visible to you."
    >
      <template #action>
        <RouterLink to="/friends" class="profile__cta">Back to friends</RouterLink>
      </template>
    </ErrorState>

    <SpinnerBlock v-else-if="status === 'loading'" label="Loading profile" />

    <ErrorState v-else-if="status === 'error' || !profile" :error="error" @retry="retry" />

    <template v-else>
      <ProfileHero :user="profile.user" :marquee="marquee">
        <template #meta>
          <span v-if="memberSince">Member since {{ memberSince }}</span>
          <template v-if="!isSelf && friendsSince">
            <span aria-hidden="true"> · </span>
            <span>friends since {{ friendsSince }}</span>
          </template>
        </template>

        <template #actions>
          <template v-if="isSelf">
            <BaseButton variant="secondary" @click="editing = true">Edit favourites</BaseButton>
            <RouterLink to="/me" class="profile__cta">Settings</RouterLink>
          </template>
          <TasteMatch v-else :match="profile.tasteMatch" size="lg" />
        </template>
      </ProfileHero>

      <!--
        Full width, above the split: the showcase is the widest thing on the page
        because it is the thing the page is for. Six posters need the room, and
        the counters below read fine in a column.
      -->
      <section class="profile__showcase" aria-labelledby="profile-favorites">
        <div class="profile__legend-row">
          <h2 id="profile-favorites" class="profile__legend">Favourites</h2>
          <button
            v-if="isSelf && profile.favorites.length > 0"
            type="button"
            class="profile__edit"
            @click="editing = true"
          >
            Edit
          </button>
        </div>

        <FavoriteShowcase
          :titles="profile.favorites"
          :owner-name="ownerName"
          :is-self="isSelf"
        />

        <BaseButton
          v-if="isSelf && profile.favorites.length === 0"
          variant="secondary"
          @click="editing = true"
        >
          Pick your favourites
        </BaseButton>
      </section>

      <div class="profile__layout">
        <div class="profile__main">
          <div class="profile__pair">
            <section class="profile__section" aria-labelledby="profile-ratings">
              <h2 id="profile-ratings" class="profile__legend">Rating spread</h2>
              <RatingHistogram :stats="profile.stats" :tone="tone" />
            </section>

            <section class="profile__section" aria-labelledby="profile-genres">
              <h2 id="profile-genres" class="profile__legend">Taste in genres</h2>
              <GenreAffinityBars :genres="profile.topGenres" :tone="tone" />
            </section>
          </div>

          <section class="profile__section" aria-labelledby="profile-activity">
            <h2 id="profile-activity" class="profile__legend">Recent activity</h2>
            <ProfileActivity
              v-if="profile.recentActivity.length > 0"
              :items="profile.recentActivity"
              :tone="tone"
            />
            <p v-else class="profile__note">
              {{ isSelf ? 'Nothing yet — rate something and it lands here.' : 'Nothing recent.' }}
            </p>
          </section>

          <!--
            A friend's lists are only reachable here. Yours are not repeated:
            the counters in "On record" link to the Lists screen, which is the
            one place they are sorted, filtered and edited.
          -->
          <section v-if="!isSelf" class="profile__section" aria-labelledby="profile-lists">
            <h2 id="profile-lists" class="profile__legend">Their lists</h2>

            <!--
              A group, not a <nav>: these switch the panel below, they do not
              navigate. The lists screen uses RouterLinks and is a <nav> for that
              reason — same visual control, different semantics.
            -->
            <div class="segmented" role="group" aria-label="Choose a list">
              <button
                v-for="tab in tabs"
                :key="tab.list"
                type="button"
                class="segmented__item"
                :class="{ 'segmented__item--on': tab.list === activeList }"
                :aria-pressed="tab.list === activeList"
                @click="selectList(tab.list)"
              >
                {{ tab.label }}
              </button>
            </div>

            <p class="profile__count">{{ titleCount(countFor(activeList)) }}</p>

            <SpinnerBlock
              v-if="listState.status === 'loading' && listState.entries.length === 0"
              label="Loading list"
            />

            <ErrorState
              v-else-if="listState.status === 'error'"
              :error="listState.error"
              @retry="lists.load(activeList, true)"
            />

            <EmptyState
              v-else-if="listState.entries.length === 0"
              :headline="`${ownerName} ${emptyCopy[activeList]}`"
            >
              <template #icon><IconLayers /></template>
            </EmptyState>

            <!--
              The same grid as everywhere else. `ratings` is *their* rating,
              rendered on its own attributed row; the toggles on each card still
              act on your lists, because the server decorated every title for you.
            -->
            <TitleGrid
              v-else
              :titles="listTitles"
              :ratings="theirRatings"
              :rating-label="ownerName"
            />
          </section>
        </div>

        <aside class="profile__rail" aria-labelledby="profile-record">
          <h2 id="profile-record" class="profile__legend">On record</h2>
          <ProfileStats :profile="profile" />
        </aside>
      </div>

      <FavoritesEditor
        v-if="isSelf"
        v-model:open="editing"
        :favorites="profile.favorites"
        :saving="savingFavorites"
        :error="favoritesError"
        @save="saveFavorites"
      />
    </template>
  </div>
</template>

<style scoped>
.profile__showcase {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  align-items: flex-start;
  padding: 0 var(--space-4) var(--space-8);
}

.profile__layout {
  display: flex;
  flex-direction: column;
  gap: var(--space-8);
  padding: 0 var(--space-4) var(--space-8);
}

.profile__main {
  display: flex;
  flex-direction: column;
  gap: var(--space-8);
  min-width: 0;
}

.profile__section {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
  align-items: flex-start;
  min-width: 0;
}

.profile__legend-row {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  width: 100%;
}

.profile__showcase :deep(.showcase) {
  width: 100%;
}

.profile__legend {
  font-size: var(--text-xs);
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--text-muted);
  font-weight: 600;
}

.profile__edit {
  margin-left: auto;
  min-height: var(--tap-min);
  padding: 0 var(--space-2);
  border: 0;
  background: none;
  color: var(--text-muted);
  font: inherit;
  font-size: var(--text-xs);
  font-weight: 600;
}

.profile__edit:hover {
  color: var(--text);
}

.profile__pair {
  display: grid;
  gap: var(--space-8);
}

.profile__note,
.profile__count {
  font-size: var(--text-xs);
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

.profile__cta {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--tap-min);
  padding: 0 var(--space-4);
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  color: var(--text);
  font-size: var(--text-sm);
  font-weight: 600;
  text-decoration: none;
}

.profile__rail {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

/* The showcase and the grid both bring their own gutter; this layout has one. */
.profile__section :deep(.title-grid) {
  padding-left: 0;
  padding-right: 0;
  width: 100%;
}

/* Same segmented control as the lists screen — one pattern, not two. */
.segmented {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: var(--space-1);
  background: var(--surface-raised);
  border-radius: var(--radius-full);
  padding: 3px;
  width: 100%;
}

.segmented__item {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: var(--tap-min);
  border: 0;
  border-radius: var(--radius-full);
  background: none;
  color: var(--text-muted);
  font-size: var(--text-sm);
  font-weight: 600;
}

.segmented__item--on {
  background: var(--accent);
  color: var(--accent-ink);
}

@media (min-width: 700px) {
  .profile__pair {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (min-width: 900px) {
  .profile__showcase {
    padding-left: var(--space-6);
    padding-right: var(--space-6);
  }

  .profile__layout {
    display: grid;
    /* Main column, then the counters — the shape a profile page has had since
       long before this one. */
    grid-template-columns: minmax(0, 1fr) 260px;
    gap: var(--space-8);
    padding: 0 var(--space-6) var(--space-8);
  }

  .profile__rail {
    position: sticky;
    top: var(--space-6);
    align-self: start;
  }

  .segmented {
    max-width: 420px;
  }
}
</style>
