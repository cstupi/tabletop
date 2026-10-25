// Lobby to playing, from the browser modules, against the running stack (docker compose up -d). It signs in three dev accounts and
// goes through the same modules the pages use: connectGame, the Api, placeBuiltin, SetupDrag, randomRequest/randomEdit, loadEdit.
// Skipped unless TABLETOP_E2E=1, so `npm test` stays offline. Run: npm run test:e2e (after ./scripts/geometry-wasm).

import { beforeAll, describe, expect, it } from 'vitest';
import { apiOf } from '../../src/lib/api/client';
import { connectGame, type ConnectionState, type GameConnection } from '../../src/lib/connection/gameConnection';
import { liveFeed } from '../../src/lib/feed/liveFeed';
import type { Geometry } from '../../src/lib/geometry';
import type { Edit, TableState } from '../../src/lib/protocol/wire.generated';
import { watchUrl } from '../../src/lib/shell/route';
import { loadCatalog, placeBuiltin, type Catalog } from '../../src/lib/setup/catalog';
import { SetupDrag } from '../../src/lib/setup/drag';
import { objective, overrideVolume, withDeclaration, zoneShapes } from '../../src/lib/setup/edits';
import { loadEdit, randomEdit, randomRequest } from '../../src/lib/setup/layouts';
import { loadTestGeometry } from '../geometry';
import { MANAGEMENT, SOCKET, accountName, session, sweep, waitFor } from './harness';

