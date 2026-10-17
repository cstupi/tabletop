// Sets the theme before the first paint. An external file, so the hash-mode CSP's script-src 'self' admits it.
// Reads tabletop.theme as src/lib/theme/theme.ts loadTheme does: 'fantasy', else the default 'scifi'.
(function () {
  var id = null;
  try { id = localStorage.getItem('tabletop.theme'); } catch (e) { /* blocked store */ }
  document.documentElement.dataset.theme = id === 'fantasy' ? 'fantasy' : 'scifi';
})();
