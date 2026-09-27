// web/tests/login-flow.test.ts
import { describe, expect, it } from 'vitest';
import { ApiError } from '../src/lib/api/http';
import { loginErrorMessage } from '../src/lib/session/messages';

describe('loginErrorMessage', () => {
  it('maps the statuses management documents', () => {
    expect(loginErrorMessage(new ApiError(401, 'Unauthorized', 'unauthorized_credentials'))).toBe('Wrong email, password or code.');
    expect(loginErrorMessage(new ApiError(409, 'Conflict', 'exists'))).toMatch(/already exists/);
    expect(loginErrorMessage(new ApiError(422, 'Unprocessable', 'weak_password'))).toBe('weak_password');
    expect(loginErrorMessage(new ApiError(502, 'Upstream failure'))).toBe('Upstream failure');
    expect(loginErrorMessage(new Error('boom'))).toBe('Something went wrong. Try again.');
  });
});
