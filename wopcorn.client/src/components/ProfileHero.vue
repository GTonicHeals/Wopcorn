<script setup lang="ts">
import { computed } from 'vue';

import UserAvatar from '@/components/UserAvatar.vue';
import { useConfigStore } from '@/stores/config';
import type { TitleCard, UserSummary } from '@/api/types';

/**
 * The marquee: the top of a profile, lit by the art of the title in the first
 * showcase slot.
 *
 * This is what makes the showcase's *order* mean something without numbering it.
 * Position numerals belong to the queue and nowhere else, so the first favourite
 * is marked by being the thing the room is lit by instead — and a profile with
 * an empty showcase sits on the plain background, which is the visible
 * difference between a curated profile and a bare one.
 *
 * The wash is decorative and hidden from the accessibility tree; everything it
 * says is said again in the showcase below it.
 */
const props = defineProps<{
  user: UserSummary;
  /** The first favourite, or null — the light source, not a content slot. */
  marquee: TitleCard | null;
  /** Small uppercase line above the name. */
  eyebrow?: string | null;
}>();

const config = useConfigStore();

// 500px of poster stretched behind a banner: any larger is bytes spent on
// something that is about to be blurred to 28px anyway.
const washUrl = computed(() => config.posterUrl(props.marquee?.posterPath ?? null, 500));
</script>

<template>
  <header class="hero" :class="{ 'hero--lit': washUrl !== null }">
    <div
      v-if="washUrl"
      class="hero__wash"
      aria-hidden="true"
      :style="{ backgroundImage: `url(${washUrl})` }"
    />
    <div v-if="washUrl" class="hero__scrim" aria-hidden="true" />

    <div class="hero__body">
      <UserAvatar class="hero__avatar" :user="user" :size="88" />

      <div class="hero__id">
        <p v-if="eyebrow" class="hero__eyebrow">{{ eyebrow }}</p>
        <h1 class="hero__name">{{ user.displayName }}</h1>
        <p v-if="$slots.meta" class="hero__meta"><slot name="meta" /></p>
      </div>

      <div v-if="$slots.actions" class="hero__actions"><slot name="actions" /></div>
    </div>
  </header>
</template>

<style scoped>
.hero {
  position: relative;
  isolation: isolate;
  overflow: hidden;
}

.hero__wash {
  position: absolute;
  /* Overdrawn, because a 28px blur eats its own edges. */
  inset: -12%;
  z-index: -2;
  background-size: cover;
  background-position: center 30%;
  filter: blur(28px) saturate(1.2);
  opacity: 0.34;
  /*
   * Faded at both edges as well as the bottom. The content column is narrower
   * than the screen, so an unmasked wash ends in two hard vertical lines and
   * reads as a panel someone forgot to finish rather than as light.
   */
  mask-image: linear-gradient(to right, transparent, #000 14%, #000 86%, transparent);
  animation: hero-lights 520ms ease-out both;
}

.hero__scrim {
  position: absolute;
  inset: 0;
  z-index: -1;
  /* The same bottom-to-top fade the queue hero and the title screen use. */
  background: var(--scrim);
}

/* Poster art behind dark text needs to sit further back than behind light text. */
:root[data-theme='light'] .hero__wash {
  opacity: 0.2;
}

@media (prefers-color-scheme: light) {
  :root:not([data-theme]) .hero__wash {
    opacity: 0.2;
  }
}

@keyframes hero-lights {
  from {
    opacity: 0;
  }
}

@media (prefers-reduced-motion: reduce) {
  .hero__wash {
    animation: none;
  }
}

.hero__body {
  display: flex;
  align-items: center;
  gap: var(--space-4);
  flex-wrap: wrap;
  padding: var(--space-6) var(--space-4) var(--space-4);
}

/* A lit marquee needs room under the avatar for the fade to read as light. */
.hero--lit .hero__body {
  padding-top: var(--space-8);
  padding-bottom: var(--space-6);
}

.hero__avatar {
  box-shadow: 0 2px 12px rgb(0 0 0 / 0.35);
}

/*
 * The basis is load-bearing. With `flex: 1` the name column happily shrinks to
 * nothing so the actions can stay on the avatar's line, and `overflow-wrap:
 * anywhere` below then breaks the display name one character per line. A basis
 * wide enough for a name means the actions wrap to their own row instead, which
 * is what should happen on a phone.
 */
.hero__id {
  min-width: 0;
  flex: 1 1 220px;
}

.hero__eyebrow {
  font-size: 11px;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--text-muted);
  font-weight: 600;
  margin-bottom: 2px;
}

.hero__name {
  font-family: var(--font-display);
  font-size: var(--text-2xl);
  font-weight: 500;
  line-height: 1.05;
  overflow-wrap: anywhere;
}

.hero__meta {
  margin-top: var(--space-1);
  font-size: var(--text-xs);
  color: var(--text-muted);
}

.hero__actions {
  display: flex;
  gap: var(--space-2);
  flex-wrap: wrap;
}

/* Only once they fit beside the name; below this they are their own row. */
@media (min-width: 700px) {
  .hero__actions {
    margin-left: auto;
  }
}

@media (min-width: 900px) {
  .hero__body {
    padding: var(--space-8) var(--space-6) var(--space-6);
  }

  .hero--lit .hero__body {
    padding-top: var(--space-12);
  }
}
</style>
