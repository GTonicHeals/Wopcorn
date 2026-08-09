import { beforeEach, describe, expect, it } from 'vitest';

import { isQuickViewClick } from '@/lib/quickView';

/**
 * The rule the whole feature rests on: a plain tap on a title opens the quick
 * view, and every other way of pressing the same pixels still means what it
 * always meant — a new tab, a middle-click, the toggles underneath.
 */

let link: HTMLAnchorElement;
let toggle: HTMLButtonElement;

beforeEach(() => {
  document.body.innerHTML = '';

  const card = document.createElement('article');
  link = document.createElement('a');
  link.href = '/title/movie-603';
  link.append(document.createElement('h3'));
  toggle = document.createElement('button');
  card.append(link, toggle);
  document.body.append(card);
});

function clickOn(target: Element, init: MouseEventInit = {}): MouseEvent {
  const event = new MouseEvent('click', { bubbles: true, button: 0, ...init });
  Object.defineProperty(event, 'target', { value: target });
  return event;
}

describe('isQuickViewClick', () => {
  it('claims a plain left click on the link', () => {
    expect(isQuickViewClick(clickOn(link))).toBe(true);
  });

  it('claims a click on something inside the link', () => {
    // The tap lands on the poster or the heading, never on the anchor itself.
    expect(isQuickViewClick(clickOn(link.firstElementChild!))).toBe(true);
  });

  it('leaves a modified click alone, so a new tab still gets the title screen', () => {
    expect(isQuickViewClick(clickOn(link, { ctrlKey: true }))).toBe(false);
    expect(isQuickViewClick(clickOn(link, { metaKey: true }))).toBe(false);
    expect(isQuickViewClick(clickOn(link, { shiftKey: true }))).toBe(false);
    expect(isQuickViewClick(clickOn(link, { altKey: true }))).toBe(false);
  });

  it('leaves a middle click alone', () => {
    expect(isQuickViewClick(clickOn(link, { button: 1 }))).toBe(false);
  });

  it('ignores anything outside the link — the list toggles keep working', () => {
    expect(isQuickViewClick(clickOn(toggle))).toBe(false);
  });
});
