// The client's two themes (client/src/Core/UiThemes.cs). The palettes, fonts and shapes are CSS in src/app.css under
// [data-theme]; static/theme-init.js sets data-theme before the app loads and reads the store the same way as loadTheme.
export const THEMES = [{ id: 'scifi', name: 'Sci-fi' }, { id: 'fantasy', name: 'Fantasy' }] as const;
export type ThemeId = (typeof THEMES)[number]['id'];
export const DEFAULT_THEME: ThemeId = 'scifi';
export const THEME_KEY = 'tabletop.theme';

export const resolveTheme = (id: unknown): ThemeId => THEMES.find((t) => t.id === id)?.id ?? DEFAULT_THEME;

export function loadTheme(): ThemeId {
  try { return resolveTheme(localStorage.getItem(THEME_KEY)); } catch { return DEFAULT_THEME; }
}

/** A blocked store keeps the choice for this page only. */
export function saveTheme(id: ThemeId): void {
  try { localStorage.setItem(THEME_KEY, id); } catch { /* private window or blocked site data */ }
}

export function applyTheme(id: ThemeId, root: { dataset: DOMStringMap | Record<string, string | undefined> } = document.documentElement): void {
  root.dataset.theme = id;
}
