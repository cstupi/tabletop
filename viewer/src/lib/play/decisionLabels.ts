// viewer/src/lib/play/decisionLabels.ts
// How the decision box lays out a button's label: the number key that leads it ("2. Assault Brothers") and the key that trails it
// ("Commit move (Enter)") are drawn as keys beside the words.

export interface LabelParts { readonly digit: string | null; readonly text: string; readonly key: string | null }

const LEADING = /^(\d)\. (.+)$/s;
const TRAILING = /^(.+) \((Enter|Esc|C|Ctrl\+Z|[A-Z])\)$/s;

export function labelParts(label: string): LabelParts {
  let text = label;
  let digit: string | null = null;
  let key: string | null = null;
  const lead = LEADING.exec(text);
  if (lead !== null) { digit = lead[1]!; text = lead[2]!; }
  const trail = TRAILING.exec(text);
  if (trail !== null) { text = trail[1]!; key = trail[2]!; }
  return { digit, text, key };
}
