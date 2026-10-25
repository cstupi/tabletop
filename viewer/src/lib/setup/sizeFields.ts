// viewer/src/lib/setup/sizeFields.ts
// The Table section's Width and Length fields: they follow the draft's size when it changes (another player's Set shows), and keep
// what the player typed otherwise, so leaving one field for the other or for Set never puts the old size back.

export interface Size { readonly width: number; readonly length: number }

export class SizeFields {
  width: number;
  length: number;
  #seen: Size;

  constructor(size: Size) { this.width = size.width; this.length = size.length; this.#seen = size; }

  /** The draft's size as it now is: the fields take it only when it differs from the size last seen. */
  follow(size: Size): void {
    if (size.width === this.#seen.width && size.length === this.#seen.length) return;
    this.#seen = size;
    this.width = size.width;
    this.length = size.length;
  }

  changed(size: Size): boolean { return this.width !== size.width || this.length !== size.length; }
}
