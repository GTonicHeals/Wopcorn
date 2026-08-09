import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';

import TasteMatch from '@/components/TasteMatch.vue';

/**
 * `tasteMatch.ts` proves the rule; this proves the component is wired to it.
 *
 * Not a layout test — the assertion is on rendered *text*, which is exactly what
 * FR-G6 constrains: a percentage may not reach the screen below the overlap
 * threshold, and may never reach it without its sample size.
 */
function render(score: number | null, sharedCount: number, qualified: boolean) {
  return mount(TasteMatch, { props: { match: { score, sharedCount, qualified } } });
}

describe('TasteMatch.vue (FR-G6)', () => {
  it('shows the percentage and the sample size together', () => {
    const text = render(78, 24, true).text();

    expect(text).toContain('78% match');
    expect(text).toContain('based on 24 titles');
  });

  it('puts no percentage on screen when the match is unqualified', () => {
    const text = render(91, 3, false).text();

    expect(text).not.toContain('%');
    expect(text).not.toContain('91');
    expect(text).toContain('3 titles in common');
  });

  it('renders nothing at all without a match', () => {
    expect(mount(TasteMatch, { props: { match: null } }).text()).toBe('');
  });

  it('names the zero-overlap case rather than showing a zero', () => {
    expect(render(null, 0, false).text()).toContain('Nothing in common yet');
  });
});
