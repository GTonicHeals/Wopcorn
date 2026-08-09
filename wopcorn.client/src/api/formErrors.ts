import { ApiError } from '@/api/client';

export type FormErrors = {
  /** Keyed by the client-side field name, in the caller's casing. */
  fields: Record<string, string[]>;
  /** One banner above the form for anything that has no field to sit under. */
  banner: string;
};

/**
 * Splits an API failure into per-field messages and a banner.
 *
 * The server keys its `errors` map off .NET model-state names (`DisplayName`,
 * sometimes `displayName`, occasionally `""`), so matching is
 * case-insensitive and anything unclaimed falls back to the banner. A validation
 * failure must never disappear because its key did not match.
 */
export function distributeErrors(error: unknown, fields: readonly string[]): FormErrors {
  if (!(error instanceof ApiError)) {
    return { fields: {}, banner: 'Something went wrong. Try again.' };
  }

  const result: Record<string, string[]> = {};
  for (const field of fields) {
    const messages = error.fieldErrors(field);
    if (messages.length > 0) result[field] = messages;
  }

  const claimed = new Set(fields.map((field) => field.toLowerCase()));
  const leftovers: string[] = [];
  for (const [key, messages] of Object.entries(error.errors ?? {})) {
    if (!claimed.has(key.toLowerCase())) leftovers.push(...messages);
  }

  const placedAny = Object.keys(result).length > 0;
  const banner = leftovers.length > 0
    ? leftovers.join(' ')
    : placedAny
      ? ''
      : error.message;

  return { fields: result, banner };
}
