// viewer/src/lib/play/iconPaths.ts
// The Godot theme icons' paths (client/content/themes/*/icons/<name>.svg, 32×32): strokes, then filled shapes. HudIcon.svelte draws them.

export const ICON_PATHS: { readonly [name: string]: { readonly stroke: readonly string[]; readonly fill: readonly string[] } } = {
  'skip-back': { stroke: ["M8 7 V25"], fill: ["M25 7 L12 16 L25 25 Z"] },
  'step-back': { stroke: [], fill: ["M22 7 L10 16 L22 25 Z"] },
  'step': { stroke: [], fill: ["M10 7 L22 16 L10 25 Z"] },
  'skip': { stroke: ["M24 7 V25"], fill: ["M7 7 L20 16 L7 25 Z"] },
  'play': { stroke: [], fill: ["M10 6 L26 16 L10 26 Z"] },
  'pause': { stroke: ["M11 7 V25 M21 7 V25"], fill: [] },
  'key': { stroke: ["M5 9 H27 A2 2 0 0 1 29 11 V22 A2 2 0 0 1 27 24 H5 A2 2 0 0 1 3 22 V11 A2 2 0 0 1 5 9 Z", "M7 13.5 H8 M12 13.5 H13 M17 13.5 H18 M22 13.5 H24 M9 17 H10 M14 17 H15 M19 17 H20 M10 20.5 H22"], fill: [] },
  'camera': { stroke: ["M5 11 H10 L12 7 H20 L22 11 H27 A1 1 0 0 1 28 12 V25 A1 1 0 0 1 27 26 H5 A1 1 0 0 1 4 25 V12 A1 1 0 0 1 5 11 Z", "M16 13 A5 5 0 1 1 15.99 13 Z"], fill: [] },
  'measure': { stroke: ["M3 22 L22 3 L29 10 L10 29 Z", "M8 17 L11 20 M12 13 L14 15 M16 9 L19 12 M20 5 L22 7"], fill: [] },
  'table': { stroke: ["M3 11 H29 V15 H3 Z", "M7 15 V27 M25 15 V27"], fill: [] },
  'cover': { stroke: ["M16 4 L26 8 V16 C26 22 21 26 16 28 C11 26 6 22 6 16 V8 Z", "M16 9 V23"], fill: [] },
  'difficult': { stroke: ["M3 11 Q7 7 11 11 T19 11 T27 11", "M3 18 Q7 14 11 18 T19 18 T27 18", "M3 25 Q7 21 11 25 T19 25 T27 25"], fill: [] },
  'dangerous': { stroke: ["M3 27 H29", "M5 27 L9 11 L13 27", "M12 27 L16 5 L20 27", "M19 27 L23 13 L27 27"], fill: [] },
  'unstable': { stroke: ["M3 27 H29", "M5 27 V20 H13 V27", "M15 27 V17 H25 V27", "M19 4 L16 9 L20 11 L17 15"], fill: [] },
  'steep': { stroke: ["M3 27 H29 V7 Z", "M8 15 L15 8 M15 8 H10 M15 8 V13"], fill: [] },
  'impassable': { stroke: ["M16 4 A12 12 0 1 1 15.99 4 Z", "M7.5 7.5 L24.5 24.5"], fill: [] },
  'blocks-sight': { stroke: ["M3 16 C8 8 24 8 29 16 C24 24 8 24 3 16 Z", "M16 12 A4 4 0 1 1 15.99 12 Z", "M5 28 L27 4"], fill: [] },
  'see-in-out': { stroke: ["M3 16 C8 8 24 8 29 16 C24 24 8 24 3 16 Z", "M16 12 A4 4 0 1 1 15.99 12 Z"], fill: [] },
  'open-ground': { stroke: ["M3 23 H29", "M7 23 L9 18 M13 23 L14 17 M19 23 L21 18 M25 23 L26 19"], fill: [] },
  'lock': { stroke: ["M8 14 H24 V27 H8 Z", "M11 14 V10 A5 5 0 0 1 21 10 V14", "M16 19 V22"], fill: [] },
  'unlock': { stroke: ["M8 14 H24 V27 H8 Z", "M11 14 V9 A5 5 0 0 1 21 7", "M16 19 V22"], fill: [] },
  'remove': { stroke: ["M6 9 H26", "M13 9 V5 H19 V9", "M9 9 L10 27 H22 L23 9", "M14 13 V23 M18 13 V23"], fill: [] },
  'undo': { stroke: ["M10 7 L5 12 L10 17", "M5 12 H20 A7 7 0 0 1 20 26 H12"], fill: [] },
};
