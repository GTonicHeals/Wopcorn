import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';

import StarRating from '@/components/StarRating.vue';

/**
 * The keyboard half of FR-E5/NFR-8. The pointer→value arithmetic is covered by
 * `lib/__tests__/stars.spec.ts` as a pure function; `setPointerCapture` does not
 * exist in jsdom, so the gesture itself is a device check.
 *
 * Assertions are on emitted values, not markup — fe-07 may restyle this control.
 */
function mountControl(modelValue: number | null) {
  return mount(StarRating, { props: { modelValue } });
}

function emittedValues(wrapper: ReturnType<typeof mountControl>): number[] {
  const events = wrapper.emitted('update:modelValue') ?? [];
  return events.map((event) => (event as [number])[0]);
}

describe('StarRating keyboard steps', () => {
  it('steps up and down by one half-star', async () => {
    const wrapper = mountControl(4);
    const slider = wrapper.get('[role="slider"]');

    await slider.trigger('keydown', { key: 'ArrowRight' });
    expect(emittedValues(wrapper)).toEqual([5]);

    await slider.trigger('keydown', { key: 'ArrowLeft' });
    expect(emittedValues(wrapper)).toEqual([5, 3]);

    // Up/Down are the vertical synonyms of the same step.
    await slider.trigger('keydown', { key: 'ArrowUp' });
    await slider.trigger('keydown', { key: 'ArrowDown' });
    expect(emittedValues(wrapper)).toEqual([5, 3, 5, 3]);
  });

  it('jumps to the ends with Home and End', async () => {
    const wrapper = mountControl(6);
    const slider = wrapper.get('[role="slider"]');

    await slider.trigger('keydown', { key: 'Home' });
    await slider.trigger('keydown', { key: 'End' });

    expect(emittedValues(wrapper)).toEqual([1, 10]);
  });

  it('clamps at both ends instead of wrapping', async () => {
    const low = mountControl(1);
    await low.get('[role="slider"]').trigger('keydown', { key: 'ArrowLeft' });
    // Already at the minimum: no change, so nothing is emitted.
    expect(low.emitted('update:modelValue')).toBeUndefined();

    const high = mountControl(10);
    await high.get('[role="slider"]').trigger('keydown', { key: 'ArrowRight' });
    expect(high.emitted('update:modelValue')).toBeUndefined();
  });

  it('starts an unrated film at half a star', async () => {
    const wrapper = mountControl(null);

    await wrapper.get('[role="slider"]').trigger('keydown', { key: 'ArrowRight' });

    expect(emittedValues(wrapper)).toEqual([1]);
  });

  it('ignores keys it does not own', async () => {
    const wrapper = mountControl(4);

    await wrapper.get('[role="slider"]').trigger('keydown', { key: 'a' });

    expect(wrapper.emitted('update:modelValue')).toBeUndefined();
  });

  it('offers a clear affordance only once there is a rating', async () => {
    const rated = mountControl(6);
    await rated.get('button').trigger('click');
    expect(rated.emitted('clear')).toHaveLength(1);

    const unrated = mountControl(null);
    expect(unrated.find('button').exists()).toBe(false);
  });
});
