<script setup lang="ts">
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue';

import BaseButton from '@/components/BaseButton.vue';
import IconSpeech from '@/components/icons/IconSpeech.vue';
import { useListsStore } from '@/stores/lists';

/**
 * The viewer's own note on a watched title (plan 10).
 *
 * Deliberately shaped like `StarRating` sitting above it: writing one marks the
 * title watched, clearing one leaves it watched, and both are one control that
 * shows the current value rather than a form you have to go and find.
 *
 * The note is visible to friends and to nobody else, which the empty state says
 * out loud — a text box that does not tell you who reads it is a text box people
 * are right to leave empty.
 */
const props = defineProps<{
  titleKey: string;
  titleName: string;
  /** The stored note, or null. */
  note: string | null;
}>();

/** Matches `ListEntry.MaxCommentLength` on the server, which rejects beyond it. */
const MAX_LENGTH = 2000;

const lists = useListsStore();

const editing = ref(false);
const draft = ref('');
const saving = ref(false);
const field = useTemplateRef<HTMLTextAreaElement>('field');

const remaining = computed(() => MAX_LENGTH - draft.value.trim().length);
const canSave = computed(() => draft.value.trim().length > 0 && remaining.value >= 0);

// Navigating between titles must not carry a half-written note across.
watch(
  () => props.titleKey,
  () => {
    editing.value = false;
    draft.value = '';
  }
);

async function open(): Promise<void> {
  draft.value = props.note ?? '';
  editing.value = true;
  await nextTick();
  field.value?.focus();
}

function cancel(): void {
  editing.value = false;
  draft.value = '';
}

async function save(): Promise<void> {
  if (!canSave.value || saving.value) return;

  saving.value = true;
  try {
    if (await lists.setComment(props.titleKey, draft.value.trim())) {
      editing.value = false;
      draft.value = '';
    }
  } finally {
    saving.value = false;
  }
}

async function remove(): Promise<void> {
  if (saving.value) return;

  saving.value = true;
  try {
    if (await lists.clearComment(props.titleKey)) {
      editing.value = false;
      draft.value = '';
    }
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <section class="note" aria-labelledby="title-note">
    <h2 id="title-note" class="note__title">
      <IconSpeech :filled="Boolean(note)" />
      Your note
    </h2>

    <template v-if="editing">
      <textarea
        ref="field"
        v-model="draft"
        class="note__field"
        rows="4"
        :maxlength="MAX_LENGTH"
        :aria-label="`Your note on ${titleName}`"
        placeholder="What did you make of it?"
      />
      <p class="note__count" :class="{ 'note__count--over': remaining < 0 }">
        {{ remaining }} characters left
      </p>

      <div class="note__actions">
        <BaseButton variant="ghost" :disabled="saving" @click="cancel">Cancel</BaseButton>
        <BaseButton variant="primary" :disabled="!canSave || saving" @click="save">
          {{ saving ? 'Saving…' : 'Save note' }}
        </BaseButton>
      </div>
    </template>

    <template v-else-if="note">
      <p class="note__body">{{ note }}</p>
      <div class="note__actions">
        <BaseButton variant="ghost" :disabled="saving" @click="remove">Delete</BaseButton>
        <BaseButton variant="ghost" @click="open">Edit</BaseButton>
      </div>
    </template>

    <template v-else>
      <p class="note__empty">Only your friends can see this.</p>
      <div class="note__actions">
        <BaseButton variant="ghost" @click="open">Write a note</BaseButton>
      </div>
    </template>
  </section>
</template>

<style scoped>
.note {
  margin-top: var(--space-8);
}

.note__title {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  font-family: var(--font-ui);
  font-size: var(--text-lg);
  font-weight: 600;
  margin-bottom: var(--space-3);
}

.note__title :deep(svg) {
  width: 18px;
  height: 18px;
  color: var(--text-muted);
}

.note__body {
  font-size: var(--text-base);
  line-height: 1.6;
  white-space: pre-wrap;
  padding: var(--space-3);
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
}

.note__empty {
  font-size: var(--text-sm);
  color: var(--text-muted);
}

.note__field {
  width: 100%;
  padding: var(--space-3);
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  color: var(--text);
  font-family: inherit;
  /* 16px, not the body size — see base.css: below it iOS zooms in on focus. */
  font-size: 16px;
  line-height: 1.6;
  resize: vertical;
}

.note__field:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 1px;
}

.note__count {
  font-size: var(--text-xs);
  color: var(--text-muted);
  margin-top: var(--space-1);
  font-variant-numeric: tabular-nums;
}

.note__count--over {
  color: var(--text);
  font-weight: 600;
}

.note__actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--space-2);
  margin-top: var(--space-2);
}
</style>
