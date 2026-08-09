import { computed, ref } from 'vue';
import { defineStore } from 'pinia';

import { api } from '@/api/client';
import type { AppConfig } from '@/api/types';

/** Fallback so an unreachable /api/config never leaves the UI with dead image URLs. */
const FALLBACK: AppConfig = {
  imageBaseUrl: 'https://image.tmdb.org/t/p/',
  posterSizes: ['w92', 'w154', 'w185', 'w342', 'w500', 'w780', 'original'],
  backdropSizes: ['w300', 'w780', 'w1280', 'original'],
  profileSizes: ['w45', 'w185', 'h632', 'original'],
  attribution: {
    text: 'This product uses the TMDB API but is not endorsed or certified by TMDB.',
    logoUrl: ''
  }
};

/**
 * Ranks a TMDB size token by its long edge. `w342` is 342px wide; the
 * height-keyed `h632` tokens (profiles only) are ranked by height; `original`
 * and anything unrecognised sort last so they are only ever the fallback.
 */
function widthOf(size: string): number {
  const match = /^[wh](\d+)$/.exec(size);
  return match?.[1] ? Number(match[1]) : Number.POSITIVE_INFINITY;
}

function devicePixelRatio(): number {
  const ratio = typeof window === 'undefined' ? 1 : window.devicePixelRatio;
  return Number.isFinite(ratio) && ratio > 0 ? ratio : 1;
}

/**
 * The smallest declared size that still covers `targetWidthPx` at the current
 * device pixel ratio — the whole point of FR-H6 is not shipping a 780px poster
 * to a 96px slot.
 */
export function pickSize(sizes: string[], targetWidthPx: number): string {
  const needed = targetWidthPx * devicePixelRatio();
  const sorted = [...sizes].sort((a, b) => widthOf(a) - widthOf(b));

  for (const size of sorted) {
    if (widthOf(size) >= needed) return size;
  }

  return sorted[sorted.length - 1] ?? 'original';
}

export const useConfigStore = defineStore('config', () => {
  const config = ref<AppConfig>(FALLBACK);
  const loaded = ref(false);

  let inFlight: Promise<void> | null = null;

  /** Fetched once at boot and held; the values are stable server-side. */
  async function load(): Promise<void> {
    if (loaded.value) return;
    if (inFlight) return inFlight;

    inFlight = (async () => {
      try {
        config.value = await api<AppConfig>('/api/config');
        loaded.value = true;
      } catch {
        // Keep the fallback. Posters degrade, lists and ratings do not.
      } finally {
        inFlight = null;
      }
    })();

    return inFlight;
  }

  function imageUrl(sizes: string[], path: string | null, targetWidthPx: number): string | null {
    if (!path) return null;
    const size = pickSize(sizes, targetWidthPx);
    return `${config.value.imageBaseUrl}${size}${path}`;
  }

  function posterUrl(path: string | null, targetWidthPx: number): string | null {
    return imageUrl(config.value.posterSizes, path, targetWidthPx);
  }

  function backdropUrl(path: string | null, targetWidthPx: number): string | null {
    return imageUrl(config.value.backdropSizes, path, targetWidthPx);
  }

  function profileUrl(path: string | null, targetWidthPx: number): string | null {
    return imageUrl(config.value.profileSizes, path, targetWidthPx);
  }

  /** FR-B9: this string must be rendered, not paraphrased. */
  const attributionText = computed(() => config.value.attribution.text);
  const attributionLogoUrl = computed(() => config.value.attribution.logoUrl);

  return {
    config,
    loaded,
    load,
    posterUrl,
    backdropUrl,
    profileUrl,
    attributionText,
    attributionLogoUrl
  };
});
