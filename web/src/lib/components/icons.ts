// Line icons on the client's 32-unit grid (client/content/themes/<id>/icons): round caps and joins, stroked in the
// current colour. Sci-fi draws a wide translucent stroke under the line (the client's glow); Fantasy draws one heavier line.
// objective and settings are the client's own paths.
const circle = (cx: number, cy: number, r: number) => `M${cx - r} ${cy} a${r} ${r} 0 1 0 ${2 * r} 0 a${r} ${r} 0 1 0 ${-2 * r} 0`;

export const ICONS = {
  objective: [circle(16, 16, 12), circle(16, 16, 6), circle(16, 16, 1.5)],
  settings: [circle(16, 16, 5), circle(16, 16, 9), 'M16 3 V7 M16 25 V29 M3 16 H7 M25 16 H29 M6.8 6.8 L9.6 9.6 M22.4 22.4 L25.2 25.2 M6.8 25.2 L9.6 22.4 M22.4 9.6 L25.2 6.8'],
  armies: ['M16 4 L26 8 V15 C26 21 21.5 25.5 16 28 C10.5 25.5 6 21 6 15 V8 Z', 'M11 13 L16 17 L21 13 M11 18 L16 22 L21 18'],
  library: ['M16 4 L27 10 V22 L16 28 L5 22 V10 Z', 'M5 10 L16 16 L27 10 M16 16 V28'],
  account: [circle(16, 11, 5), 'M6 27 C7 20.5 11 18 16 18 C21 18 25 20.5 26 27'],
  logout: ['M14 5 H7 V27 H14', 'M19 10 L25 16 L19 22 M25 16 H12'],
  plus: ['M16 7 V25 M7 16 H25'],
  edit: ['M7 25 L8.5 19.5 L21 7 L25 11 L12.5 23.5 Z', 'M18.5 9.5 L22.5 13.5'],
  move: ['M16 4 V28 M4 16 H28', 'M12 8 L16 4 L20 8 M12 24 L16 28 L20 24 M8 12 L4 16 L8 20 M24 12 L28 16 L24 20'],
  close: ['M9 9 L23 23 M23 9 L9 23'],
  upload: ['M16 21 V6 M10 12 L16 6 L22 12', 'M6 20 V26 H26 V20'],
  import: ['M16 5 V20 M10 14 L16 20 L22 14', 'M6 20 V26 H26 V20'],
  download: ['M16 4 V19 M10 13 L16 19 L22 13', 'M5 23 H27 M8 28 H24'],
  replay: ['M7 11 A11 11 0 1 1 5 17', 'M4 6 V11 H9', 'M13 11 L21 16 L13 21 Z'],
  chevron:['M12 8 L20 16 L12 24'],
  back: ['M20 8 L12 16 L20 24'],
  folder: ['M4 8 H12 L15 11 H28 V25 H4 Z'],
  miniature: [circle(16, 9, 4), 'M11 25 C11 19 12.5 15 16 15 C19.5 15 21 19 21 25', 'M7 26 H25'],
  terrain: ['M4 26 L11 14 L16 20 L21 11 L28 26 Z'],
  texture: ['M6 6 H26 V26 H6 Z', 'M6 16 H26 M16 6 V26'],
  alert: ['M16 5 L28 26 H4 Z', 'M16 13 V19 M16 23 V23.2'],
  activated: [circle(16, 16, 12), 'M10 16 L14 21 L22 11'],
} as const;

export type IconName = keyof typeof ICONS;
