<script setup lang="ts">
import { computed, ref, watch } from 'vue';

import ProviderLogo from '@/components/ProviderLogo.vue';
import { regionName } from '@/lib/regions';
import { useAuthStore } from '@/stores/auth';
import { useConfigStore } from '@/stores/config';
import { useTitlesStore } from '@/stores/titles';
import type { OfferKind, WatchProvider } from '@/api/types';

/**
 * Where a title can be watched, on its own screen.
 *
 * Flatrate first and at full size, then rent and buy behind a disclosure: the
 * question someone opens this to answer is "is it included", not "what would it
 * cost". Fetched **after** the detail renders (`D-3`) — a 24-hour answer must not
 * be tied to the detail's seven-day TTL, and a providers failure must not delay
 * the page.
 *
 * The region is labelled on the block, so a wrong answer reads as a wrong region
 * rather than as a broken app.
 */
const props = defineProps<{ titleKey: string }>();

const auth = useAuthStore();
const config = useConfigStore();
const titles = useTitlesStore();

const status = ref<'idle' | 'loading' | 'ready' | 'error'>('idle');
const showPaid = ref(false);

const availability = computed(() => titles.availability(props.titleKey));

/** Included with a subscription, in the order the server sent them. */
const INCLUDED: OfferKind[] = ['flatrate', 'free', 'ads'];
const PAID: OfferKind[] = ['rent', 'buy'];

const KIND_LABELS: Record<OfferKind, string> = {
  flatrate: 'Included with',
  free: 'Free on',
  ads: 'Free with ads on',
  rent: 'Rent from',
  buy: 'Buy from'
};

function groups(kinds: OfferKind[]): { kind: OfferKind; providers: WatchProvider[] }[] {
  return (availability.value?.offers ?? []).filter((group) => kinds.includes(group.kind));
}

const included = computed(() => groups(INCLUDED));
const paid = computed(() => groups(PAID));

/** Never looked. Distinct from having looked and found nothing. */
const unknown = computed(() => status.value === 'error' || availability.value?.fetchedAt === null);

const regionLabel = computed(() =>
  availability.value ? regionName(availability.value.region) : ''
);

async function load(force = false): Promise<void> {
  if (!auth.region) return;

  status.value = 'loading';
  try {
    await titles.loadAvailability(props.titleKey, force);
    status.value = 'ready';
  } catch {
    // Never an error region over the page — availability decorates it.
    status.value = 'error';
  }
}

watch(() => props.titleKey, () => void load(), { immediate: true });

// Setting a region in another tab (or on first setup) should fill this in.
watch(() => auth.region, () => void load(true));
</script>

<template>
  <!--
    Nothing renders at all until the viewer has said where they watch. The block
    appears when it can answer; until then the title screen looks as it did.
  -->
  <section v-if="auth.region" class="watch" aria-labelledby="title-watch">
    <h2 id="title-watch" class="watch__title">
      Where to watch
      <span v-if="regionLabel" class="watch__region">{{ regionLabel }}</span>
    </h2>

    <p v-if="status === 'loading' && !availability" class="watch__note">Checking…</p>

    <template v-else-if="unknown">
      <p class="watch__note">
        Availability unknown.
        <button type="button" class="watch__retry" @click="load(true)">Try again</button>
      </p>
    </template>

    <template v-else-if="included.length > 0 || paid.length > 0">
      <div v-for="group in included" :key="group.kind" class="watch__group">
        <p class="watch__kind">{{ KIND_LABELS[group.kind] }}</p>
        <ul class="watch__providers">
          <li v-for="provider in group.providers" :key="provider.id" class="watch__provider">
            <ProviderLogo :provider="provider" :size="34" />
            <span>{{ provider.name }}</span>
          </li>
        </ul>
      </div>

      <template v-if="paid.length > 0">
        <button
          type="button"
          class="watch__disclosure"
          :aria-expanded="showPaid"
          @click="showPaid = !showPaid"
        >
          {{ showPaid ? 'Hide' : 'Rent or buy' }}
        </button>

        <div v-if="showPaid" class="watch__paid">
          <p v-for="group in paid" :key="group.kind" class="watch__paid-row">
            <span class="watch__kind">{{ KIND_LABELS[group.kind] }}</span>
            <span>{{ group.providers.map((p) => p.name).join(', ') }}</span>
          </p>
        </div>
      </template>
    </template>

    <p v-else class="watch__note">No streaming service carries this here.</p>

    <!-- The attribution the availability data comes with, and its one link. -->
    <p class="watch__credit">
      <a
        v-if="availability?.link"
        class="watch__link"
        :href="availability.link"
        target="_blank"
        rel="noopener noreferrer"
      >
        {{ config.availabilityAttribution }}
      </a>
      <span v-else>{{ config.availabilityAttribution }}</span>
    </p>
  </section>
</template>

<style scoped>
.watch {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.watch__title {
  display: flex;
  align-items: baseline;
  gap: var(--space-2);
  font-size: var(--text-xs);
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--text-muted);
  font-weight: 600;
}

/* Neutral: which region you are in is a fact about the answer, not your state. */
.watch__region {
  letter-spacing: 0.04em;
  text-transform: none;
  font-weight: 500;
}

.watch__group {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}

.watch__kind {
  font-size: var(--text-xs);
  color: var(--text-muted);
}

.watch__providers {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2) var(--space-3);
}

.watch__provider {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  font-size: var(--text-sm);
  font-weight: 600;
}

.watch__disclosure,
.watch__retry {
  align-self: flex-start;
  min-height: var(--tap-min);
  border: 0;
  background: none;
  padding: 0;
  color: var(--text);
  font-size: var(--text-xs);
  font-weight: 600;
  text-decoration: underline;
}

.watch__paid {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.watch__paid-row {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
  font-size: var(--text-sm);
}

.watch__note {
  font-size: var(--text-sm);
  color: var(--text-muted);
}

.watch__credit {
  font-size: 11px;
  color: var(--text-muted);
}

.watch__link {
  color: inherit;
}
</style>
