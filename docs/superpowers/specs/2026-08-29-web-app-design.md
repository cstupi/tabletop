# The web app, and the line between preparation and play

Design session, 2026-08-29. Whether a web app exists, what it is for, and where login
lives.

Eighth module, and the last loose end. It reopens two decisions from
[accounts](2026-08-29-accounts-design.md) — that login and the game browser belong in the
client, and that the web app's existence was undecided — and settles both.

## The line is preparation versus play

**The browser is where a user prepares. The client is where they play.**

| Web app | Client |
| --- | --- |
| Army import and editing | Create a game, pick an army |
| Mesh binding per slot, base shape and height | The game browser, joining |
| Asset upload, folders, the library | The setup phase: table, terrain, markers |
| Account settings | Playing |

The client does not edit armies. It creates a game and picks one.

This is a cleaner seam than the one the previous session drew. It splits on what a user is
doing rather than on which technology is convenient, and each side ends up with one job.

## Why this rather than everything in the client

The case that had built up for a client-only product was that two of the web app's three
remaining jobs — the asset library and mesh binding — are about **looking at a model**, and
the client already has a renderer. That argument is real but not decisive: a glTF viewer on
the web is a component, not an engine. `<model-viewer>` is a script tag; three.js has a
loader.

Against it: form-heavy CRUD is genuinely worse in Godot than in a browser, iteration on the
client is slower, and army editing is where a user spends time before they ever load a
game. Keeping that out of the client keeps the client about the table.

**The rejected alternative was an embedded webview** — gdcef, godot-webview, godot_wry —
serving one shared UI in both places. It was rejected because it does not remove a UI stack,
it adds a bridge to one: a browser engine bundled into a game client, plus a second renderer
inside it for mesh preview, to avoid writing `Control` nodes.

That verdict is narrow. A small OS-native webview used **only to catch an auth redirect** is
a different thing and stays on the table as one of the mechanisms below.

## Login is the web app's

*Corrected 2026-09-04 by [the client shell](2026-09-04-client-shell-design.md): **there is no
social login and no magic link**, so nothing below that exists to serve a redirect survives. Login
is email plus password, or email plus a one-time code, through management. The web app has that
form and **the client has its own**; neither needs the other. Loopback and device code are gone.*

**Reversed from [accounts](2026-08-29-accounts-design.md)**, which put login in the client
and routed OTP and password through management, on the reasoning that no browser existed to
host it. One does now, and it is where a user starts — you cannot play without an army, and
armies are made in the browser.

So the browser owns login, and the client acquires a session from it. This restores
`architecture.md`'s original position, which the previous session had overturned.

**What that changes about Stytch, and what it does not.** Confinement still holds: Stytch
answers only "is this session valid, and whose," `external_id` holds the account's GUID, and
no Stytch identifier is stored in our schema. What changes is that the browser makes the two
methods that need a redirect ordinary:

- `POST /v1/otps/email/login_or_create` and `POST /v1/passwords/authenticate` are Basic-auth
  endpoints requiring the project secret. They **cannot** be called from a browser, so they
  run server-side in management either way.
- `/v1/public/oauth/<provider>/start` is client-side only, authenticates with the public
  token, and redirects to a pre-registered URL. Magic links land on a redirect URL the same
  way. Both need a page to come back to, which is what the web app supplies.

### Getting the session into the client

The server is hosted remotely; the browser and the client are both on the user's machine.
Two mechanisms work, and neither touches the data model.

- **Loopback.** The client listens on `http://127.0.0.1:<port>`, opens the browser, and the
  web app hands the session back after login. The handoff must be a **top-level navigation,
  not a fetch** — an HTTPS page fetching plain HTTP localhost trips mixed-content blocking,
  while navigating to loopback does not, since it counts as a trustworthy origin.
- **Device code.** The client shows a short code, the user confirms it in the browser, and
  the client polls management until it flips. No listener, no redirect registration, and it
  works when the browser is on a different machine — which a remotely hosted server makes an
  ordinary case rather than an edge one.

Both are deferred. Nothing in the model depends on which is chosen.

## What this does not change

- **The game browser and joining stay in the client.** Games are public, listed in the
  client — all as [accounts](2026-08-29-accounts-design.md) has it.
- **`Account` is still id and name.** No new fields.
- **Management is still the only thing that talks to Stytch.** It no longer issues a token of
  its own — the Stytch session JWT carries our account UUID and both services verify it
  locally.
- **The asset library, the army, and the setup content are unchanged.** This decides where
  their screens live, not what they are.

## Deliberately not decided

- ~~Which of loopback or device code is used.~~ **Settled 2026-09-04**: neither. No redirect
  method exists, so nothing has to cross from the browser to the client.
- ~~Whether the client keeps a login of its own as a fallback when no browser is available.~~
  **Settled 2026-09-04**: the client has its own login screen, and it is not a fallback.
- The web app's own screens, routing, and framework details beyond it being a Svelte SPA.
- How a glTF preview is embedded — `<model-viewer>`, three.js, or otherwise.

## Still open

- Whether the client ever needs to edit an army rather than only pick one. A user who
  notices the wrong mesh while loading into a game currently has to go back to the browser.
