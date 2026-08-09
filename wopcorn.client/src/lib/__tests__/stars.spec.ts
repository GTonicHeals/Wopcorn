import { describe, expect, it } from 'vitest';

import {
  MAX_RATING,
  MIN_RATING,
  clampRating,
  fillPercent,
  ratingText,
  starLabel,
  valueFromPointer
} from '@/lib/stars';

/** The control is a full-width row; 300px wide starting at x=50 is a realistic one. */
const ROW = { left: 50, width: 300 };

describe('valueFromPointer', () => {
  it('maps every tenth of the row to its half-star value', () => {
    // The nth tenth ends at left + n/10 * width and reads as n half-stars.
    for (let n = 1; n <= MAX_RATING; n++) {
      const endOfBand = ROW.left + (n / MAX_RATING) * ROW.width;

      expect(valueFromPointer(endOfBand, ROW)).toBe(n);
      // Just inside the band, away from the boundary.
      expect(valueFromPointer(endOfBand - 1, ROW)).toBe(n);
    }
  });

  it('holds at both edges', () => {
    // The very first pixel is one half-star, not zero.
    expect(valueFromPointer(ROW.left, ROW)).toBe(MIN_RATING);
    expect(valueFromPointer(ROW.left + 0.01, ROW)).toBe(MIN_RATING);

    // The last pixel is five stars, and dragging past the end saturates.
    expect(valueFromPointer(ROW.left + ROW.width, ROW)).toBe(MAX_RATING);
    expect(valueFromPointer(ROW.left + ROW.width + 400, ROW)).toBe(MAX_RATING);
  });

  it('saturates rather than going negative before the row', () => {
    expect(valueFromPointer(ROW.left - 200, ROW)).toBe(MIN_RATING);
  });

  it('survives a zero-width rect', () => {
    expect(valueFromPointer(10, { left: 0, width: 0 })).toBe(MIN_RATING);
  });
});

describe('clampRating', () => {
  it('keeps values inside 1..10', () => {
    expect(clampRating(0)).toBe(1);
    expect(clampRating(11)).toBe(10);
    expect(clampRating(6)).toBe(6);
    expect(clampRating(Number.NaN)).toBe(1);
  });
});

describe('fillPercent', () => {
  it('is a percentage of the five-star row', () => {
    expect(fillPercent(null)).toBe(0);
    expect(fillPercent(1)).toBe(10);
    expect(fillPercent(7)).toBe(70);
    expect(fillPercent(10)).toBe(100);
  });
});

describe('ratingText', () => {
  it('reads the way it would be spoken', () => {
    expect(ratingText(null)).toBe('Not rated');
    expect(ratingText(1)).toBe('Half a star');
    expect(ratingText(2)).toBe('1 star');
    expect(ratingText(7)).toBe('3 and a half stars');
    expect(ratingText(10)).toBe('5 stars');
  });
});

describe('starLabel', () => {
  it('is the histogram axis, on the five-star scale', () => {
    expect(starLabel(1)).toBe('0.5');
    expect(starLabel(2)).toBe('1.0');
    expect(starLabel(9)).toBe('4.5');
    expect(starLabel(10)).toBe('5.0');
  });

  it('is the same width on every row, so the axis is a straight column', () => {
    const widths = new Set(
      Array.from({ length: 10 }, (_, index) => starLabel(index + 1).length)
    );

    expect(widths).toEqual(new Set([3]));
  });
});
