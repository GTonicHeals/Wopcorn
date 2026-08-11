<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { RouterLink } from 'vue-router';

import BaseButton from '@/components/BaseButton.vue';
import PosterImage from '@/components/PosterImage.vue';
import StarDisplay from '@/components/StarDisplay.vue';
import UserAvatar from '@/components/UserAvatar.vue';
import { relativeTime } from '@/lib/relativeTime';
import { intentLabel, targetLabel } from '@/lib/suggestions';
import { titlePath } from '@/lib/titleKey';
import { useListsStore } from '@/stores/lists';
import { useSuggestionsStore } from '@/stores/suggestions';
import { useTitlesStore } from '@/stores/titles';
import type { Suggestion } from '@/api/types';

/**
 * What friends have suggested, and what you have suggested to them (plan 10).
 *
 * A row here is not a `TitleCard`: the card is built to be acted on with three
 * list toggles, and a suggestion has exactly two answers. Showing the full card
 * would offer four ways to say yes and none of them the one the sender asked
 * for. So this is a compact row — poster, who, why, two buttons.
 *
 * The whole section disappears when both directions are empty, because an
 * inbox that is always on screen saying "nothing" is a worse answer than
 * silence.
 */
const suggestions = useSuggestionsStore();
const lists = useListsStore();
const titles = useTitlesStore();

const busy = ref<string[]>([]);

onMounted(() => void suggestions.load());

const hasAny = computed(
  () => suggestions.incoming.length > 0 || suggestions.outgoing.length > 0
);

function isBusy(id: string): boolean {
  return busy.value.includes(id);
}

/** "Sam · for your queue, next up" — the sender's intent in their own terms. */
function intent(suggestion: Suggestion): string {
  return intentLabel(suggestion.target, suggestion.position);
}

/**
 * The two answers, and both of them depend on the state.
 *
 * `pending` wrote nothing, so the offer is the add itself: "Add to watchlist" /
 * "Dismiss". `added` already put the row there — offering to add it again names
 * something that has happened, so the pair becomes "Keep" / "Remove", which is
 * what the choice actually is once the title is sitting on the list. That is the
 * split API-CONTRACT.md draws between the two rows of its states table.
 */
function acceptLabel(suggestion: Suggestion): string {
  return suggestion.state === 'added' ? 'Keep' : `Add to ${targetLabel(suggestion.target)}`;
}

function dismissLabel(suggestion: Suggestion): string {
  return suggestion.state === 'added' ? 'Remove' : 'Dismiss';
}

async function withBusy(id: string, work: () => Promise<unknown>): Promise<void> {
  if (isBusy(id)) return;
  busy.value = [...busy.value, id];
  try {
    await work();
  } finally {
    busy.value = busy.value.filter((other) => other !== id);
  }
}

function respond(suggestion: Suggestion, answer: 'accept' | 'dismiss'): Promise<void> {
  return withBusy(suggestion.id, async () => {
    const ok =
      answer === 'accept'
        ? await suggestions.accept(suggestion.id)
        : await suggestions.dismiss(suggestion.id);

    if (!ok) return;

    // The badge on the shared title row is now stale wherever else it is on
    // screen, and the list it named has gained or lost a row.
    titles.patch(suggestion.title.key, { suggestion: null });
    await lists.load(suggestion.target === 'queue' ? 'queue' : 'watchlist', true);
  });
}

function withdraw(suggestion: Suggestion): Promise<void> {
  return withBusy(suggestion.id, () => suggestions.withdraw(suggestion.id));
}
</script>

