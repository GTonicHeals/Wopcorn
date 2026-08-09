<script setup lang="ts">
import { computed } from 'vue';

import BaseButton from '@/components/BaseButton.vue';
import IconAlert from '@/components/icons/IconAlert.vue';
import { ApiError } from '@/api/client';

const props = withDefaults(
  defineProps<{
    error: ApiError | Error | null;
    retryable?: boolean;
    /**
     * Overrides for a failure the screen understands better than the code does.
     * fe-07 uses them for the one case where a bare `403` would mislead: a
     * friendship that ended mid-session is not "you do not have access", it is
     * "you are no longer friends with this person".
     */
    headline?: string;
    body?: string;
  }>(),
  { retryable: true, headline: undefined, body: undefined }
);

defineEmits<{ retry: [] }>();

/**
 * NFR-10: a failure never renders as a blank region, and upstream trouble names
 * what still works. The tmdb_unavailable copy is fixed by fe-05 — it is the one
 * error a user is likely to meet during normal use.
 */
const derived = computed(() => {
  const error = props.error;
  const code = error instanceof ApiError ? error.code : 'network_error';

  switch (code) {
    case 'tmdb_unavailable':
      return {
        headline: 'Film search is unavailable',
        body: "TMDB isn't responding right now. Your lists, ratings, and friends are unaffected."
      };
    case 'network_error':
      return {
        headline: 'Wopcorn is not responding',
        body: error?.message ?? "Wopcorn's server is not responding."
      };
    case 'forbidden':
      return {
        headline: 'Not available to you',
        body: error?.message ?? 'You do not have access to this.'
      };
    case 'not_found':
      return {
        headline: 'Nothing here',
        body: error?.message ?? 'That page or film does not exist.'
      };
    default:
      return {
        headline: 'Something went wrong',
        body: error?.message ?? 'Try again in a moment.'
      };
  }
});

const presentation = computed(() => ({
  headline: props.headline ?? derived.value.headline,
  body: props.body ?? derived.value.body
}));
</script>

<template>
  <div class="error-state" role="alert">
    <span class="error-state__icon" aria-hidden="true"><IconAlert /></span>
    <p class="error-state__headline">{{ presentation.headline }}</p>
    <p class="error-state__body">{{ presentation.body }}</p>
    <BaseButton v-if="retryable" variant="secondary" @click="$emit('retry')">Retry</BaseButton>
    <!-- A way out, for failures that retrying cannot fix. -->
    <div v-if="$slots.action" class="error-state__action"><slot name="action" /></div>
  </div>
</template>

<style scoped>
.error-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: var(--space-2);
  padding: var(--space-8) var(--space-4);
  max-width: 44ch;
  margin: 0 auto;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
}

.error-state__icon {
  color: var(--text-muted);
  margin-bottom: var(--space-1);
}

.error-state__headline {
  font-size: var(--text-base);
  font-weight: 600;
}

.error-state__body {
  font-size: var(--text-sm);
  color: var(--text-muted);
  margin-bottom: var(--space-3);
}

.error-state__action {
  display: flex;
  gap: var(--space-2);
  flex-wrap: wrap;
  justify-content: center;
}
</style>
