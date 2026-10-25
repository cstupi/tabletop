// viewer/src/lib/api/names.ts
// TextLimits.Name and NameProblem (client/src/Core/SetupLimits.cs), word for word: management's Limits.Name, checked before a request.

export const NAME = 64;

const bidi = (ch: string): boolean => /[‎‏؜‪-‮⁦-⁩]/.test(ch);
const control = (ch: string): boolean => /[\u0000-\u001F\u007F-\u009F]/.test(ch);

/** Null when management would take the name (as trimmed), else what it would say. */
export function nameProblem(value: string | null, field = 'A name'): string | null {
  const name = value?.trim() ?? '';
  if (name === '') return `${field} is required.`;
  if (name.length > NAME) return `${field} is at most ${NAME} characters.`;
  if ([...name].some((ch) => control(ch) || bidi(ch))) return `${field} contains a control or bidirectional formatting character.`;
  return null;
}
