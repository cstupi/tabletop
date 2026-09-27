export const MM_PER_INCH = 25.4;
export const mmToInches = (mm: number) => mm / MM_PER_INCH;
export const inchesToMm = (inches: number) => inches * MM_PER_INCH;
export const round = (n: number, places = 3) => Math.round(n * 10 ** places) / 10 ** places;
