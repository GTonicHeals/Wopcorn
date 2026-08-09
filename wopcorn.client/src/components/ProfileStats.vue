<script setup lang="ts">
import { computed } from 'vue';
import { RouterLink } from 'vue-router';

import { titleCount } from '@/lib/format';
import { averageStars, runtimeOnRecord } from '@/lib/profile';
import type { Profile } from '@/api/types';

/**
 * "On record" — the counters block, and the one place on the profile where the
 * numbers are the content rather than a caption.
 *
 * Deliberately not in the accent, on either profile. Gold means the signed-in
 * user's own state, but it means it by being *scarce*: six gold figures on your
 * own profile would drown the two places it still has to carry information — the
 * rating spread and the toggles on the cards below.
 *
 * On your own profile the three list counts are links, because there is a screen
 * behind each of them. On someone else's they are plain text: their lists are in
 * the tabs further down, not on your Lists screen.
 */
const props = defineProps<{ profile: Profile }>();

type Stat = {
  key: string;
  label: string;
  value: string;
  /** Second line, for a figure that needs a caveat spelled out. */
  note?: string | null;
  to?: string;
};

const stats = computed<Stat[]>(() => {
  const profile = props.profile;
  const runtime = runtimeOnRecord(profile.runtime);
  const average = averageStars(profile.stats);

  return [
    {
      key: 'watched',
      label: 'Watched',
      value: String(profile.counts.watched),
      to: profile.isSelf ? '/watched' : undefined
    },
    {
      key: 'runtime',
      label: 'On record',
      // "at least" is the honest reading whenever a watched title has no
      // runtime, which for a series is the ordinary case rather than the odd one.
      value: runtime ? `${runtime.approximate ? 'at least ' : ''}${runtime.value}` : '—',
      note: runtime?.note ?? null
    },
    {
      key: 'average',
      label: 'Average',
      value: average ? `${average} of 5` : '—',
      note: profile.stats.count > 0 ? `${titleCount(profile.stats.count)} rated` : null
    },
    {
      key: 'watchlist',
      label: 'Watchlist',
      value: String(profile.counts.watchlist),
      to: profile.isSelf ? '/watchlist' : undefined
    },
    {
      key: 'queue',
      label: 'Queued',
      value: String(profile.counts.queue),
      to: profile.isSelf ? '/queue' : undefined
    },
    {
      key: 'friends',
      label: 'Friends',
      value: String(profile.friendCount),
      to: profile.isSelf ? '/friends' : undefined
    }
  ];
});
</script>

<template>
  <dl class="on-record">
    <div v-for="stat in stats" :key="stat.key" class="on-record__cell">
      <dt class="on-record__label">{{ stat.label }}</dt>
      <dd class="on-record__value">
        <RouterLink v-if="stat.to" class="on-record__link" :to="stat.to">
          {{ stat.value }}
        </RouterLink>
        <template v-else>{{ stat.value }}</template>
        <span v-if="stat.note" class="on-record__note">{{ stat.note }}</span>
      </dd>
    </div>
  </dl>
</template>

<style scoped>
.on-record {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 1px;
  background: var(--border);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  overflow: hidden;
}

.on-record__cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: var(--space-3);
  background: var(--surface);
  min-width: 0;
}

.on-record__label {
  font-size: 11px;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--text-muted);
  font-weight: 600;
}

.on-record__value {
  font-size: var(--text-lg);
  font-weight: 600;
  line-height: 1.2;
  font-variant-numeric: tabular-nums;
  overflow-wrap: anywhere;
}

.on-record__link {
  color: inherit;
  text-decoration: none;
}

.on-record__link:hover {
  text-decoration: underline;
}

.on-record__note {
  display: block;
  font-size: var(--text-xs);
  font-weight: 400;
  color: var(--text-muted);
  line-height: 1.3;
}

@media (min-width: 560px) {
  .on-record {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}

/* In the sidebar it goes back to a single column of rows. */
@media (min-width: 900px) {
  .on-record {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
</style>
