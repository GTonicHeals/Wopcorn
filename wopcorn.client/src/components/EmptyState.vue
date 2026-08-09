<script setup lang="ts">
import IconFilm from '@/components/icons/IconFilm.vue';

defineProps<{
  headline: string;
  /** One sentence. Say what to do next, not that something is missing. */
  body?: string;
}>();
</script>

<template>
  <div class="empty">
    <span class="empty__icon" aria-hidden="true">
      <slot name="icon"><IconFilm /></slot>
    </span>
    <p class="empty__headline">{{ headline }}</p>
    <p v-if="body" class="empty__body">{{ body }}</p>
    <div v-if="$slots.action" class="empty__action"><slot name="action" /></div>
  </div>
</template>

<style scoped>
.empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: var(--space-2);
  padding: var(--space-12) var(--space-4);
  max-width: 42ch;
  margin: 0 auto;
}

.empty__icon {
  color: var(--text-muted);
  margin-bottom: var(--space-2);
}

.empty__icon :deep(svg) {
  width: 28px;
  height: 28px;
}

.empty__headline {
  font-size: var(--text-lg);
  font-weight: 600;
}

.empty__body {
  font-size: var(--text-sm);
  color: var(--text-muted);
}

.empty__action {
  margin-top: var(--space-4);
}
</style>
