export const MM_PER_INCH = 25.4;
export const mmToInches = (mm: number) => mm / MM_PER_INCH;
export const inchesToMm = (inches: number) => inches * MM_PER_INCH;
export const round = (n: number, places = 3) => Math.round(n * 10 ** places) / 10 ** places;

/** Inches per model unit for each unit a mesh can be made in. glTF's unit is the metre. */
export const UNIT_SCALES = { mm: 1 / MM_PER_INCH, cm: 10 / MM_PER_INCH, in: 1, m: 1000 / MM_PER_INCH } as const;
export type MeshUnit = keyof typeof UNIT_SCALES;
