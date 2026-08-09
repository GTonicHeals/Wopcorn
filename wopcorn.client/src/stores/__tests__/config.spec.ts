import { afterEach, describe, expect, it } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';

import { pickSize, useConfigStore } from '@/stores/config';

const POSTER_SIZES = ['w92', 'w154', 'w185', 'w342', 'w500', 'w780', 'original'];

function setDevicePixelRatio(ratio: number): void {
  Object.defineProperty(window, 'devicePixelRatio', { value: ratio, configurable: true });
}

afterEach(() => {
  setDevicePixelRatio(1);
});

describe('pickSize', () => {
  it('picks the smallest size that covers the target width at DPR 1', () => {
    setDevicePixelRatio(1);

    expect(pickSize(POSTER_SIZES, 92)).toBe('w92');
    expect(pickSize(POSTER_SIZES, 100)).toBe('w154');
    expect(pickSize(POSTER_SIZES, 158)).toBe('w185');
  });

  it('accounts for DPR 2', () => {
    setDevicePixelRatio(2);

    // 158 CSS px on a 2x screen needs 316 device px.
    expect(pickSize(POSTER_SIZES, 158)).toBe('w342');
    expect(pickSize(POSTER_SIZES, 46)).toBe('w92');
  });

  it('accounts for DPR 3', () => {
    setDevicePixelRatio(3);

    // 158 -> 474, 96 -> 288.
    expect(pickSize(POSTER_SIZES, 158)).toBe('w500');
    expect(pickSize(POSTER_SIZES, 96)).toBe('w342');
  });

  // 138px is the grid card's minimum column (fe-06, tasks 1 and 2), so it is the
  // width the app actually asks for most often.
  it('covers the 138px card at every device pixel ratio', () => {
    setDevicePixelRatio(1);
    expect(pickSize(POSTER_SIZES, 138)).toBe('w154');

    setDevicePixelRatio(2);
    expect(pickSize(POSTER_SIZES, 138)).toBe('w342');

    setDevicePixelRatio(3);
    expect(pickSize(POSTER_SIZES, 138)).toBe('w500');
  });

  it('falls back to the largest declared size when nothing covers the target', () => {
    setDevicePixelRatio(3);

    expect(pickSize(POSTER_SIZES, 600)).toBe('original');
  });
});

describe('config store', () => {
  it('builds poster URLs from the configured base and returns null without a path', () => {
    setActivePinia(createPinia());
    setDevicePixelRatio(1);

    const config = useConfigStore();

    expect(config.posterUrl('/abc123.jpg', 158)).toBe(
      'https://image.tmdb.org/t/p/w185/abc123.jpg'
    );
    expect(config.posterUrl(null, 158)).toBeNull();
  });
});
