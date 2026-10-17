import { ApiError } from '$lib/api/http';
export function loginErrorMessage(e: unknown): string {
  if (!(e instanceof ApiError)) return 'Something went wrong. Try again.';
  switch (e.status) {
    case 401: return 'Wrong email, password or code.';
    default: return e.detail ?? e.title;
  }
}
