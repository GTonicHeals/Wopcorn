import { onScopeDispose } from 'vue';

import { isSearchHotkey } from '@/lib/hotkeys';

/**
 * Binds `/` and Ctrl/Cmd-K on the window for the lifetime of the calling scope.
 *
 * Registered once, by `AppShell`, rather than by the overlay itself: the overlay
 * has to be openable while it is closed, and a listener that lives on the thing
 * it opens cannot do that.
 */
export function useSearchHotkey(open: () => void): void {
  function onKeydown(event: KeyboardEvent): void {
    if (event.defaultPrevented || !isSearchHotkey(event)) return;

    // Ctrl/Cmd-K is a browser shortcut in some places, and `/` is quick-find in
    // Firefox; both have to be taken over for the shortcut to be reliable.
    event.preventDefault();
    open();
  }

  window.addEventListener('keydown', onKeydown);
  onScopeDispose(() => window.removeEventListener('keydown', onKeydown));
}
