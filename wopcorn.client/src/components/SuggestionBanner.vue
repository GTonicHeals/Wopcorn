<script setup lang="ts">
import { computed, ref } from 'vue';

import IconSpeech from '@/components/icons/IconSpeech.vue';
import { useListsStore } from '@/stores/lists';
import { useSuggestionsStore } from '@/stores/suggestions';
import { useTitlesStore } from '@/stores/titles';
import type { ListName, SuggestionBadge } from '@/api/types';

/**
 * "Recommended by Sam — Accept / Remove", wherever the title appears.
 *
 * The badge exists **only while the suggestion is unanswered**, so this component
 * renders exactly when there is a decision to make and vanishes the moment one is
 * taken. That is the whole behaviour: accepting keeps the title and drops the
 * line; removing drops both.
 *
 * It is deliberately not in the accent. Gold means the signed-in user's own
 * state, and a suggestion is the opposite of that — it is someone else asking. It
 * gets a raised surface and a border instead, which is loud enough for a row that
 * wants answering and quiet enough not to compete with the user's own ratings.
 *
 * The speech bubble is an **indicator**, not a disclosure: it says a note exists.
 * Reading it means opening the title, where the note sits beside the friend's
 * rating with room to be read.
 */
const props = withDefaults(
  defineProps<{
    badge: SuggestionBadge;
    /** The title the badge is attached to. */
    titleKey: string;
    /** Dense grid cards drop the verb and shrink the buttons. */
    compact?: boolean;
  }>(),
  { compact: false }
);

const suggestions = useSuggestionsStore();
const lists = useListsStore();
const titles = useTitlesStore();

const busy = ref(false);

const targetList = computed<ListName>(() =>
  props.badge.target === 'queue' ? 'queue' : 'watchlist'
);

const attribution = computed(() =>
  props.compact
    ? props.badge.from.displayName
    : `${props.badge.from.displayName} suggests this for your ${targetList.value}`
);

/**
 * "Remove" when the suggestion put the title there and taking it back means
 * taking the title off; "Dismiss" when nothing was ever written and there is only
 * the message to clear. Two words because they are two different outcomes.
 */
const dismissLabel = computed(() => (props.badge.state === 'added' ? 'Remove' : 'Dismiss'));

async function respond(answer: 'accept' | 'dismiss'): Promise<void> {
  if (busy.value) return;
  busy.value = true;

  try {
    const ok =
      answer === 'accept'
        ? await suggestions.accept(props.badge.id)
        : await suggestions.dismiss(props.badge.id);

    if (!ok) return;

    // The badge goes first so the line disappears in the same frame as the tap.
    titles.patch(props.titleKey, { suggestion: null });

    // Both answers can move the list underneath: accepting a pending suggestion
    // adds a row, removing an added one takes one away, and either can renumber
    // the queue. Re-reading it is cheaper than replaying the server's rules here.
    await lists.load(targetList.value, true);
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <div class="suggestion" :class="{ 'suggestion--compact': compact }">
    <p class="suggestion__who">
      <IconSpeech v-if="badge.comment" class="suggestion__bubble" filled />
      <span class="suggestion__text">{{ attribution }}</span>
      <span v-if="badge.comment" class="sr-only">— left a note; open the title to read it</span>
    </p>

    <div class="suggestion__actions">
      <button
        type="button"
        class="suggestion__action suggestion__action--accept"
        :disabled="busy"
        :aria-label="`Accept ${badge.from.displayName}'s suggestion`"
        @click.stop.prevent="respond('accept')"
      >
        Accept
      </button>
      <button
        type="button"
        class="suggestion__action"
        :disabled="busy"
        :aria-label="`${dismissLabel} ${badge.from.displayName}'s suggestion`"
        @click.stop.prevent="respond('dismiss')"
      >
        {{ dismissLabel }}
      </button>
    </div>
  </div>
</template>

<style scoped>
.suggestion {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-2);
  flex-wrap: wrap;
  padding: var(--space-2) var(--space-3);
  background: var(--surface-raised);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
}

.suggestion--compact {
  padding: var(--space-1) var(--space-2);
  gap: var(--space-1);
}

.suggestion__who {
  display: flex;
  align-items: center;
  gap: var(--space-1);
  min-width: 0;
  font-size: var(--text-xs);
  color: var(--text-muted);
}

.suggestion__text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.suggestion__bubble {
  flex: none;
}

.suggestion__bubble :deep(svg),
.suggestion__bubble {
  width: 13px;
  height: 13px;
}

.suggestion__actions {
  display: flex;
  gap: var(--space-1);
  flex: none;
}

/*
 * Below the 44px floor on purpose, and only here. FR-H4 governs the controls a
 * card is *for* — the three list toggles under it are full size. This is a
 * transient strip that disappears on first use, and giving it two 44px buttons
 * would make the recommendation taller than the poster it is about.
 */
.suggestion__action {
  min-height: 28px;
  padding: 0 var(--space-2);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-muted);
  font-size: var(--text-xs);
  font-weight: 600;
  white-space: nowrap;
}

.suggestion__action:disabled {
  opacity: 0.5;
}

.suggestion__action--accept {
  border-color: var(--text-muted);
  color: var(--text);
}
</style>
