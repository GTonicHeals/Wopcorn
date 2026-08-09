<script setup lang="ts">
/**
 * The top of every screen: a Fraunces title, an optional count beside it, and a
 * slot for one screen-level action. Fraunces appears here and on the film hero
 * only — never in cards or body copy.
 */
defineProps<{
  title: string;
  /** e.g. "84 films · 14h 51m" — sits on the title's baseline. */
  count?: string;
  /** Small uppercase line above the title. */
  eyebrow?: string;
}>();
</script>

<template>
  <header class="screen-head">
    <p v-if="eyebrow" class="screen-head__eyebrow">{{ eyebrow }}</p>
    <div class="screen-head__row">
      <h1 class="screen-head__title">{{ title }}</h1>
      <span v-if="count" class="screen-head__count">{{ count }}</span>
      <div v-if="$slots.actions" class="screen-head__actions"><slot name="actions" /></div>
    </div>
  </header>
</template>

<style scoped>
.screen-head {
  padding: var(--space-6) var(--space-4) var(--space-3);
}

.screen-head__eyebrow {
  font-size: 11px;
  letter-spacing: 0.18em;
  text-transform: uppercase;
  color: var(--text-muted);
  font-weight: 600;
  margin-bottom: var(--space-1);
}

.screen-head__row {
  display: flex;
  align-items: baseline;
  gap: var(--space-3);
  flex-wrap: wrap;
}

.screen-head__title {
  font-family: var(--font-display);
  font-size: var(--text-xl);
  font-weight: 500;
  line-height: 1.1;
}

.screen-head__count {
  font-size: var(--text-xs);
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

.screen-head__actions {
  margin-left: auto;
  align-self: center;
}

@media (min-width: 900px) {
  .screen-head {
    padding: var(--space-8) var(--space-6) var(--space-4);
  }
}
</style>
