import type { RecordedAsk, RecordedAskOption } from '../protocol/recording.generated';

export type Addressee = { readonly id: string; readonly label: string; readonly answered: boolean };

export type AskView = {
  readonly id: string;
  readonly heading: string;
  readonly prompt: string;
  readonly addressees: readonly Addressee[];
  readonly waitingOn: readonly string[];
  readonly legal: string;
  readonly notes: readonly string[];
  readonly options: readonly RecordedAskOption[];
  readonly pieces: readonly string[];
  readonly allowance: number | null;
};

export function humanise(question: string): string {
  if (question.length === 0) return '';
  const words = question.split('_').join(' ');
  return words[0]!.toUpperCase() + words.slice(1);
}

const plural = (n: number, one: string, many = `${one}s`): string => `${n} ${n === 1 ? one : many}`;

export function describeLegal(ask: RecordedAsk): string {
  switch (ask.legalKind) {
    case 'options': return plural(ask.options.length, 'option');
    case 'pieces': return `${plural(ask.pieces.length, 'eligible piece')}`;
    case 'allocation': return `${plural(ask.pieces.length, 'target')} to allocate over`;
    case 'assignment': return `${plural(ask.options.length, 'item')} over ${plural(ask.pieces.length, 'target')}`;
    case 'move_region': return `${plural(ask.pieces.length, 'model')} within ${ask.allowance ?? 0}"`;
    // The spend is per model (p.13): the recording carries the spender models in `pieces` and the
    // sum of their maxima in `allowance`, so the line says how much may be spent and by how many models.
    case 'token_spend': return `a token spend of up to ${ask.allowance ?? 0} over ${plural(ask.pieces.length, 'model')}`;
    case 'confirmation': return 'yes or no';
    default: return ask.legalKind;
  }
}

export function describeAsk(ask: RecordedAsk, teamLabels: ReadonlyMap<string, string>): AskView {
  const answered = new Set(ask.answered);
  const addressees = ask.teams.map((id) => ({
    id, label: teamLabels.get(id) ?? `team ${id.slice(0, 8)}`, answered: answered.has(id),
  }));
  const notes: string[] = [];
  if (ask.allowPass) notes.push('a pass is allowed');
  if (ask.sealed) notes.push('sealed — answers stay hidden until it closes');
  if (ask.windowKind !== undefined) notes.push(`reaction window: ${ask.windowKind}`);
  if (ask.frameId !== undefined) notes.push(`in frame ${ask.frameId.slice(0, 8)}`);

  return {
    id: ask.id,
    heading: humanise(ask.question),
    prompt: ask.prompt,
    addressees,
    waitingOn: addressees.filter((a) => !a.answered).map((a) => a.label),
    legal: describeLegal(ask),
    notes,
    options: ask.options,
    pieces: ask.pieces,
    allowance: ask.allowance ?? null,
  };
}
