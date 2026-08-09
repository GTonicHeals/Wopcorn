/**
 * The pure half of the global search shortcut. Pure functions — no window, no
 * component — because the rule that actually goes wrong is "is the user already
 * typing?", and that is worth testing on its own.
 */

/** Enough of a `KeyboardEvent` to decide; keeps the tests free of DOM events. */
export type HotkeyLike = {
  key: string;
  ctrlKey?: boolean;
  metaKey?: boolean;
  altKey?: boolean;
  shiftKey?: boolean;
  target?: EventTarget | null;
};

/**
 * Elements that own the keystroke. A bare-character shortcut must never eat a
 * character someone meant to type — including in the search field the shortcut
 * itself opens.
 */
export function isTypingTarget(target: EventTarget | null | undefined): boolean {
  if (!target || !(target instanceof HTMLElement)) return false;
  if (target.isContentEditable) return true;

  const tag = target.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
}

/**
 * `/` or Ctrl/Cmd-K.
 *
 * The two are deliberately not symmetric: Ctrl/Cmd-K carries a modifier and so
 * can never be mistaken for typing, and fires from anywhere. A bare `/` is a
 * printable character, so it only counts outside a field.
 */
export function isSearchHotkey(event: HotkeyLike): boolean {
  if (event.altKey) return false;

  if (event.ctrlKey || event.metaKey) {
    return (event.key === 'k' || event.key === 'K') && !event.shiftKey;
  }

  return event.key === '/' && !event.shiftKey && !isTypingTarget(event.target);
}

/**
 * Moves through a result list, wrapping at both ends. `-1` is "nothing active",
 * and stepping backwards from it lands on the last row rather than the first —
 * ArrowUp with no selection should reach the bottom of the list.
 *
 * Returns `-1` for an empty list, so there is never an active index pointing at
 * a row that is not rendered.
 */
export function wrapIndex(current: number, delta: number, length: number): number {
  if (length <= 0) return -1;

  const start = current < 0 ? (delta > 0 ? -1 : 0) : Math.min(current, length - 1);
  return (((start + delta) % length) + length) % length;
}
