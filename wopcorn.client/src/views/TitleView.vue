<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { RouterLink, useRouter } from 'vue-router';

import ErrorState from '@/components/ErrorState.vue';
import IconChevronLeft from '@/components/icons/IconChevronLeft.vue';
import IconStar from '@/components/icons/IconStar.vue';
import ListToggles from '@/components/ListToggles.vue';
import PosterImage from '@/components/PosterImage.vue';
import SpinnerBlock from '@/components/SpinnerBlock.vue';
import StarDisplay from '@/components/StarDisplay.vue';
import StarRating from '@/components/StarRating.vue';
import SuggestBox from '@/components/SuggestBox.vue';
import SuggestedBy from '@/components/SuggestedBy.vue';
import SuggestionBanner from '@/components/SuggestionBanner.vue';
import TmdbAttribution from '@/components/TmdbAttribution.vue';
import TypeChip from '@/components/TypeChip.vue';
import UserAvatar from '@/components/UserAvatar.vue';
import WatchedNote from '@/components/WatchedNote.vue';
import WhereToWatch from '@/components/WhereToWatch.vue';
import { ApiError } from '@/api/client';
import { episodeCount, formatScore } from '@/lib/format';
import { titlePath } from '@/lib/titleKey';
import { seasonProgressLabel, titleMeta } from '@/lib/titleMeta';
import { useConfigStore } from '@/stores/config';
import { useListsStore } from '@/stores/lists';
import { isDetail, useTitlesStore } from '@/stores/titles';
import type { TitleCard, TitleDetail } from '@/api/types';

/**
 * FR-B3, for all three media types. Backdrop, then everything that can be acted
 * on — the rating and the three list toggles sit directly under the meta row so
 * they are reachable without scrolling on a 320×568 screen.
 *
 * A series grows a **Seasons** section from the `seasons[]` array already in the
 * detail response, each row with its own toggles and stars. Nothing cascades:
 * marking every season watched leaves the series entry alone and the progress
 * line simply reads `5 / 5`. That is the honest rendering of independent
 * entries, and the alternative would need a rule for what happens when TMDB adds
 * a season later.
 */
const props = defineProps<{ titleKey: string }>();

const router = useRouter();
const config = useConfigStore();
const titles = useTitlesStore();
const lists = useListsStore();

const status = ref<'loading' | 'ready' | 'error'>('loading');
const error = ref<ApiError | null>(null);
const refreshing = ref(false);

const title = computed(() => titles.get(props.titleKey));
const detail = computed<TitleDetail | null>(() => (isDetail(title.value) ? title.value : null));

async function load(force = false): Promise<void> {
  if (force) refreshing.value = true;
  else status.value = detail.value ? 'ready' : 'loading';

  error.value = null;

  try {
    await titles.loadDetail(props.titleKey, force);
    status.value = 'ready';
  } catch (failure) {
    error.value = failure instanceof ApiError ? failure : null;
    // A cached copy already on screen beats an error region over the top of it.
    status.value = detail.value ? 'ready' : 'error';
  } finally {
    refreshing.value = false;
  }
}

watch(() => props.titleKey, () => void load(), { immediate: true });

const backdrop = computed(() => config.backdropUrl(detail.value?.backdropPath ?? null, 780));

const metaText = computed(() => (detail.value ? titleMeta(detail.value) : ''));
const score = computed(() => formatScore(detail.value?.tmdbVoteAverage));
const progress = computed(() => seasonProgressLabel(detail.value?.seasonProgress));

/** A season says which series it belongs to, and links back to it. */
const parent = computed(() => {
  const current = detail.value;
  if (!current || current.mediaType !== 'season' || !current.parentKey) return null;
  return { key: current.parentKey, title: titles.get(current.parentKey)?.title ?? 'the series' };
});

/**
 * The Seasons rows read their state from the shared map rather than from
 * `detail.seasons` — the store holds one entry per title, so a toggle pressed
 * here and the same season's own screen stay in step without a refetch.
 */
const seasonRows = computed<TitleCard[]>(() =>
  (detail.value?.seasons ?? [])
    .map((season) => titles.get(season.key))
    .filter((row): row is TitleCard => row !== null)
);

function onRate(value: number): void {
  void lists.setRating(props.titleKey, value);
}

function onClearRating(): void {
  void lists.clearRating(props.titleKey);
}

function onRateSeason(key: string, value: number): void {
  void lists.setRating(key, value);
}

function onClearSeasonRating(key: string): void {
  void lists.clearRating(key);
}
</script>

