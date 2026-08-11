<script setup lang="ts">
import StarDisplay from '@/components/StarDisplay.vue';
import UserAvatar from '@/components/UserAvatar.vue';
import { relativeTime } from '@/lib/relativeTime';
import { targetLabel } from '@/lib/suggestions';
import type { SuggestionNote } from '@/api/types';

/**
 * Who suggested this title to the viewer, what they said, and what they made of
 * it themselves (plan 10).
 *
 * This is the payoff for the speech bubble on the card: the bubble says a note
 * exists, and opening the title is where it can actually be read. So it keeps
 * **accepted** suggestions too — the accept/remove prompt is transient, but who
 * put a film in front of you and why is part of the title from then on.
 *
 * Their rating renders neutral, never in the accent, for the same reason a
 * friend's rating does everywhere else: gold is the signed-in user's own state.
 *
 * `compact` is the quick view's form of the same block. The note is the reason
 * the title is in front of you, so a dialog opened *from a list* has to be able
 * to answer "why is this here" — making the reader leave for the full screen to
 * read two sentences is the trip the quick view exists to avoid. Only the frame
 * shrinks: same attribution, same rating, and the comment is **never clamped**,
 * because a half-shown message is the problem rather than the fix.
 */
withDefaults(defineProps<{ notes: SuggestionNote[]; compact?: boolean }>(), {
  compact: false
});
</script>

<template>
  <section
    v-if="notes.length > 0"
    class="suggested"
    :class="{ 'suggested--compact': compact }"
    aria-labelledby="title-suggested"
  >
    <!--
      Still a heading in compact, just a quiet one: an avatar and a quote with
      nothing above them could as easily be a review as a recommendation.
    -->
    <h2 id="title-suggested" class="suggested__title">
      {{ notes.length === 1 ? 'Suggested by a friend' : 'Suggested by friends' }}
    </h2>

    <ul class="suggested__list">
      <li v-for="note in notes" :key="note.id" class="suggested__item">
        <div class="suggested__who">
          <UserAvatar :user="note.from" :size="compact ? 24 : 30" />
          <div class="suggested__lines">
            <p class="suggested__name">
              {{ note.from.displayName }}
              <span class="suggested__for">for your {{ targetLabel(note.target) }}</span>
            </p>
            <p class="suggested__when">
              <StarDisplay
                v-if="note.fromRating !== null"
                :value="note.fromRating"
                :size="12"
                :label="`${note.from.displayName} rated it`"
                tone="neutral"
              />
              <span v-else>hasn't rated it</span>
              <span aria-hidden="true">·</span>
              <span>{{ relativeTime(note.sentAt) }}</span>
            </p>
          </div>
        </div>

        <blockquote v-if="note.comment" class="suggested__comment">{{ note.comment }}</blockquote>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.suggested {
  margin-top: var(--space-8);
}

.suggested__title {
  font-family: var(--font-ui);
  font-size: var(--text-lg);
  font-weight: 600;
  margin-bottom: var(--space-3);
}

/*
 * Inside the quick view the dialog supplies the spacing and the title, so this
 * drops to a section label of the kind the title screen uses over its synopsis.
 */
.suggested--compact {
  margin-top: 0;
}

.suggested--compact .suggested__title {
  font-size: var(--text-xs);
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--text-muted);
  margin-bottom: var(--space-2);
}

.suggested--compact .suggested__item {
  padding: var(--space-2) var(--space-3) var(--space-3);
}

.suggested--compact .suggested__comment {
  font-size: var(--text-sm);
}

.suggested__list {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.suggested__item {
  padding: var(--space-3);
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
}

.suggested__who {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-width: 0;
}

.suggested__lines {
  min-width: 0;
}

.suggested__name {
  font-size: var(--text-sm);
  font-weight: 600;
}

.suggested__for {
  font-weight: 400;
  color: var(--text-muted);
}

.suggested__when {
  display: flex;
  align-items: center;
  gap: var(--space-1);
  flex-wrap: wrap;
  font-size: var(--text-xs);
  color: var(--text-muted);
  margin-top: 2px;
}

.suggested__comment {
  margin-top: var(--space-2);
  padding-left: var(--space-3);
  border-left: 2px solid var(--border);
  font-size: var(--text-base);
  line-height: 1.6;
  white-space: pre-wrap;
}
</style>
