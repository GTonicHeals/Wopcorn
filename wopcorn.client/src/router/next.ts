import type { LocationQueryValue } from 'vue-router';

/**
 * Turns a `?next=` query value into a path we are willing to navigate to.
 *
 * Only same-origin absolute paths qualify: a protocol-relative `//evil.example`
 * or a full URL would be an open redirect.
 */
export function safeNextPath(
  next: LocationQueryValue | LocationQueryValue[] | undefined
): string {
  const candidate = Array.isArray(next) ? next[0] : next;
  if (typeof candidate !== 'string') return '/';
  if (!candidate.startsWith('/') || candidate.startsWith('//')) return '/';
  return candidate;
}
