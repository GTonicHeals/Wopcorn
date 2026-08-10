<script setup lang="ts">
import { computed } from 'vue';

import ProviderLogo from '@/components/ProviderLogo.vue';
import { namedProviders } from '@/lib/services';
import { useAuthStore } from '@/stores/auth';
import { useConfigStore } from '@/stores/config';

/**
 * "You can watch this tonight" on a card: up to three of the viewer's own
 * services, then `+N`.
 *
 * **An empty `availableOn` renders nothing at all** — no skeleton, no "not
 * available". The array cannot distinguish "we have not looked" from "on none of
 * your services", so it must not claim either.
 *
 * The row is neutral-bordered and never adjacent to the accent: these are brand
 * marks in their own colours, and gold in this app means one thing.
 */
const props = withDefaults(defineProps<{ providerIds: number[]; max?: number }>(), { max: 3 });

const auth = useAuthStore();
const config = useConfigStore();

const known = computed(() =>
  namedProviders(config.providersByRegion.get(auth.region ?? '') ?? [], props.providerIds)
);

const shown = computed(() => known.value.slice(0, props.max));
const overflow = computed(() => known.value.length - shown.value.length);

const label = computed(() =>
  known.value.length === 0 ? '' : `On ${known.value.map((p) => p.name).join(', ')}`
);
</script>

<template>
  <p v-if="shown.length > 0" class="providers" :aria-label="label">
    <ProviderLogo
      v-for="provider in shown"
      :key="provider.id"
      :provider="provider"
      :size="20"
    />
    <span v-if="overflow > 0" class="providers__more" aria-hidden="true">+{{ overflow }}</span>
  </p>
</template>

<style scoped>
.providers {
  display: flex;
  align-items: center;
  gap: 4px;
  min-width: 0;
}

.providers__more {
  font-size: 10px;
  font-weight: 600;
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}
</style>
