import { describe, expect, it } from 'vitest';

import { ApiError } from '@/api/client';
import { distributeErrors } from '@/api/formErrors';

describe('distributeErrors', () => {
  it('places messages under their field regardless of key casing', () => {
    const error = new ApiError(400, 'validation_failed', 'Some fields need attention.', {
      DisplayName: ['Too short.'],
      email: ['That is not an email address.']
    });

    const { fields, banner } = distributeErrors(error, ['displayName', 'email', 'password']);

    expect(fields.displayName).toEqual(['Too short.']);
    expect(fields.email).toEqual(['That is not an email address.']);
    expect(fields.password).toBeUndefined();
    expect(banner).toBe('');
  });

  it('sends unclaimed messages to the banner rather than dropping them', () => {
    // Identity reports some failures with no field at all.
    const error = new ApiError(400, 'validation_failed', 'Some fields need attention.', {
      '': ['Registration is closed.']
    });

    const { fields, banner } = distributeErrors(error, ['email', 'password']);

    expect(fields).toEqual({});
    expect(banner).toBe('Registration is closed.');
  });

  it('falls back to the error message when there is no errors map', () => {
    const error = new ApiError(401, 'unauthenticated', 'Email or password is incorrect.');

    expect(distributeErrors(error, ['email', 'password']).banner).toBe(
      'Email or password is incorrect.'
    );
  });

  it('does not leak a non-API failure into a field', () => {
    const { fields, banner } = distributeErrors(new Error('boom'), ['email']);

    expect(fields).toEqual({});
    expect(banner).toBe('Something went wrong. Try again.');
  });
});