describe.skipIf(process.env.TABLETOP_E2E !== '1')('browser play, lobby to playing', () => {
  let geometry: Geometry;
  let catalog: Catalog;
  beforeAll(async () => { geometry = await loadTestGeometry(); }, 60_000);

  it('a game goes from lobby to playing from the browser modules', async () => {
    const jwts = await Promise.all(['a', 'b', 'c'].map((seat) => session(accountName('c2', seat))));
    const [jwtA, jwtB, jwtC] = [jwts[0]!, jwts[1]!, jwts[2]!];
    const apiA = apiOf(async () => jwtA, MANAGEMENT);
    const apiB = apiOf(async () => jwtB, MANAGEMENT);
    const name = 'viewer e2e layout';
    const removeLayouts = async () => {
      for (const l of await apiA.listLayouts().catch(() => [])) if (l.name === name) await apiA.deleteLayout(l.id).catch(() => undefined);
    };
    await Promise.all([sweep(jwtA), sweep(jwtB), removeLayouts()]);
    const conns: GameConnection[] = [];
    try {
      catalog = await loadCatalog(apiA);
      const properties = (await apiA.listProperties())[0]!;
      const created = await apiA.createGame({
        name: 'c2 e2e', propertiesHash: properties.contentHash, schedulerShape: 'alternating-activation',
        enforcement: 'warn', waitsBetweenSteps: false,
      });
      await apiB.joinGame(created.id);

      const a = connectGame({ url: SOCKET, gameId: created.id, jwt: async () => jwtA });
      const b = connectGame({ url: SOCKET, gameId: created.id, jwt: async () => jwtB });
      conns.push(a, b);
      await Promise.all([waitFor(a, (s) => s.phase === 'connected', 'A connected'), waitFor(b, (s) => s.phase === 'connected', 'B connected')]);

      const sendOk = async (conn: GameConnection, edit: Edit) => expect((await conn.sendEdit(edit)).refused).toBeNull();
      const refusal = async (conn: GameConnection, edit: Edit) => {
        // A broadcast from another player's edit can settle the send before its own Refused: the Refused by id is the result.
        const outcome = await conn.sendEdit(edit);
        if (outcome.refused !== null) return outcome.refused.reason;
        return new Promise<string | undefined>((resolve) => {
          const stop = conn.onRefused((r) => { if (r.messageId === outcome.id) { stop(); resolve(r.reason); } });
          setTimeout(() => { stop(); resolve(undefined); }, 1000);
        });
      };
      const teams = (s: ConnectionState) => s.game?.teams ?? [];

      while (teams(a.state).length < 2) await sendOk(a, { kind: 'create_team' });
      await waitFor(b, (s) => teams(s).length >= 2, 'B sees two teams');
      const [t1, t2] = [...teams(a.state)].sort((x, y) => x.ordinal - y.ordinal);
      await sendOk(a, { kind: 'pick_team', teamId: t1!.id });
      await sendOk(b, { kind: 'pick_team', teamId: t2!.id });
      const held = (s: ConnectionState, who: GameConnection) => s.game?.participants.find((p) => p.id === who.state.participantId)?.teamId;
      await waitFor(a, (s) => held(s, a) === t1!.id && held(s, b) === t2!.id, 'both seated');

      expect(await refusal(b, { kind: 'start_setup' })).toBe('not_permitted');
      await sendOk(a, { kind: 'start_setup' });
      await Promise.all([
        waitFor(a, (s) => s.game?.status === 'setup' && s.draft !== null, 'A in setup'),
        waitFor(b, (s) => s.game?.status === 'setup' && s.draft !== null, 'B in setup'),
      ]);

      await sendOk(a, { kind: 'set_table_size', size: { width: 60, length: 44 } });
      await waitFor(b, (s) => s.draft?.size.width === 60 && s.draft.size.length === 44, 'B sees 60×44');

      // Two large sight blockers beside the table; the second dragged onto the first lands clear of it.
      const blocker = catalog.terrain.get('large-sight-blocker')!;
      const first = placeBuiltin(blocker, { x: 68, y: 4, z: 0 }, { degrees: 0 });
      const second = placeBuiltin(blocker, { x: 68, y: 12, z: 0 }, { degrees: 0 });
      await sendOk(a, { kind: 'add_terrain', placement: first });
      await sendOk(a, { kind: 'add_terrain', placement: second });
      const drag = new SetupDrag(geometry);
      const draft = a.state.draft!;
      expect(drag.start(draft, { kind: 'terrain', id: second.id, volumeId: second.volumes[0]!.id }, { x: 68, y: 12 })).toBe(true);
      drag.move(draft, { x: 68, y: 4 });
      const drop = drag.end(draft);
      expect(drop?.kind).toBe('move_terrain');
      await sendOk(a, drop!);
      const landed = await waitFor(b, (s) => {
        const p = s.draft?.terrain.find((t) => t.id === second.id);
        return p !== undefined && (p.position.x !== 68 || p.position.y !== 12);
      }, 'B sees the drop');
      const at = landed.draft!.terrain.find((t) => t.id === second.id)!.position;
      expect(Math.max(Math.abs(at.x - 68), Math.abs(at.y - 4))).toBeGreaterThanOrEqual(6 - 1e-6);

      // Scale, a declaration, an objective, the zones: B's draft agrees on each.
      await sendOk(a, { kind: 'scale_terrain', placementId: first.id, scale: 2 });
      const withDifficult = overrideVolume(first, first.volumes[0]!.id, withDeclaration('difficult', true));
      await sendOk(a, withDifficult);
      const marker = objective({ width: 60, length: 44 }, []);
      await sendOk(a, marker);
      await sendOk(a, { kind: 'set_zone_style', style: 'spearhead', shapes: zoneShapes('spearhead') });
      const agreed = await waitFor(b, (s) => {
        const p = s.draft?.terrain.find((t) => t.id === first.id);
        return p?.scale === 2 && p.volumes[0]!.volume.declarations.includes('difficult')
          && s.draft!.markers.some((m) => m.id === marker.marker.id) && s.draft!.zones.some((z) => z.shape === 'spearhead/1');
      }, 'B agrees on scale, declaration, marker and zones');
      expect(agreed.draft!.zones.map((z) => z.shape).sort()).toEqual(['spearhead/1', 'spearhead/2']);

      // The random table: the server lays it; the objective and the spearhead zones stay.
      const answer = await apiA.randomTable(randomRequest(a.state.draft!, 'hospitable'));
      expect(answer.terrain.length).toBeGreaterThan(0);
      await sendOk(a, randomEdit(a.state.draft!, answer));
      // The server gives a replaced draft's pieces ids of its own, so the terrain is compared by what and where, not by id.
      const key = (t: { mesh: { builtin?: string }; position: { x: number; y: number } }) => `${t.mesh.builtin}@${t.position.x},${t.position.y}`;
      const random = await waitFor(b, (s) => s.draft?.terrain.length === answer.terrain.length
        && answer.terrain.map(key).sort().join('|') === s.draft.terrain.map(key).sort().join('|'), 'B has the random terrain');
      expect(random.draft!.markers.filter((m) => m.kind === 'objective' && m.position.x === marker.marker.position.x && m.position.y === marker.marker.position.y)).toHaveLength(1);
      expect(random.draft!.zones.map((z) => z.shape).sort()).toEqual(['spearhead/1', 'spearhead/2']);

      // Save as, then delete: the list lacks it.
      await apiA.saveLayout({ ...a.state.draft!, name });
      const listed = await apiA.listLayouts();
      const row = listed.find((l) => l.name === name);
      expect(row).toBeDefined();
      await apiA.deleteLayout(row!.id);
      expect((await apiA.listLayouts()).some((l) => l.name === name)).toBe(false);

      // The Death table replaces the whole draft, size included.
      const tables = await apiA.builtinLayouts();
      const death: TableState = tables[2]!;
      await sendOk(a, loadEdit(death));
      for (const conn of [a, b]) {
        const s = await waitFor(conn, (x) => x.draft?.id === death.id || (x.draft?.size.width === 72 && x.draft.size.length === 48 && x.draft.markers.length === 4), 'the Death table');
        expect(s.draft!.size).toEqual({ width: 72, length: 48 });
        expect(s.draft!.markers.filter((m) => m.kind === 'objective')).toHaveLength(4);
      }

      expect(await refusal(b, { kind: 'end_setup' })).toBe('not_permitted');
      await sendOk(a, { kind: 'end_setup' });
      await Promise.all([waitFor(a, (s) => s.game?.status === 'playing', 'A playing'), waitFor(b, (s) => s.game?.status === 'playing', 'B playing')]);

      // A stranger is turned away; B's replay socket opens while B's play socket stays connected.
      const c = connectGame({ url: SOCKET, gameId: created.id, jwt: async () => jwtC });
      conns.push(c);
      const closed = await waitFor(c, (s) => s.phase === 'closed', 'C closed');
      expect(closed.closeReason).toBe('You are not in that game: join it from the list.');

      const feed = liveFeed({ url: watchUrl(SOCKET), gameId: created.id, sessionJwt: jwtB, label: 'replay' });
      let status = '';
      const stop = feed.subscribe((s) => { status = s.status; });
      for (let i = 0; i < 100 && status !== 'open'; i++) await new Promise((r) => setTimeout(r, 100));
      expect(status).toBe('open');
      expect(b.state.phase).toBe('connected');
      stop();
      feed.close();
    } finally {
      for (const conn of conns) conn.close();
      await Promise.all([sweep(jwtA), sweep(jwtB), removeLayouts()]);
    }
  }, 90_000);
});
