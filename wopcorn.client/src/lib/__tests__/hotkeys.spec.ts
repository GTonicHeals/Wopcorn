import { describe, expect, it } from 'vitest';

import { isSearchHotkey, isTypingTarget, wrapIndex } from '@/lib/hotkeys';

function field(tag: string): HTMLElement {
  return document.createElement(tag);
}

describe('isTypingTarget', () => {
  it('recognises the elements that own a keystroke', () => {
    expect(isTypingTarget(field('input'))).toBe(true);
    expect(isTypingTarget(field('textarea'))).toBe(true);
    expect(isTypingTarget(field('select'))).toBe(true);
  });

  it('recognises a contenteditable element', () => {
    const editable = field('div');
    editable.setAttribute('contenteditable', 'true');
    // jsdom does not derive isContentEditable from the attribute.
    Object.defineProperty(editable, 'isContentEditable', { value: true });

    expect(isTypingTarget(editable)).toBe(true);
  });

  it('is false for ordinary elements and for nothing at all', () => {
    expect(isTypingTarget(field('div'))).toBe(false);
    expect(isTypingTarget(field('button'))).toBe(false);
    expect(isTypingTarget(null)).toBe(false);
    expect(isTypingTarget(undefined)).toBe(false);
  });
});

describe('isSearchHotkey', () => {
  it('accepts a bare slash outside a field', () => {
    expect(isSearchHotkey({ key: '/', target: field('div') })).toBe(true);
  });

  it('refuses a slash typed into a field — it is a character there', () => {
    expect(isSearchHotkey({ key: '/', target: field('input') })).toBe(false);
    expect(isSearchHotkey({ key: '/', target: field('textarea') })).toBe(false);
  });

  it('accepts Ctrl/Cmd-K anywhere, including inside a field', () => {
    expect(isSearchHotkey({ key: 'k', ctrlKey: true, target: field('div') })).toBe(true);
    expect(isSearchHotkey({ key: 'k', metaKey: true, target: field('input') })).toBe(true);
    // Caps lock should not break the shortcut.
    expect(isSearchHotkey({ key: 'K', metaKey: true, target: field('div') })).toBe(true);
  });

  it('refuses combinations that are somebody else’s shortcut', () => {
    expect(isSearchHotkey({ key: 'k', target: field('div') })).toBe(false);
    expect(isSearchHotkey({ key: 'k', ctrlKey: true, shiftKey: true })).toBe(false);
    expect(isSearchHotkey({ key: 'k', ctrlKey: true, altKey: true })).toBe(false);
    expect(isSearchHotkey({ key: '/', altKey: true, target: field('div') })).toBe(false);
    expect(isSearchHotkey({ key: 'j', ctrlKey: true })).toBe(false);
  });
});

describe('wrapIndex', () => {
  it('steps forwards and backwards', () => {
    expect(wrapIndex(0, 1, 3)).toBe(1);
    expect(wrapIndex(2, -1, 3)).toBe(1);
  });

  it('wraps at both ends', () => {
    expect(wrapIndex(2, 1, 3)).toBe(0);
    expect(wrapIndex(0, -1, 3)).toBe(2);
  });

  it('enters the list from either end when nothing is active', () => {
    expect(wrapIndex(-1, 1, 3)).toBe(0);
    expect(wrapIndex(-1, -1, 3)).toBe(2);
  });

  it('never points past a list that has shrunk', () => {
    expect(wrapIndex(9, 1, 3)).toBe(0);
    expect(wrapIndex(9, -1, 3)).toBe(1);
  });

  it('has nothing to select in an empty list', () => {
    expect(wrapIndex(-1, 1, 0)).toBe(-1);
    expect(wrapIndex(0, -1, 0)).toBe(-1);
  });
});
