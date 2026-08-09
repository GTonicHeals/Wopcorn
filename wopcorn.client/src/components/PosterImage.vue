<script setup lang="ts">
import { computed, ref, watch } from 'vue';

import { decadeGradient, placeholderLabel } from '@/lib/posters';
import { useConfigStore } from '@/stores/config';

/**
 * A poster box that is reserved before the image arrives (NFR-2) and never shows
 * a broken image (fe-06, task 2). With no `path` — or with one that fails to
 * load — it renders the decade duotone with the title set small in the display
 * face, keeping the same ratio, radius, and edge as real artwork.
 */
const props = withDefaults(
  defineProps<{
    path: string | null;
    title: string;
    releaseYear: number | null;
    /** Rendered width in CSS px; the config store picks the TMDB size from it. */
    width?: number;
  }>(),
  { width: 138 }
);

const config = useConfigStore();
const broken = ref(false);

watch(
  () => props.path,
  () => {
    broken.value = false;
  }
);

const src = computed(() => config.posterUrl(props.path, props.width));
const showPlaceholder = computed(() => src.value === null || broken.value);
const label = computed(() => placeholderLabel(props.title));
const gradient = computed(() => decadeGradient(props.releaseYear));
</script>

<template>
  <div class="poster" :style="showPlaceholder ? { backgroundImage: gradient } : undefined">
    <img
      v-if="!showPlaceholder && src"
      class="poster__img"
      :src="src"
      alt=""
      loading="lazy"
      decoding="async"
      @error="broken = true"
    />
    <span v-else class="poster__label" aria-hidden="true">{{ label }}</span>
  </div>
</template>

<style scoped>
.poster {
  position: relative;
  width: 100%;
  /* Reserved before the image loads so the grid never reflows (NFR-2). */
  aspect-ratio: 2 / 3;
  border-radius: var(--radius-md);
  /* Contains bright posters against the surface. */
  border: 1px solid var(--poster-edge);
  overflow: hidden;
  background-color: var(--surface-raised);
}

.poster__img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.poster__label {
  position: absolute;
  left: 8px;
  bottom: 8px;
  right: 8px;
  font-family: var(--font-display);
  font-size: 11px;
  line-height: 1.25;
  text-transform: uppercase;
  letter-spacing: 0.24em;
  color: rgba(255, 255, 255, 0.5);
}
</style>