<template>
  <div class="title">
    <SpinnerBlock v-if="status === 'loading'" label="Loading title" />

    <ErrorState v-else-if="status === 'error' || !detail" :error="error" @retry="load(false)" />

    <template v-else>
      <header class="hero">
        <div
          class="hero__art"
          :style="backdrop ? { backgroundImage: `url(${backdrop})` } : undefined"
        >
          <span class="hero__scrim" aria-hidden="true" />
          <button type="button" class="hero__back" aria-label="Back" @click="router.back()">
            <IconChevronLeft />
          </button>
        </div>
        <!--
          The title band is solid `--bg`, not a partly transparent scrim: over a
          bright backdrop a gradient cannot be guaranteed to clear 4.5:1, and
          NFR-9 is not a probabilistic requirement. The gradient above it still
          dissolves the image into the band, so the composition is unchanged.
        -->
        <div class="hero__band">
          <!-- A season names its series above its own name, so "Season 2" alone
               is never the whole heading. -->
          <RouterLink v-if="parent" class="hero__parent" :to="titlePath(parent.key)">
            {{ parent.title }}
          </RouterLink>
          <h1 class="hero__title">{{ detail.title }}</h1>
        </div>
      </header>

      <div class="title__body">
        <p class="meta">
          <TypeChip :media-type="detail.mediaType" />
          <span v-if="metaText">{{ metaText }}</span>
          <span v-for="genre in detail.genres" :key="genre.id" class="chip">{{ genre.name }}</span>
        </p>

        <!-- Upstream score and friends' opinions read as one unit. -->
        <section class="opinions" aria-label="Scores">
          <p v-if="score" class="score">
            <IconStar filled />
            <b>{{ score }}</b>
            <span class="score__src">TMDB</span>
          </p>

          <ul v-if="detail.friendsWatched.length > 0" class="friends">
            <li v-for="entry in detail.friendsWatched" :key="entry.user.id" class="friends__row">
              <UserAvatar :user="entry.user" :size="26" />
              <span class="friends__name">{{ entry.user.displayName }}</span>
              <StarDisplay
                v-if="entry.rating !== null"
                :value="entry.rating"
                :size="13"
                tone="neutral"
              />
              <span v-else class="friends__unrated">watched</span>
              <!--
                Their note, on its own line under the rating it belongs to. Full
                text rather than a bubble: this is the screen the bubble on the
                card was pointing at, so there is nowhere further to send anyone.
              -->
              <p v-if="entry.comment" class="friends__note">{{ entry.comment }}</p>
            </li>
          </ul>
        </section>

        <!--
          A friend's unanswered recommendation, in full here rather than the
          compact card strip: this is the screen with room to say what it is for
          and who it is from.
        -->
        <SuggestionBanner
          v-if="detail.suggestion"
          class="suggestion-row"
          :badge="detail.suggestion"
          :title-key="detail.key"
        />

        <StarRating
          :model-value="detail.myRating"
          :film-title="detail.title"
          @update:model-value="onRate"
          @clear="onClearRating"
        />

        <ListToggles :title="detail" show-labels />

        <div class="title__share">
          <SuggestBox :title-key="detail.key" :title-name="detail.title" />
        </div>

        <!-- Not an error: a saved copy is being shown on purpose (FR-B8). -->
        <p v-if="detail.stale" class="stale">
          <span>Showing saved details; TMDB is unreachable.</span>
          <button type="button" class="stale__retry" :disabled="refreshing" @click="load(true)">
            {{ refreshing ? 'Retrying…' : 'Try again' }}
          </button>
        </p>

        <!--
          High on the page, because "can I watch this tonight" is the question
          the title screen is opened to answer at least as often as "what is it
          about". A season shows its series' answer — TMDB has no season-level
          providers, and where the show is carried is the honest reply.
        -->
        <WhereToWatch class="section" :title-key="detail.key" />

        <section v-if="detail.overview" class="section" aria-labelledby="title-synopsis">
          <h2 id="title-synopsis" class="section__title">Synopsis</h2>
          <p class="synopsis">{{ detail.overview }}</p>
        </section>

        <!-- ------------------------------------------------------- seasons -->

        <section v-if="seasonRows.length > 0" class="section" aria-labelledby="title-seasons">
          <h2 id="title-seasons" class="section__title">
            Seasons
            <!-- The viewer's own progress, so it earns the accent. -->
            <span v-if="progress" class="section__progress">{{ progress }} watched</span>
          </h2>

          <ul class="seasons">
            <li v-for="season in seasonRows" :key="season.key" class="season">
              <RouterLink class="season__link" :to="titlePath(season.key)">
                <span class="season__poster">
                  <PosterImage
                    :path="season.posterPath"
                    :title="season.title"
                    :release-year="season.releaseYear"
                    :width="46"
                  />
                </span>
                <span class="season__text">
                  <b class="season__name">{{ season.title }}</b>
                  <span class="season__meta">
                    <span v-if="season.episodeCount">{{ episodeCount(season.episodeCount) }}</span>
                    <span v-if="season.releaseYear">
                      <span v-if="season.episodeCount" aria-hidden="true">·</span>
                      {{ season.releaseYear }}
                    </span>
                  </span>
                </span>
              </RouterLink>

              <div class="season__controls">
                <StarRating
                  :model-value="season.myRating"
                  :film-title="season.title"
                  @update:model-value="onRateSeason(season.key, $event)"
                  @clear="onClearSeasonRating(season.key)"
                />
                <ListToggles :title="season" />
              </div>
            </li>
          </ul>
        </section>

        <section v-if="detail.director" class="section" aria-labelledby="title-director">
          <h2 id="title-director" class="section__title">Director</h2>
          <p class="people">{{ detail.director }}</p>
        </section>

        <section v-if="detail.creators.length > 0" class="section" aria-labelledby="title-creators">
          <h2 id="title-creators" class="section__title">
            {{ detail.creators.length === 1 ? 'Creator' : 'Creators' }}
          </h2>
          <p class="people">{{ detail.creators.join(', ') }}</p>
        </section>

        <!--
          The viewer's own note, and then the notes friends attached to their
          suggestions. Both sit below the synopsis and the seasons: they are
          about the title rather than part of it, and someone arriving to find
          out what a film *is* should reach that first.
        -->
        <WatchedNote
          v-if="detail.lists.watched"
          :title-key="detail.key"
          :title-name="detail.title"
          :note="detail.myComment"
        />

        <SuggestedBy :notes="detail.suggestedBy" />

        <section v-if="detail.cast.length > 0" class="section" aria-labelledby="title-cast">
          <h2 id="title-cast" class="section__title">Cast</h2>
          <ul class="cast">
            <li
              v-for="member in detail.cast"
              :key="`${member.name}-${member.character}`"
              class="cast__item"
            >
              <img
                v-if="config.profileUrl(member.profilePath, 64)"
                class="cast__photo"
                :src="config.profileUrl(member.profilePath, 64) ?? undefined"
                alt=""
                loading="lazy"
                decoding="async"
              />
              <span v-else class="cast__photo cast__photo--blank" aria-hidden="true" />
              <span class="cast__name">{{ member.name }}</span>
              <span v-if="member.character" class="cast__role">{{ member.character }}</span>
            </li>
          </ul>
        </section>

        <TmdbAttribution />
      </div>
    </template>
  </div>
