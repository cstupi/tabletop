// web/tests/login-flow.test.ts
import { describe, expect, it } from 'vitest';
import { ApiError } from '../src/lib/api/http';
import { loginErrorMessage } from '../src/lib/session/messages';

describe('loginErrorMessage', () => {
  it('maps the statuses management documents', () => {
    expect(loginErrorMessage(new ApiError(401, 'Unauthorized', 'unauthorized_credentials'))).toBe('Wrong email, password or code.');
    expect(loginErrorMessage(new ApiError(422, 'Unprocessable', 'That password is too weak.'))).toBe('That password is too weak.');
    expect(loginErrorMessage(new ApiError(429, 'Too many requests', 'Too many attempts.'))).toBe('Too many requests. Wait a minute and try again.');
    expect(loginErrorMessage(new ApiError(502, 'Upstream failure', 'The sign-in service is unavailable.'))).toBe('The sign-in service is unavailable.');
    expect(loginErrorMessage(new ApiError(502, 'Upstream failure'))).toBe('The sign-in service is unavailable. Try again shortly.');
    expect(loginErrorMessage(new TypeError('Failed to fetch'))).toMatch(/Could not reach the server/);
    expect(loginErrorMessage({})).toBe('Something went wrong. Try again.');
  });
});
