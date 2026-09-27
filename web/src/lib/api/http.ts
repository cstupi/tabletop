import type { Problem } from './types';

export const API_BASE: string = (import.meta.env?.PUBLIC_API_BASE as string | undefined) ?? '';

export class ApiError extends Error {
  constructor(public status: number, public title: string, public detail?: string) { super(detail ?? title); }
}

let tokenProvider: () => string | null = () => null;
export function setTokenProvider(p: () => string | null) { tokenProvider = p; }

async function send<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers = new Headers({ accept: 'application/json' });
  if (body !== undefined) headers.set('content-type', 'application/json');
  const token = tokenProvider();
  const isAuthRoute = path.startsWith('/v1/auth/') && !(method === 'DELETE' && path === '/v1/auth/session');
  if (token && !isAuthRoute) headers.set('authorization', `Bearer ${token}`);
  const response = await fetch(API_BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  if (response.ok) { const text = await response.text(); return (text ? JSON.parse(text) : undefined) as T; }   // 202 and 204 carry no body
  let problem: Problem = {};
  try { problem = (await response.json()) as Problem; } catch { /* no body */ }
  if (response.status === 401 && !isAuthRoute) http.onUnauthorized();
  throw new ApiError(response.status, problem.title ?? response.statusText, problem.detail);
}

export const http = {
  onUnauthorized: (() => {}) as () => void,
  get: <T>(path: string) => send<T>('GET', path),
  post: <T>(path: string, body?: unknown) => send<T>('POST', path, body ?? {}),
  put: <T>(path: string, body?: unknown) => send<T>('PUT', path, body),
  patch: <T>(path: string, body: unknown) => send<T>('PATCH', path, body),
  del: <T = void>(path: string) => send<T>('DELETE', path),
};

export function query(params: Record<string, string | number | undefined>): string {
  const q = Object.entries(params).filter(([, v]) => v !== undefined && v !== '').map(([k, v]) => `${k}=${encodeURIComponent(String(v))}`).join('&');
  return q ? `?${q}` : '';
}
