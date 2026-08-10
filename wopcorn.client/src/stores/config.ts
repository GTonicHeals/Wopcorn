import { computed, ref } from 'vue';
import { defineStore } from 'pinia';

import { api } from '@/api/client';
import type { AppConfig, WatchProvider } from '@/api/types';

/** Fallback so an unreachable /api/config never leaves the UI with dead image URLs. */
const FALLBACK: AppConfig = {
  imageBaseUrl: 'https://image.tmdb.org/t/p/',
  posterSizes: ['w92', 'w154', 'w185', 'w342', 'w500', 'w780', 'original'],
  backdropSizes: ['w300', 'w780', 'w1280', 'original'],
  profileSizes: ['w45', 'w185', 'h632', 'original'],
  logoSizes: ['w45', 'w92', 'w154', 'w185', 'original'],
  attribution: {
    text: 'This product uses the TMDB API but is not endorsed or certified by TMDB.',
    logoUrl: '',
    availabilityText: 'Streaming availability data provided by JustWatch.'
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

  /** A provider's brand mark. Rendered tiny, so the size tokens start at w45. */
  function logoUrl(path: string | null, targetWidthPx: number): string | null {
    return imageUrl(config.value.logoSizes, path, targetWidthPx);
  }

  /** FR-B9: this string must be rendered, not paraphrased. */
  const attributionText = computed(() => config.value.attribution.text);
  const attributionLogoUrl = computed(() => config.value.attribution.logoUrl);

  /** The same rule for the streaming data: rendered wherever availability is. */
  const availabilityAttribution = computed(() => config.value.attribution.availabilityText);

  // ------------------------------------------------------ provider directory

  /**
   * The services TMDB publishes for the viewer's region, cached per region.
   *
   * It is reference data that changes monthly and is read by three screens —
   * settings, the filter sheet, and the badges — so refetching it per screen is
   * exactly what NFR-2 exists to prevent. It lives here rather than in `titles`
   * because it is not a title, and it is keyed by region because it is not
   * global either.
   */
  const providersByRegion = ref(new Map<string, WatchProvider[]>());
  const providerRequests = new Map<string, Promise<WatchProvider[]>>();

  async function loadProviders(region: string | null): Promise<WatchProvider[]> {
    if (!region) return [];

    const cached = providersByRegion.value.get(region);
    if (cached) return cached;

    const pending = providerRequests.get(region);
    if (pending) return pending;

    const request = (async () => {
      try {
        const providers = await api<WatchProvider[]>('/api/providers');
        providersByRegion.value = new Map(providersByRegion.value).set(region, providers);
        return providers;
      } catch {
        // No directory costs the settings grid its options and the badges their
        // names; lists, ratings and everything else are untouched.
        return [];
      } finally {
        providerRequests.delete(region);
      }
    })();

    providerRequests.set(region, request);
    return request;
  }

  /** The directory as a lookup, for turning `availableOn` ids into logos. */
  function provider(region: string | null, id: number): WatchProvider | null {
    if (!region) return null;
    return providersByRegion.value.get(region)?.find((entry) => entry.id === id) ?? null;
  }

  return {
    config,
    loaded,
    load,
    posterUrl,
    backdropUrl,
    profileUrl,
    logoUrl,
    attributionText,
    attributionLogoUrl,
    availabilityAttribution,
    providersByRegion,
    loadProviders,
    provider
  };
});
