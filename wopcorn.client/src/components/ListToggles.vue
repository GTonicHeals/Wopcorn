<script setup lang="ts">
import { computed, nextTick, ref, type Component } from 'vue';

import BaseButton from '@/components/BaseButton.vue';
import BaseSheet from '@/components/BaseSheet.vue';
import IconBookmark from '@/components/icons/IconBookmark.vue';
import IconEye from '@/components/icons/IconEye.vue';
import IconLayers from '@/components/icons/IconLayers.vue';
import { useListsStore } from '@/stores/lists';
import type { ListName, TitleCard } from '@/api/types';

/**
 * Watched / Watchlist / Queue, in that order, one tap each (FR-C2, FR-C3).
 *
 * Member state is an accent fill **and** a filled icon variant; non-member is a
 * transparent button with a border and an outline icon. Membership is never
 * signalled by colour alone (NFR-9), and each button carries `aria-pressed` plus
 * a label naming the title — that is the whole of "at a glance" for a screen
 * reader.
 *
 * A series and its seasons are separate titles, so these buttons act on exactly
 * the one they were given. Marking a season watched leaves the series alone, in
 * both directions — the data does not cascade and neither does this.
 */
const props = withDefaults(
  defineProps<{
    title: TitleCard;
    /** The title screen labels its buttons; a dense grid card does not. */
    showLabels?: boolean;
  }>(),
  { showLabels: false }
);

const lists = useListsStore();

type Toggle = { list: ListName; label: string; icon: Component; pressedLabel: string };

const toggles: Toggle[] = [
  { list: 'watched', label: 'Watched', icon: IconEye, pressedLabel: 'Watched' },
  { list: 'watchlist', label: 'Watchlist', icon: IconBookmark, pressedLabel: 'On your watchlist' },
  { list: 'queue', label: 'Queue', icon: IconLayers, pressedLabel: 'In your queue' }
];

/**
 * Names the thing precisely enough to be unambiguous when read aloud out of
 * context — a season announced as just "Season 2" could be any series'.
 */
const titleLabel = computed(() => {
  const title = props.title;
  if (title.mediaType === 'season') return title.title;

  const year = title.releaseYear;
  return year ? `${title.title} (${year})` : title.title;
});

function isOn(list: ListName): boolean {
  return props.title.lists[list];
}

function ariaLabel(toggle: Toggle): string {
  return `${isOn(toggle.list) ? toggle.pressedLabel : toggle.label} — ${titleLabel.value}`;
}

// ------------------------------------------------- the Watched special case

/**
 * FR-C6. Marking a title watched while it sits on the Watchlist or the Queue asks
 * once, then sends a single request with `alsoRemoveFrom`. When it is on neither,
 * there is nothing to ask and nothing is shown.
 */
const sheetOpen = ref(false);
/**
 * The sheet is a `<dialog>`, and this component renders once per card. A
 * 200-entry list must not carry 200 dialogs, so one is only created the first
 * time a card actually needs to ask (NFR-2).
 */
const sheetMounted = ref(false);
const removeFrom = ref<Record<'watchlist' | 'queue', boolean>>({ watchlist: false, queue: false });

const sourceLists = computed(() =>
  (['watchlist', 'queue'] as const).filter((list) => props.title.lists[list])
);

const sourceLabels: Record<'watchlist' | 'queue', string> = {
  watchlist: 'Remove from your watchlist',
  queue: 'Remove from your queue'
};

async function openWatchedSheet(): Promise<void> {
  // Pre-checked: removing is the expected outcome of having watched it.
  removeFrom.value = {
    watchlist: props.title.lists.watchlist,
    queue: props.title.lists.queue
  };

  // BaseSheet calls showModal() on a false→true transition, so it has to exist
  // with `open: false` for one tick before being opened.
  sheetMounted.value = true;
  await nextTick();
  sheetOpen.value = true;
}

async function confirmWatched(): Promise<void> {
  const alsoRemoveFrom = sourceLists.value.filter((list) => removeFrom.value[list]);
  sheetOpen.value = false;
  await lists.add('watched', props.title.key, { alsoRemoveFrom });
}

async function onToggle(list: ListName): Promise<void> {
  if (list === 'watched' && !isOn('watched') && sourceLists.value.length > 0) {
    await openWatchedSheet();
    return;
  }

  await lists.toggle(list, props.title.key);
}
</script>

<template>
  <div class="toggles">
    <button
      v-for="toggle in toggles"
      :key="toggle.list"
      type="button"
      class="toggle"
      :class="{ 'toggle--on': isOn(toggle.list) }"
      :aria-pressed="isOn(toggle.list)"
      :aria-label="ariaLabel(toggle)"
      @click.stop.prevent="onToggle(toggle.list)"
    >
      <component :is="toggle.icon" :filled="isOn(toggle.list)" />
      <span v-if="showLabels" class="toggle__label">{{ toggle.label }}</span>
    </button>
  </div>

  <BaseSheet v-if="sheetMounted" v-model:open="sheetOpen" :title="`Watched ${title.title}`">
    <p class="sheet-copy">It is on other lists. Take it off while marking it watched?</p>
    <ul class="sheet-options">
      <li v-for="list in sourceLists" :key="list">
        <label class="sheet-option">
          <input v-model="removeFrom[list]" type="checkbox" />
          <span>{{ sourceLabels[list] }}</span>
        </label>
      </li>
    </ul>

    <template #actions>
      <BaseButton variant="ghost" @click="sheetOpen = false">Cancel</BaseButton>
      <BaseButton variant="primary" @click="confirmWatched">Mark watched</BaseButton>
    </template>
  </BaseSheet>
</template>

<style scoped>
.toggles {
  display: flex;
  gap: var(--space-1);
}

.toggle {
  flex: 1;
  min-width: 0;
  /* FR-H4 floor. The mockup draws these at 40px; 44px wins. */
  height: var(--tap-min);
  display: flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-2);
  padding: 0 var(--space-2);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text-muted);
  font-size: var(--text-xs);
  font-weight: 600;
  transition: background-color 120ms ease, border-color 120ms ease, color 120ms ease;
}

/* Member: accent fill, accent-ink icon, and a filled icon variant (NFR-9). */
.toggle--on {
  background: var(--accent);
  border-color: var(--accent);
  color: var(--accent-ink);
}

.toggle :deep(svg) {
  width: 18px;
  height: 18px;
  flex: none;
}

.toggle__label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.sheet-copy {
  font-size: var(--text-sm);
  color: var(--text-muted);
  margin-bottom: var(--space-3);
}

.sheet-options {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}

.sheet-option {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  min-height: var(--tap-min);
  font-size: var(--text-base);
}

.sheet-option input {
  width: 20px;
  height: 20px;
  accent-color: var(--accent);
  flex: none;
}
</style>
