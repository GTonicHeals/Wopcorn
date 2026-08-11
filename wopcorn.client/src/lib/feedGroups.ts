/**
 * Consecutive activity from one friend, collapsed into one entry.
 *
 * The feed arrives as one event per title, which is what the API says and what
 * the wire type is. Rendered that way it reads badly: queuing six films in one
 * sitting produces six identical sentences, each above a single poster, and on a
 * desktop column that is six screens of mostly empty band. Grouping is a
 * *display* decision and nothing else — no event is dropped, reordered, or
 * merged with a non-adjacent one, so the timeline still reads newest-first.
 *
 * Three rules decide whether an event joins the group above it:
 *
 * - **A rating is never grouped.** The stars belong to one title; a row of
 *   posters under a single "rated ★★★★☆" line could not say whose stars those
 *   were.
 * - **A day is the reach.** The group shows one timestamp, the newest, so
 *   swallowing an event from three weeks earlier would make that timestamp a
 *   lie. Adjacent events more than 24h apart stay separate.
 * - **A title appears once per group.** Two events for the same title in one
 *   group would render the same card twice.
 */
import type { ActivityItem } from '@/api/types';

export type ActivityGroup = {
  /** The newest event's id — stable for `v-for` as long as that event is. */
  id: string;
  user: ActivityItem['user'];
  kind: ActivityItem['kind'];
  /** Newest first, in the order the feed delivered them. Never empty. */
  items: ActivityItem[];
  /** The newest event's instant: the one timestamp the group shows. */
  occurredAt: string;
};

const GROUP_WINDOW_MS = 24 * 60 * 60 * 1000;

/** Milliseconds between two events, or `null` if either instant is unreadable. */
function gap(a: ActivityItem, b: ActivityItem): number | null {
  const first = new Date(a.occurredAt).getTime();
  const second = new Date(b.occurredAt).getTime();
  if (Number.isNaN(first) || Number.isNaN(second)) return null;

  return Math.abs(first - second);
}

function joins(group: ActivityGroup, item: ActivityItem): boolean {
  if (group.kind === 'rated' || item.kind === 'rated') return false;
  if (group.kind !== item.kind) return false;
  if (group.user.id !== item.user.id) return false;
  if (group.items.some((held) => held.title.key === item.title.key)) return false;

  const previous = group.items[group.items.length - 1];
  if (!previous) return false;

  const distance = gap(previous, item);
  return distance !== null && distance <= GROUP_WINDOW_MS;
}

/**
 * Groups a page-ordered activity list. Safe to re-run over the whole array as
 * more pages arrive: grouping depends only on neighbours, so appending a page
 * either extends the last group or starts a new one, exactly as it would have
 * had the whole array arrived at once.
 */
export function groupActivity(items: ActivityItem[]): ActivityGroup[] {
  const groups: ActivityGroup[] = [];

  for (const item of items) {
    const open = groups[groups.length - 1];

    if (open && joins(open, item)) {
      open.items.push(item);
      continue;
    }

    groups.push({
      id: item.id,
      user: item.user,
      kind: item.kind,
      items: [item],
      occurredAt: item.occurredAt
    });
  }

  return groups;
}
