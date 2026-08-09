import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { DEFAULT_VIEW_MODE, readViewMode, writeViewMode } from '@/lib/viewMode';

beforeEach(() => {
  localStorage.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('list view mode', () => {
  it('defaults to the grid when nothing has been chosen', () => {
    expect(readViewMode()).toBe('grid');
    expect(DEFAULT_VIEW_MODE).toBe('grid');
  });

  it('round-trips a choice', () => {
    writeViewMode('table');
    expect(readViewMode()).toBe('table');

    writeViewMode('grid');
    expect(readViewMode()).toBe('grid');
  });

  it('falls back on a value it does not recognise', () => {
    localStorage.setItem('wopcorn:list-view', 'spreadsheet');
    expect(readViewMode()).toBe('grid');
  });

  it('survives a localStorage that throws', () => {
    // Safari in private mode throws on access rather than returning null.
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('denied');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('denied');
    });

    expect(readViewMode()).toBe('grid');
    expect(() => writeViewMode('table')).not.toThrow();
  });
});
