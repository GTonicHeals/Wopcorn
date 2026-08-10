<script setup lang="ts">
import { computed } from 'vue';

import { useConfigStore } from '@/stores/config';
import type { WatchProvider } from '@/api/types';

/**
 * One service's brand mark.
 *
 * These logos are the only thing in the interface not drawn from `tokens.css` —
 * they are third-party marks in their own colours, which is exactly why they are
 * kept small, square and boxed. A Netflix red sitting loose beside the gold is
 * two competing signals in a design that has spent considerable effort making
 * gold mean one thing.
 *
 * With no logo path the initial stands in, so a provider is never a blank square.
 */
const props = withDefaults(defineProps<{ provider: WatchProvider; size?: number }>(), {
  size: 20
});

const config = useConfigStore();

const src = computed(() => config.logoUrl(props.provider.logoPath, props.size));
const initial = computed(() => props.provider.name.trim().charAt(0).toUpperCase() || '?');
</script>

<template>
  <span
    class="provider-logo"
    :style="{ width: `${size}px`, height: `${size}px` }"
    :title="provider.name"
  >
    <img v-if="src" :src="src" :alt="provider.name" loading="lazy" decoding="async" />
    <template v-else>
      <span aria-hidden="true">{{ initial }}</span>
      <span class="sr-only">{{ provider.name }}</span>
    </template>
  </span>
</template>

<style scoped>
.provider-logo {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: none;
  overflow: hidden;
  border-radius: var(--radius-sm);
  /* Neutral edge, never the accent: whose logo this is is not user state. */
  border: 1px solid var(--border);
  background: var(--surface-raised);
  font-size: 10px;
  font-weight: 700;
  color: var(--text-muted);
  line-height: 1;
}

.provider-logo img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}
</style>
