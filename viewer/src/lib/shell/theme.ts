// viewer/src/lib/shell/theme.ts
// The website's theme choice (web/src/lib/theme/theme.ts): `tabletop.theme` in localStorage, 'fantasy' or the default 'scifi',
// on <html data-theme>. The viewer shares the website's origin, so a switch here holds there too.

export type ThemeId = 'scifi' | 'fantasy';
export const THEMES: readonly { readonly id: ThemeId; readonly name: string }[] = [{ id: 'scifi', name: 'Sci-fi' }, { id: 'fantasy', name: 'Fantasy' }];
const KEY = 'tabletop.theme';

export const resolveTheme = (raw: string | null): ThemeId => (raw === 'fantasy' ? 'fantasy' : 'scifi');

export function loadTheme(): ThemeId {
  try { return resolveTheme(localStorage.getItem(KEY)); } catch { return 'scifi'; }
}

export function chooseTheme(id: ThemeId): void {
  document.documentElement.dataset.theme = id;
  try { localStorage.setItem(KEY, id); } catch { /* private window or blocked site data */ }
}
