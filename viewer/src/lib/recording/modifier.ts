import type { RecordedModifier } from '../protocol/recording.generated';

/** `cover +1`, a spell's `-1`; AP as p.13 writes it, `AP(1)`. */
export const modifierText = (m: RecordedModifier): string =>
  m.source === 'AP' ? `AP(${-m.amount})` : `${m.source} ${m.amount >= 0 ? `+${m.amount}` : m.amount}`;
