import { ref, watch } from 'vue';
import { defineStore } from 'pinia';

export type ThemePreference = 'system' | 'light' | 'dark';

/** Shared with the inline script in index.html that applies the theme before first paint. */
export const THEME_STORAGE_KEY = 'wopcorn.theme';

/** Selector order, System first — the OS preference is the default (FR-H8). */
export const THEME_OPTIONS: ThemePreference[] = ['system', 'light', 'dark'];

function readStored(): ThemePreference {
  try {
    const stored = window.localStorage.getItem(THEME_STORAGE_KEY);
    return THEME_OPTIONS.includes(stored as ThemePreference)
      ? (stored as ThemePreference)
      : 'system';
  } catch {
    // Private mode or storage disabled: follow the OS.
    return 'system';
  }
}

/**
 * `system` means *no* attribute, so the `prefers-color-scheme` block in
 * tokens.css decides. An explicit attribute overrides the OS in both
 * directions, which is why the light palette is written twice (FR-H8).
 */
function applyToDocument(preference: ThemePreference): void {
  const root = document.documentElement;
  if (preference === 'system') {
    root.removeAttribute('data-theme');
  } else {
    root.setAttribute('data-theme', preference);
  }
}

export const useThemeStore = defineStore('theme', () => {
  const preference = ref<ThemePreference>(readStored());

  // Registered inside the store's effect scope so it is disposed with the store.
  watch(preference, (next) => {
    applyToDocument(next);
    try {
      window.localStorage.setItem(THEME_STORAGE_KEY, next);
    } catch {
      // The choice just will not survive a reload.
    }
  });

  function set(next: ThemePreference): void {
    preference.value = next;
  }

  /**
   * Re-applies the stored preference. The inline script in index.html already
   * did this before first paint; this keeps the document honest if the store is
   * created after some other code has touched the attribute.
   */
  function init(): void {
    applyToDocument(preference.value);
  }

  return { preference, set, init };
});
