<script setup lang="ts">
import { RouterLink } from 'vue-router';

import IconStar from '@/components/icons/IconStar.vue';
import ListToggles from '@/components/ListToggles.vue';
import PosterImage from '@/components/PosterImage.vue';
import StarDisplay from '@/components/StarDisplay.vue';
import TypeChip from '@/components/TypeChip.vue';
import { formatScore } from '@/lib/format';
import { isQuickViewClick } from '@/lib/quickView';
import { titlePath } from '@/lib/titleKey';
import { seasonProgressLabel, titleMetaShort } from '@/lib/titleMeta';
import type { MediaType, TitleCard } from '@/api/types';

/**
 * The dense alternative to `TitleGrid` — one row per title instead of a poster
 * wall, so a long watchlist can be scanned in one pass.
 *
 * Shaped like the queue's rows on purpose, with one deliberate difference: **no
 * numerals**. The queue is the only ordered list in the app and the only place
 * a position numeral means anything; a number here would imply an order this
 * list does not have. Ordering is whatever the sort sheet last asked the server
 * for, exactly as in the grid.
 *
 * The row is a grid rather than a `<table>`: the same cells have to reflow into
 * two lines on a phone, which a real table cannot do without losing its
 * semantics anyway. The header is `aria-hidden` and desktop-only — each row
 * already names its own parts for a screen reader.
 *
 * Type gets its own column on desktop, where there is room for a word. On a
 * phone the chip rides in the title cell instead, because a fourth column at
 * 320px would cost the title the space it needs.
 */
const props = withDefaults(
  defineProps<{
    titles: TitleCard[];
    /** Rows emit `select` instead of navigating — the lists' quick view. */
    quickView?: boolean;
  }>(),
  { quickView: false }
);

const emit = defineEmits<{ select: [key: string] }>();

const TYPE_LABELS: Record<MediaType, string> = {
  movie: 'Film',
  series: 'Series',
  season: 'Season'
};

/** Capture phase, so the click is claimed before vue-router follows the link. */
function onCaptureClick(event: MouseEvent, key: string): void {
  if (!props.quickView || !isQuickViewClick(event)) return;

  event.preventDefault();
  event.stopPropagation();
  emit('select', key);
}
</script>

<template>
  <div class="table">
    <div class="table__head" aria-hidden="true">
      <span class="table__col table__col--main">Title</span>
      <span class="table__col table__col--type">Type</span>
      <span class="table__col table__col--stars">Your rating</span>
      <span class="table__col table__col--toggles">Lists</span>
    </div>

    <ul class="table__rows">
      <li
        v-for="title in titles"
        :key="title.key"
        class="row"
        @click.capture="onCaptureClick($event, title.key)"
      >
        <RouterLink
          class="row__poster"
          :to="titlePath(title.key)"
          tabindex="-1"
          aria-hidden="true"
        >
          <PosterImage
            :path="title.posterPath"
            :title="title.title"
            :release-year="title.releaseYear"
            :width="46"
          />
        </RouterLink>

        <RouterLink class="row__main" :to="titlePath(title.key)">
          <b class="row__title">{{ title.title }}</b>
          <span class="row__meta">
            <!-- Mobile carries the type here; the desktop column hides it. -->
            <span class="row__chip"><TypeChip :media-type="title.mediaType" /></span>
            <span v-if="titleMetaShort(title)">{{ titleMetaShort(title) }}</span>
            <span v-if="formatScore(title.tmdbVoteAverage)" class="row__score">
              <span v-if="titleMetaShort(title)" aria-hidden="true">·</span>
              <IconStar filled />
              <span>{{ formatScore(title.tmdbVoteAverage) }}</span>
              <span class="sr-only">TMDB score</span>
            </span>
            <!-- The viewer's own progress, so it earns the accent here too. -->
            <span v-if="seasonProgressLabel(title.seasonProgress)" class="row__progress">
              {{ seasonProgressLabel(title.seasonProgress) }}
            </span>
          </span>
        </RouterLink>

        <span class="row__type">{{ TYPE_LABELS[title.mediaType] }}</span>

        <!-- Gold is the signed-in user's own rating and nothing else. -->
        <span class="row__stars">
          <StarDisplay v-if="title.myRating !== null" :value="title.myRating" :size="13" />
          <span v-else class="row__unrated" aria-hidden="true">—</span>
        </span>

        <span class="row__toggles">
          <ListToggles :title="title" />
        </span>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.table {
  padding: 0 var(--space-4) var(--space-8);
}

.table__head {
  display: none;
}

.table__rows {
  list-style: none;
  margin: 0;
  padding: 0;
}

/*
 * Phone: poster on the left spanning both lines, title and meta on the first,
 * the rating on the second, toggles pinned right. The type column does not
 * exist here — the chip in the meta line carries it.
 */
.row {
  display: grid;
  grid-template-columns: 46px minmax(0, 1fr) auto;
  grid-template-areas:
    'poster main  toggles'
    'poster stars toggles';
  align-items: center;
  column-gap: var(--space-3);
  padding: var(--space-2) 0;
  border-bottom: 1px solid var(--border);
}

.row:last-child {
  border-bottom: 0;
}

.row__poster {
  grid-area: poster;
  display: block;
  width: 46px;
}

.row__main {
  grid-area: main;
  min-width: 0;
  display: flex;
  flex-direction: column;
  text-decoration: none;
  color: inherit;
}

.row__title {
  font-size: 13.5px;
  font-weight: 600;
  line-height: 1.3;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.row__meta {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: var(--text-xs);
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

.row__score {
  display: inline-flex;
  align-items: center;
  gap: 3px;
}

.row__score :deep(svg) {
  width: 11px;
  height: 11px;
}

.row__progress {
  color: var(--accent);
  font-weight: 600;
}

.row__type {
  display: none;
}

.row__stars {
  grid-area: stars;
  min-width: 0;
  line-height: 0;
}

.row__unrated {
  font-size: var(--text-xs);
  color: var(--text-muted);
  line-height: 1;
}

.row__toggles {
  grid-area: toggles;
  /* Three 44px tap targets and their gaps; without this the buttons stretch. */
  width: 144px;
}

@media (min-width: 900px) {
  .table {
    padding: 0 var(--space-6) var(--space-8);
  }

  .table__head,
  .row {
    grid-template-columns: 46px minmax(0, 1fr) 5rem 7.5rem 144px;
    grid-template-areas: 'poster main type stars toggles';
  }

  .table__head {
    display: grid;
    column-gap: var(--space-3);
    padding: 0 0 var(--space-2);
    border-bottom: 1px solid var(--border);
    font-size: 11px;
    letter-spacing: 0.16em;
    text-transform: uppercase;
    color: var(--text-muted);
    font-weight: 600;
  }

  .table__col--main {
    grid-area: main;
  }

  .table__col--type {
    grid-area: type;
  }

  .table__col--stars {
    grid-area: stars;
  }

  .table__col--toggles {
    grid-area: toggles;
  }

  /* The column takes over from the chip; showing both would say it twice. */
  .row__chip {
    display: none;
  }

  .row__type {
    grid-area: type;
    display: block;
    font-size: var(--text-xs);
    color: var(--text-muted);
  }

  .row__title {
    font-size: var(--text-base);
  }
}
</style>
