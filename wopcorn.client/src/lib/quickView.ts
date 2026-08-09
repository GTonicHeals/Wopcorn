/**
 * Whether a click on a title card or row should be taken over by the quick-view
 * dialog instead of following the link.
 *
 * The cards stay **real links** — the `<a>` keeps its href — so middle-click,
 * ctrl/cmd-click and "open link in new tab" still land on the title screen, and
 * a keyboard Enter still opens the quick view because the browser delivers it
 * here as an unmodified left click.
 *
 * The caller runs this in the **capture** phase on the card, which is the only
 * place a click can be claimed before vue-router's own handler on the anchor
 * runs. Everything outside the link — the list toggles, the queue's grip and its
 * move buttons — has no `<a>` above it and keeps doing its own job.
 */
export function isQuickViewClick(event: MouseEvent): boolean {
  // A modified or secondary click means "open it somewhere else"; never take it.
  if (event.button !== 0) return false;
  if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return false;

  const target = event.target;
  return target instanceof Element && target.closest('a') !== null;
}
