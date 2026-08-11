<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';

import BaseButton from '@/components/BaseButton.vue';
import BaseSheet from '@/components/BaseSheet.vue';
import UserAvatar from '@/components/UserAvatar.vue';
import IconPeople from '@/components/icons/IconPeople.vue';
import { placementPosition, type QueuePlacement } from '@/lib/suggestions';
import { useFriendsStore } from '@/stores/friends';
import { useQueueStore } from '@/stores/queue';
import { useSuggestionsStore } from '@/stores/suggestions';
import { useToastStore } from '@/stores/toasts';
import type { SuggestionTarget } from '@/api/types';

/**
 * "Suggest to a friend", from the title screen (plan 10).
 *
 * The position control appears only for a queue suggestion, and it is expressed
 * in the **suggester's** terms — "near the top", "somewhere in the middle", "no
 * preference" — rather than as a number. A number would be a lie: it is read once
 * against a queue you cannot see, clamped to its length, and never re-asserted
 * afterwards. Three coarse intentions survive that honestly; "position 7" does
 * not.
 */
const props = defineProps<{ titleKey: string; titleName: string }>();

const friends = useFriendsStore();
const suggestions = useSuggestionsStore();
const queue = useQueueStore();
const toasts = useToastStore();

const open = ref(false);
const mounted = ref(false);
const sending = ref(false);

const recipientId = ref<string>('');
const target = ref<SuggestionTarget>('queue');
const placement = ref<QueuePlacement>('end');
const comment = ref('');

/** Matches `Suggestion.MaxCommentLength` on the server. */
const MAX_LENGTH = 500;

const remaining = computed(() => MAX_LENGTH - comment.value.trim().length);
const canSend = computed(() => recipientId.value !== '' && remaining.value >= 0 && !sending.value);

/** Already sent and still unanswered — the server would 409, so say so first. */
const alreadySent = computed(() =>
  recipientId.value ? suggestions.sentTo(recipientId.value, props.titleKey) : null
);

watch(
  () => props.titleKey,
  () => {
    comment.value = '';
    recipientId.value = '';
  }
);

/**
 * The three placements as a 0-based index, against the recipient's queue length —
 * which we do not know, so the viewer's own is the closest honest guess and the
 * server clamps whatever comes out of it.
 */
const position = computed<number | null>(() =>
  target.value === 'queue' ? placementPosition(placement.value, queue.keys.length) : null
);

async function show(): Promise<void> {
  await Promise.all([friends.load(), suggestions.load()]);

  // BaseSheet calls showModal() on a false→true transition, so the dialog has to
  // exist with `open: false` for one tick first.
  mounted.value = true;
  await nextTick();
  open.value = true;
}

async function send(): Promise<void> {
  if (!canSend.value) return;

  sending.value = true;
  try {
    const outcome = await suggestions.send({
      toUserId: recipientId.value,
      key: props.titleKey,
      target: target.value,
      position: position.value,
      comment: comment.value.trim() || null
    });

    const name =
      friends.friends.find((friend) => friend.user.id === recipientId.value)?.user.displayName ??
      'them';

    if (outcome === 'sent') {
      open.value = false;
      comment.value = '';
      toasts.show(`Suggested ${props.titleName} to ${name}.`);
    } else if (outcome === 'already_suggested') {
      toasts.show(`You have already suggested this to ${name}.`);
    } else if (outcome === 'not_friends') {
      toasts.show(`You are not friends with ${name} any more.`);
    }
  } finally {
    sending.value = false;
  }
}
</script>

