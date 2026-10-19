import { describe, expect, it } from 'vitest';
import { errorMessage, retryAdvice } from '../src/lib/api/errors';
import { ApiError } from '../src/lib/api/http';
import { formatBytes, libraryUsage, MAX_BYTES, MIB, nameProblem, sizeProblem } from '../src/lib/validation';
import { loginHref, safeNext } from '../src/lib/session/redirect';

const problem = (status: number, detail?: string, title = 'T') => new ApiError(status, title, detail);

describe('errorMessage', () => {
  it('says a network failure is a connection problem', () => {
    expect(errorMessage(new TypeError('Failed to fetch'))).toMatch(/Could not reach the server/);
  });

  it('rewrites management’s name and source details without the wire field', () => {
    expect(errorMessage(problem(422, 'name is at most 64 characters.'))).toBe('Name is too long: at most 64 characters.');
    expect(errorMessage(problem(422, 'name is required.'))).toBe('Name is required.');
    expect(errorMessage(problem(422, 'name contains a control or bidirectional formatting character.'))).toMatch(/invisible control/);
    expect(errorMessage(problem(422, 'source is at most 512 characters.'))).toBe('The Army Forge link is too long: at most 512 characters.');
  });

  it('turns an asset-ownership refusal into what to do', () => {
    expect(errorMessage(problem(422, 'Asset 0190-aa is not an uploaded asset of yours.'))).toMatch(/no longer in your library.*Pick another/);
    expect(errorMessage(problem(422, 'Asset 0190-aa is terrain; this reference takes miniature.'))).toBe('That asset is terrain; this slot takes miniature.');
  });

  it('adds the way out to count and quota limits', () => {
    expect(errorMessage(problem(422, 'An account keeps at most 200 armies.'))).toBe('An account keeps at most 200 armies. Delete some to make room.');
    expect(errorMessage(problem(422, 'An account holds at most 500 assets.'))).toMatch(/500 assets\. Delete some/);
    expect(errorMessage(problem(422, 'An account holds at most 1 GiB of uploads.'))).toMatch(/1 GiB of uploads\. Delete assets/);
    expect(errorMessage(problem(422, 'A miniature file is at most 64 MiB.'))).toBe('A miniature file is at most 64 MiB.');
  });

  it('gives retry guidance on a 429, from Retry-After when there is one', () => {
    expect(errorMessage(problem(429, 'Too many requests. Wait a minute and try again.'))).toBe('Too many requests. Wait a minute and try again.');
    const e = problem(429); e.retryAfter = 30;
    expect(errorMessage(e)).toBe('Too many requests. Try again in 30 seconds.');
    expect(retryAdvice(1)).toBe('Try again in 1 second.');
    expect(retryAdvice(600)).toBe('Try again in 10 minutes.');
  });

  it('reads 401 and 502 by the flow that failed', () => {
    expect(errorMessage(problem(401, 'whatever'), 'auth')).toBe('Wrong email, password or code.');
    expect(errorMessage(problem(401))).toMatch(/session has ended/);
    expect(errorMessage(problem(502, 'Army Forge could not be reached.'), 'import')).toBe('Army Forge could not be reached. Check the link is a public share link and try again.');
    expect(errorMessage(problem(502), 'auth')).toMatch(/sign-in service is unavailable/);
  });

  it('covers 409, 413, 404 and 5xx', () => {
    expect(errorMessage(problem(409, 'The upload was abandoned before it was confirmed; create the asset again.'))).toMatch(/took too long.*Upload the file again/);
    expect(errorMessage(problem(409, 'Only terrain carries volumes.'))).toBe('Only terrain carries volumes.');
    expect(errorMessage(problem(413, 'A request body is at most 1,048,576 bytes.'))).toMatch(/too large/);
    expect(errorMessage(problem(404))).toMatch(/Not found/);
    expect(errorMessage(problem(500, undefined, 'An error occurred'))).toMatch(/server had a problem/);
  });
});

describe('nameProblem', () => {
  it('accepts a trimmed name up to 64 characters', () => {
    expect(nameProblem('  Battle Brothers  ')).toBeNull();
    expect(nameProblem('x'.repeat(64))).toBeNull();
    expect(nameProblem('Ünïcødé ✓')).toBeNull();
  });
  it('refuses empty, long, control and bidi text', () => {
    expect(nameProblem('   ')).toBe('Name is required.');
    expect(nameProblem('x'.repeat(65), 'Folder name')).toBe('Folder name is too long: 65 of at most 64 characters.');
    expect(nameProblem('a\tb')).toMatch(/invisible/);
    expect(nameProblem('a‮b')).toMatch(/invisible/);
    expect(nameProblem('a⁦b')).toMatch(/invisible/);
  });
});

describe('sizeProblem', () => {
  it('holds a mesh to 64 MB and a texture to 16 MB', () => {
    expect(sizeProblem('miniature', MAX_BYTES.miniature)).toBeNull();
    expect(sizeProblem('terrain', 64 * MIB + 1)).toMatch(/A mesh is at most 64 MB; this one is 64 MB\. Lower the triangle budget/);
    expect(sizeProblem('texture', 17 * MIB)).toMatch(/A board texture is at most 16 MB; this one is 17 MB\. Resize/);
  });
  it('formats sizes', () => {
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(2048)).toBe('2 KB');
    expect(formatBytes(1.5 * MIB)).toBe('1.5 MB');
    expect(formatBytes(1024 * MIB)).toBe('1 GB');
  });
});

describe('libraryUsage', () => {
  it('counts every asset and each distinct file once', () => {
    expect(libraryUsage([{ hash: 'a', byteSize: 10 }, { hash: 'a', byteSize: 10 }, { hash: 'b', byteSize: 5 }])).toEqual({ assets: 3, bytes: 15 });
    expect(libraryUsage([])).toEqual({ assets: 0, bytes: 0 });
  });
});

describe('safeNext and loginHref', () => {
  it('keeps a path on this site', () => {
    expect(safeNext('/library/abc?folder=1')).toBe('/library/abc?folder=1');
    expect(safeNext('/armies/x#u')).toBe('/armies/x#u');
    expect(safeNext('/viewer/')).toBe('/viewer/');   // the hosted viewer sends a signed-out reader here and back
  });
  it('refuses other origins, the sign-in pages and nothing', () => {
    for (const bad of [null, '', 'https://evil.example/', '//evil.example/x', '/\\evil.example', 'library', '/login?next=/x', '/reset-password'])
      expect(safeNext(bad)).toBe('/armies');
  });
  it('builds the sign-in link', () => {
    expect(loginHref('/library?folder=f1')).toBe('/login?next=%2Flibrary%3Ffolder%3Df1');
    expect(loginHref('/armies', true)).toBe('/login?expired=1');
    expect(loginHref('/login')).toBe('/login');
  });
});
