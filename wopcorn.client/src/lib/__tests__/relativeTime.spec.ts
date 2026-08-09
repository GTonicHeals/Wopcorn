import { describe, expect, it } from 'vitest';

import { absoluteTime, relativeTime } from '@/lib/relativeTime';

const NOW = Date.parse('2026-08-09T12:00:00.000Z');

function ago(ms: number): string {
  return new Date(NOW - ms).toISOString();
}

const SECOND = 1000;
const MINUTE = 60 * SECOND;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

describe('relativeTime', () => {
  it('rounds down to the largest whole unit', () => {
    expect(relativeTime(ago(30 * SECOND), NOW)).toBe('just now');
    expect(relativeTime(ago(2 * MINUTE), NOW)).toBe('2m ago');
    expect(relativeTime(ago(2 * HOUR), NOW)).toBe('2h ago');
    expect(relativeTime(ago(3 * DAY), NOW)).toBe('3d ago');
    expect(relativeTime(ago(14 * DAY), NOW)).toBe('2 weeks ago');
    expect(relativeTime(ago(90 * DAY), NOW)).toBe('3 months ago');
    expect(relativeTime(ago(800 * DAY), NOW)).toBe('2 years ago');
  });

  it('switches unit exactly on the boundary', () => {
    expect(relativeTime(ago(MINUTE - 1), NOW)).toBe('just now');
    expect(relativeTime(ago(MINUTE), NOW)).toBe('1m ago');
    expect(relativeTime(ago(HOUR - 1), NOW)).toBe('59m ago');
    expect(relativeTime(ago(HOUR), NOW)).toBe('1h ago');
    expect(relativeTime(ago(DAY - 1), NOW)).toBe('23h ago');
    expect(relativeTime(ago(DAY), NOW)).toBe('1d ago');
    expect(relativeTime(ago(7 * DAY), NOW)).toBe('1 week ago');
  });

  it('singularises the compound units', () => {
    expect(relativeTime(ago(7 * DAY), NOW)).toBe('1 week ago');
    expect(relativeTime(ago(31 * DAY), NOW)).toBe('1 month ago');
    expect(relativeTime(ago(400 * DAY), NOW)).toBe('1 year ago');
  });

  it('reads a future timestamp as "just now" rather than a negative age', () => {
    // Clock skew between the server and the device, not a real event.
    expect(relativeTime(new Date(NOW + 5 * MINUTE).toISOString(), NOW)).toBe('just now');
  });

  it('is empty for a missing or unparseable instant', () => {
    expect(relativeTime(null, NOW)).toBe('');
    expect(relativeTime(undefined, NOW)).toBe('');
    expect(relativeTime('', NOW)).toBe('');
    expect(relativeTime('not a date', NOW)).toBe('');
  });
});

describe('absoluteTime', () => {
  it('produces something for a valid instant and nothing for a bad one', () => {
    // The exact string is locale-dependent; that it is non-empty and not the
    // raw ISO input is the contract the `title` attribute relies on.
    const formatted = absoluteTime('2026-08-09T12:00:00.000Z');
    expect(formatted.length).toBeGreaterThan(0);
    expect(formatted).not.toBe('2026-08-09T12:00:00.000Z');

    expect(absoluteTime(null)).toBe('');
    expect(absoluteTime('not a date')).toBe('');
  });
});