<template>
  <!--
    Bordered rather than ghost. It sits under the three list toggles, which are
    the loudest thing on the screen, and a borderless line of muted text below
    them read as a caption rather than a control. It stays *secondary* though —
    the accent means the viewer's own state, and this is the one button on the
    title screen that acts on somebody else's lists. The border gives it an
    edge to press; the gold stays with the toggles.
  -->
  <BaseButton class="suggest__open" variant="secondary" @click="show">
    <IconPeople class="suggest__open-icon" />
    Suggest to a friend
  </BaseButton>

  <BaseSheet v-if="mounted" v-model:open="open" :title="`Suggest ${titleName}`">
    <p v-if="friends.friends.length === 0" class="suggest__empty">
      Suggestions go to friends. Add someone first.
    </p>

    <template v-else>
      <fieldset class="suggest__group">
        <legend class="suggest__legend">Who</legend>
        <ul class="suggest__people">
          <li v-for="friend in friends.friends" :key="friend.user.id">
            <label class="suggest__person">
              <input v-model="recipientId" type="radio" :value="friend.user.id" name="recipient" />
              <UserAvatar :user="friend.user" :size="28" />
              <span>{{ friend.user.displayName }}</span>
            </label>
          </li>
        </ul>
      </fieldset>

      <fieldset class="suggest__group">
        <legend class="suggest__legend">For their</legend>
        <div class="suggest__choices">
          <label class="suggest__choice">
            <input v-model="target" type="radio" value="queue" name="target" />
            <span>Queue</span>
          </label>
          <label class="suggest__choice">
            <input v-model="target" type="radio" value="watchlist" name="target" />
            <span>Watchlist</span>
          </label>
        </div>
      </fieldset>

      <!-- Only the queue has an order to have an opinion about. -->
      <fieldset v-if="target === 'queue'" class="suggest__group">
        <legend class="suggest__legend">How soon</legend>
        <div class="suggest__choices">
          <label class="suggest__choice">
            <input v-model="placement" type="radio" value="top" name="placement" />
            <span>Next up</span>
          </label>
          <label class="suggest__choice">
            <input v-model="placement" type="radio" value="middle" name="placement" />
            <span>Fairly soon</span>
          </label>
          <label class="suggest__choice">
            <input v-model="placement" type="radio" value="end" name="placement" />
            <span>No rush</span>
          </label>
        </div>
        <p class="suggest__hint">
          A starting place, not a reservation — they can move it wherever they like.
        </p>
      </fieldset>

      <fieldset class="suggest__group">
        <legend class="suggest__legend">Say why (optional)</legend>
        <textarea
          v-model="comment"
          class="suggest__field"
          rows="3"
          :maxlength="MAX_LENGTH"
          placeholder="Why should they watch it?"
          aria-label="Why you are suggesting it"
        />
        <p class="suggest__hint">{{ remaining }} characters left</p>
      </fieldset>

      <p v-if="alreadySent" class="suggest__hint suggest__hint--warn">
        You already have a suggestion of this waiting with them.
      </p>
    </template>

    <template #actions>
      <BaseButton variant="ghost" @click="open = false">Cancel</BaseButton>
      <BaseButton
        v-if="friends.friends.length > 0"
        variant="primary"
        :disabled="!canSend"
        @click="send"
      >
        {{ sending ? 'Sending…' : 'Suggest' }}
      </BaseButton>
    </template>
  </BaseSheet>
</template>

<style scoped>
.suggest__open-icon {
  width: 18px;
  height: 18px;
  flex: none;
  /* Quieter than the label: the word is the action, the figures are the hint. */
  color: var(--text-muted);
}

.suggest__empty {
  font-size: var(--text-sm);
  color: var(--text-muted);
}

.suggest__group {
  border: 0;
  padding: 0;
  margin: 0 0 var(--space-4);
}

.suggest__legend {
  font-size: var(--text-xs);
  font-weight: 600;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.06em;
  margin-bottom: var(--space-2);
}

.suggest__people {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  max-height: 220px;
  overflow-y: auto;
}

.suggest__person {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-height: var(--tap-min);
  font-size: var(--text-base);
}

.suggest__choices {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-4);
}

.suggest__choice {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  min-height: var(--tap-min);
  font-size: var(--text-base);
}

.suggest__person input,
.suggest__choice input {
  width: 20px;
  height: 20px;
  accent-color: var(--accent);
  flex: none;
}

.suggest__field {
  width: 100%;
  padding: var(--space-3);
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  color: var(--text);
  font-family: inherit;
  font-size: var(--text-base);
  line-height: 1.5;
  resize: vertical;
}

.suggest__field:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 1px;
}

.suggest__hint {
  font-size: var(--text-xs);
  color: var(--text-muted);
  margin-top: var(--space-1);
}

.suggest__hint--warn {
  color: var(--text);
  font-weight: 600;
}
</style>
