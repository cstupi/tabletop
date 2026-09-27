import { ApiError } from '$lib/api/http';
export function loginErrorMessage(e: unknown): string {
  if (!(e instanceof ApiError)) return 'Something went wrong. Try again.';
  switch (e.status) {
    case 401: return 'Wrong email, password or code.';
    case 409: return 'An account with that email already exists — sign in instead.';
    case 422: return e.detail ?? e.title;
    default: return e.title;
  }
}
