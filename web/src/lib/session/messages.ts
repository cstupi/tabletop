import { errorMessage } from '$lib/api/errors';
export const loginErrorMessage = (e: unknown): string => errorMessage(e, 'auth');