</template>

<style scoped>
/* ------------------------------------------------------------------- hero */

.hero {
  position: relative;
}

.hero__art {
  position: relative;
  aspect-ratio: 16 / 9;
  background-color: var(--surface-raised);
  background-size: cover;
  background-position: center;
}

.hero__scrim {
  position: absolute;
  inset: 40% 0 0;
  background: var(--scrim);
}

.hero__back {
  position: absolute;
  top: var(--space-2);
  left: var(--space-2);
  width: var(--tap-min);
  height: var(--tap-min);
  display: flex;
  align-items: center;
  justify-content: center;
  border: 0;
  border-radius: var(--radius-full);
  background: var(--bg);
  color: var(--text);
}

.hero__band {
  background: var(--bg);
  padding: 0 var(--space-4) var(--space-2);
}

.hero__parent {
  display: inline-block;
  font-size: var(--text-xs);
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--text-muted);
  font-weight: 600;
  text-decoration: none;
  margin-bottom: 2px;
}

.hero__title {
  font-family: var(--font-display);
  font-size: var(--text-2xl);
  font-weight: 500;
  line-height: 1.05;
  text-wrap: balance;
}

/* ------------------------------------------------------------------- body */

.title__body {
  padding: var(--space-3) var(--space-4) var(--space-8);
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
}

