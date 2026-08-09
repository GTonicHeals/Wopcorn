<script setup lang="ts">
import { ref } from 'vue';

import { useConfigStore } from '@/stores/config';

/**
 * FR-B9. The attribution text comes from GET /api/config and is rendered
 * verbatim.
 *
 * The logo is a trademarked brand asset: it is only ever loaded from the URL the
 * server hands us, never approximated and never fetched from a third party. If
 * it fails to load — as it does today, because Wopcorn.Server/wwwroot has no
 * tmdb-logo.svg yet — the block degrades to the text alone rather than showing a
 * broken image.
 */
const config = useConfigStore();
const logoFailed = ref(false);
</script>

<template>
  <aside class="attribution">
    <img
      v-if="config.attributionLogoUrl && !logoFailed"
      class="attribution__logo"
      :src="config.attributionLogoUrl"
      alt="TMDB"
      width="80"
      height="32"
      @error="logoFailed = true"
    />
    <p class="attribution__text">{{ config.attributionText }}</p>
  </aside>
</template>

<style scoped>
.attribution {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  padding: var(--space-4) 0;
  border-top: 1px solid var(--border);
}

.attribution__logo {
  width: 80px;
  height: auto;
  flex: none;
}

.attribution__text {
  font-size: var(--text-xs);
  color: var(--text-muted);
  line-height: 1.5;
}
</style>
