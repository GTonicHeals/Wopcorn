/**
 * Grid-or-table for the Watched and Watchlist screens, remembered across
 * sessions. One preference covers both — the switch is about how you want to
 * read a list of titles, not about which list you are on.
 *
 * The choice is a display preference, not user data, so it lives in
 * `localStorage` rather than on the server — nothing here is worth a round trip
 * or a column in `ListEntries`.
 *
 * Every access is wrapped: Safari in private mode throws on `localStorage`
 * access outright, and a preference that cannot be read is not worth a blank
 * screen. An unreadable or unrecognised value falls back to the default.
 */

export type ViewMode = 'grid' | 'table';

export const DEFAULT_VIEW_MODE: ViewMode = 'grid';

const KEY = 'wopcorn:list-view';

function isViewMode(value: unknown): value is ViewMode {
  return value === 'grid' || value === 'table';
}

export function readViewMode(): ViewMode {
  try {
    const stored = localStorage.getItem(KEY);
    return isViewMode(stored) ? stored : DEFAULT_VIEW_MODE;
  } catch {
    return DEFAULT_VIEW_MODE;
  }
}

export function writeViewMode(mode: ViewMode): void {
  try {
    localStorage.setItem(KEY, mode);
  } catch {
    // A preference that cannot be saved is still a preference for this session.
  }
}