.meta {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
  font-size: var(--text-xs);
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

.chip {
  border: 1px solid var(--border);
  border-radius: var(--radius-full);
  padding: 2px 10px;
  font-size: 11.5px;
}

.opinions {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.score {
  display: flex;
  align-items: center;
  gap: var(--space-1);
  font-size: var(--text-sm);
  color: var(--text-muted);
}

.score :deep(svg) {
  width: 14px;
  height: 14px;
}

.score b {
  color: var(--text);
  font-variant-numeric: tabular-nums;
}

.score__src {
  font-size: 11.5px;
  letter-spacing: 0.08em;
}

.friends {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}

.friends__row {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
  font-size: var(--text-sm);
}

/*
 * Full width on its own line, indented past the avatar so it reads as belonging
 * to the name above it rather than to the row below.
 */
.friends__note {
  flex-basis: 100%;
  margin: 2px 0 var(--space-1) calc(26px + var(--space-2));
  padding-left: var(--space-2);
  border-left: 2px solid var(--border);
  font-size: var(--text-sm);
  line-height: 1.5;
  color: var(--text-muted);
  white-space: pre-wrap;
}

.friends__name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.friends__unrated {
  font-size: var(--text-xs);
  color: var(--text-muted);
}

.suggestion-row {
  margin-top: var(--space-4);
}

.title__share {
  margin-top: var(--space-3);
}

.stale {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  flex-wrap: wrap;
  font-size: var(--text-xs);
  color: var(--text-muted);
  border-left: 2px solid var(--border);
  padding-left: var(--space-3);
}

.stale__retry {
  min-height: var(--tap-min);
  border: 0;
  background: none;
  color: var(--text);
  font-size: var(--text-xs);
  font-weight: 600;
  text-decoration: underline;
  padding: 0;
}

.section {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.section__title {
  display: flex;
  align-items: baseline;
  gap: var(--space-2);
  font-size: var(--text-xs);
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--text-muted);
  font-weight: 600;
}

.section__progress {
  color: var(--accent);
  letter-spacing: 0.04em;
  font-variant-numeric: tabular-nums;
}

.synopsis {
  font-size: var(--text-sm);
  color: var(--text-muted);
  line-height: 1.6;
}

.people {
  font-size: var(--text-sm);
}

/* ---------------------------------------------------------------- seasons */

.seasons {
  display: flex;
  flex-direction: column;
}

.season {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  padding: var(--space-3) 0;
  border-bottom: 1px solid var(--border);
}

.season:last-child {
  border-bottom: 0;
}

.season__link {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-width: 0;
  text-decoration: none;
  color: inherit;
}

.season__poster {
  flex: none;
  width: 46px;
  display: block;
}

.season__text {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}

.season__name {
  font-size: var(--text-base);
  font-weight: 600;
  line-height: 1.3;
}

.season__meta {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: var(--text-xs);
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

/*
 * Stars above the toggles rather than beside them: three 44px buttons plus a
 * star control does not fit a 320px row, and shrinking either would break the
 * FR-H4 floor.
 */
.season__controls {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

/* ------------------------------------------------------------------- cast */

.cast {
  display: flex;
  gap: var(--space-3);
  overflow-x: auto;
  scrollbar-width: none;
  -webkit-overflow-scrolling: touch;
  margin: 0 calc(var(--space-4) * -1);
  padding: 0 var(--space-4);
}

.cast::-webkit-scrollbar {
  display: none;
}

.cast__item {
  flex: none;
  width: 84px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.cast__photo {
  width: 84px;
  height: 126px;
  object-fit: cover;
  border-radius: var(--radius-sm);
  border: 1px solid var(--poster-edge);
  background: var(--surface-raised);
}

.cast__photo--blank {
  display: block;
}

.cast__name {
  font-size: var(--text-xs);
  font-weight: 600;
  line-height: 1.3;
}

.cast__role {
  font-size: 11px;
  color: var(--text-muted);
  line-height: 1.3;
}

@media (min-width: 900px) {
  .hero__band {
    padding: 0 var(--space-6) var(--space-2);
  }

  .title__body {
    padding: var(--space-3) var(--space-6) var(--space-8);
    max-width: 720px;
  }

  .season {
    flex-direction: row;
    align-items: center;
    justify-content: space-between;
  }

  .season__link {
    flex: 1;
  }

  /*
   * A fixed column so the star rows line up down the section. The control is
   * built to be dragged along, so it keeps a real width rather than shrinking
   * to its stars.
   */
  .season__controls {
    flex: none;
    width: 240px;
  }

  .cast {
    margin: 0 calc(var(--space-6) * -1);
    padding: 0 var(--space-6);
  }
}
</style>
