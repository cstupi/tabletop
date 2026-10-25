// The calls agreement's normalisation: the same algorithm as tests/Tabletop.Client.Tests/CallNormalise.cs. Nulls drop, keys sort ordinally,
// numbers round to nine places, and an id no log file or case input holds is renamed new:n in the order the canonical value meets it.

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const GUID_INSIDE = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi;

/** Every GUID-shaped string in these JSON texts (lower case). */
export function knownIds(...json: string[]): Set<string> {
  const ids = new Set<string>();
  for (const text of json) for (const m of text.matchAll(GUID_INSIDE)) ids.add(m[0].toLowerCase());
  return ids;
}

const round = (x: number): number => (x === 0 ? 0 : Math.sign(x) * (Math.round(Math.abs(x) * 1e9) / 1e9));

function sorted(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(sorted);
  if (typeof value === 'number') return round(value);
  if (value !== null && typeof value === 'object') {
    const out: Record<string, unknown> = {};
    for (const k of Object.keys(value).sort()) {
      const v = (value as Record<string, unknown>)[k];
      if (v !== null && v !== undefined) out[k] = sorted(v);
    }
    return out;
  }
  return value;
}

/** Keys sorted, nulls dropped, numbers rounded, unknown ids renamed new:n in canonical order. */
export function normaliseCall(message: unknown, known: ReadonlySet<string>): unknown {
  const fresh = new Map<string, string>();
  const rename = (s: string): string => {
    if (!GUID.test(s)) return s;
    const lower = s.toLowerCase();
    if (known.has(lower)) return s;
    let name = fresh.get(lower);
    if (name === undefined) { name = `new:${fresh.size + 1}`; fresh.set(lower, name); }
    return name;
  };
  const walk = (value: unknown): unknown => {
    if (Array.isArray(value)) return value.map(walk);
    if (typeof value === 'string') return rename(value);
    if (value !== null && typeof value === 'object') {
      const out: Record<string, unknown> = {};
      for (const [k, v] of Object.entries(value)) out[rename(k)] = walk(v);
      return out;
    }
    return value;
  };
  return walk(sorted(message));
}

/** JSON.stringify of the value, its keys in the order normaliseCall left them. */
export function canonical(value: unknown): string {
  return JSON.stringify(value);
}