<template>
  <template v-if="hasAny">
    <section
      v-if="suggestions.incoming.length > 0"
      class="section"
      aria-labelledby="suggestions-incoming"
    >
      <h2 id="suggestions-incoming" class="section__title">Suggested to you</h2>

      <ul class="rows">
        <li v-for="suggestion in suggestions.incoming" :key="suggestion.id" class="row">
          <RouterLink class="row__poster" :to="titlePath(suggestion.title.key)">
            <PosterImage
              :path="suggestion.title.posterPath"
              :title="suggestion.title.title"
              :release-year="suggestion.title.releaseYear"
              :width="52"
            />
          </RouterLink>

          <div class="row__body">
            <RouterLink class="row__title" :to="titlePath(suggestion.title.key)">
              {{ suggestion.title.title }}
            </RouterLink>

            <p class="row__who">
              <UserAvatar :user="suggestion.from" :size="20" />
              <span>{{ suggestion.from.displayName }}</span>
              <span aria-hidden="true">·</span>
              <span>{{ intent(suggestion) }}</span>
              <StarDisplay
                v-if="suggestion.fromRating !== null"
                :value="suggestion.fromRating"
                :size="11"
                :label="`${suggestion.from.displayName} rated it`"
                tone="neutral"
              />
            </p>

            <blockquote v-if="suggestion.comment" class="row__comment">
              {{ suggestion.comment }}
            </blockquote>

            <div class="row__actions">
              <BaseButton
                variant="primary"
                :loading="isBusy(suggestion.id)"
                @click="respond(suggestion, 'accept')"
              >
                {{ acceptLabel(suggestion) }}
              </BaseButton>
              <BaseButton
                variant="ghost"
                :disabled="isBusy(suggestion.id)"
                @click="respond(suggestion, 'dismiss')"
              >
                {{ dismissLabel(suggestion) }}
              </BaseButton>
            </div>
          </div>
        </li>
      </ul>
    </section>

    <section
      v-if="suggestions.outgoing.length > 0"
      class="section"
      aria-labelledby="suggestions-outgoing"
    >
      <h2 id="suggestions-outgoing" class="section__title">You suggested</h2>

      <ul class="rows">
        <li v-for="suggestion in suggestions.outgoing" :key="suggestion.id" class="row row--sent">
          <RouterLink class="row__poster" :to="titlePath(suggestion.title.key)">
            <PosterImage
              :path="suggestion.title.posterPath"
              :title="suggestion.title.title"
              :release-year="suggestion.title.releaseYear"
              :width="40"
            />
          </RouterLink>

          <div class="row__body">
            <RouterLink class="row__title" :to="titlePath(suggestion.title.key)">
              {{ suggestion.title.title }}
            </RouterLink>
            <p class="row__who">
              <span>to {{ suggestion.to.displayName }}</span>
              <span aria-hidden="true">·</span>
              <!--
                "Waiting" covers both pending and added: whether it went straight
                onto their list is their setting and their business.
              -->
              <span>{{ suggestion.state === 'accepted' ? 'accepted' : 'waiting' }}</span>
              <span aria-hidden="true">·</span>
              <span>{{ relativeTime(suggestion.sentAt) }}</span>
            </p>
          </div>

          <BaseButton
            variant="ghost"
            :disabled="isBusy(suggestion.id)"
            @click="withdraw(suggestion)"
          >
            Withdraw
          </BaseButton>
        </li>
      </ul>
    </section>
  </template>
</template>

<style scoped>
.section {
  margin-top: var(--space-8);
}

.section__title {
  font-family: var(--font-ui);
  font-size: var(--text-lg);
  font-weight: 600;
  margin-bottom: var(--space-3);
}

.rows {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.row {
  display: flex;
  gap: var(--space-3);
  padding: var(--space-3);
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
}

.row--sent {
  align-items: center;
}

.row__poster {
  flex: none;
  border-radius: var(--radius-sm);
}

.row__body {
  flex: 1;
  min-width: 0;
}

.row__title {
  display: block;
  font-size: var(--text-base);
  font-weight: 600;
  color: inherit;
  text-decoration: none;
}

.row__who {
  display: flex;
  align-items: center;
  gap: var(--space-1);
  flex-wrap: wrap;
  font-size: var(--text-xs);
  color: var(--text-muted);
  margin-top: 2px;
}

.row__comment {
  margin-top: var(--space-2);
  padding-left: var(--space-2);
  border-left: 2px solid var(--border);
  font-size: var(--text-sm);
  line-height: 1.5;
  white-space: pre-wrap;
}

.row__actions {
  display: flex;
  gap: var(--space-2);
  margin-top: var(--space-3);
  flex-wrap: wrap;
}
</style>
