# Shared models

Design session, 2026-10-25. One player offers models from their library to a named player, who accepts a copy into their
own library or declines.

Owner: "We also need a share models option so one player can let other users use their models in their own army.
Player A uploads models and Player B can copy them over to their model collection and use them if they want."

Owner: "when you click share.. what actually happens. i didnt get asked to specify what user to share it with. This is an
attack vector if it just pushes files into other people that they could load. someone needs to accept the shared files
and it shouldnt just go to EVERYONE."

This replaces one statement of [the asset library](2026-08-28-asset-library-design.md): "nobody shares storage with
anybody else" (an accepted copy names the sharer's object in S3). "Nobody can browse or search another account's assets"
stands: **nothing is public**. An offered asset is visible to its recipients while the offer is open, and to no one else.

## Offers

An offer is a row: sharer, recipient, the offered assets, the folder's name when a folder was offered, created, state
(`open`, `accepted`, `declined`, `withdrawn`), decided. One request offers the same assets to 1 to 20 recipients and makes
one offer per recipient.

- **What:** 1 to `Limits.Bulk` of the sharer's uploaded assets, or one folder: the uploaded assets in it and its
  sub-folders at that moment. The set is fixed when the offer is made; an asset filed there later is not in it, and an
  asset the sharer deletes leaves it. Any kind (miniature, terrain, texture).
- **Whom:** a player the sharer has been in a game with: an account holding a participant row in a game the sharer holds
  one in, at any status. `GET /v1/offers/players` lists them by display name, each with up to three of the games played
  together (newest first) and the newest one's date, which tells two players of the same name apart. Anyone else is 404.

  No lookup by email. Emails live only in Stytch, so a lookup is a Stytch user search per request, and its time and its
  failures differ between an existing and a missing account however the response is worded. Playing a game first costs
  little: the games list is public and anyone can join a lobby. A player who wants models from a stranger joins one of the
  stranger's games.
- **Seeing it:** the recipient sees an open offer while one offered asset remains and they have not blocked the sharer;
  the sharer sees their offers in every state. Every route answers 404 to anyone else, so a refusal says nothing about
  whether an offer or an asset exists.

## Answering

- **Accept** `{ folderId? }`: one transaction, as the copy of [Copying](#copying) below, then the offer is `accepted`.
  Nothing reaches the recipient's library, an army or a game without Accept.
- **Decline** `{ block }`: the offer is `declined`; nothing is written to the recipient's library. With `block`, every
  open offer from that sharer to the recipient is declined too, and the sharer is blocked.
- **Block:** a blocked sharer's offers, existing and new, are hidden from the recipient. The sharer is not told: a new
  offer is made as usual and stays `open` on the sharer's side. `GET /v1/offers/blocked` lists the blocked;
  `DELETE /v1/offers/blocked/{accountId}` unblocks, and the hidden open offers show again.
- **Withdraw** (sharer, open only, else 409): the offer is `withdrawn`. Accepted copies are the recipient's and stay when
  the sharer withdraws, deletes the asset or deletes the account.

Previews: `GET /v1/offers/{id}/assets/{assetId}/download` presigns the bytes (60 min) for the recipient of an open offer
naming the asset; the sharer downloads their own asset through `/v1/assets/{id}/download`.

## Limits

- 50 offers per sharer in any 24 hours (one per recipient); over it is 429 with `Retry-After` until the oldest of them is a
  day old. The account row lock serializes a sharer's offers.
- 20 open offers a recipient can see; an offer to a recipient at the cap is 422. Blocking a sharer frees their slots.
- The per-account and per-IP request limits apply as to every route.

## Copying

Accept in one transaction, for each offered asset still uploaded:

- the recipient gets an asset row of their own: the source's name, kind, scale, orientation and modelled base, in no
  folder but `folderId`; a terrain source's volumes are copied as new `asset_volume` rows.
- the recipient gets a blob row for the hash unless they already have an uploaded one. Its `s3_key` is the source blob's
  key, so no bytes are copied.
- a pending (not uploaded) blob row of the recipient's for the same hash carries a declared, unverified size and content
  type. It takes the source's key, size and content type and becomes uploaded. If one of the recipient's assets on it is
  of a kind those bytes do not fit (a pending texture on a mesh's hash), Accept is 409 and nothing is written.

`folderId` must be the recipient's folder (else 404). The quota is the upload quota (500 assets, 1 GiB) over the
recipient's blob rows, so an accepted 20 MB mesh counts 20 MB against the recipient; a hash they already have uploaded adds
nothing, and a pending row on a source hash counts at the source's size. Over quota is 422, nothing is written and the
offer stays open.

After Accept the asset is the recipient's in every way: renamed, rescaled, filed, offered on, deleted, assigned to army
slots and layouts, listed in a game's manifest, downloaded by the Godot client (which runs `GlbPreflight` on every GLB it
loads) and the browser. Nothing records where it came from.

## Storage: a key is referenced by blob rows

Blob rows are per owner, `(owner_id, hash)`, and each names an S3 key. An accepted copy's row names the sharer's. So an
S3 object is deleted only when **no blob row names its key**. Every path that deletes a content object goes through that
check:

- deleting an asset (single and bulk) when it was the owner's last asset on the blob;
- the upload sweep, whose abandoned blob row may name a key a copy still names;
- a confirm that lost its rows to the sweep;
- deleting an account: its blob rows cascade, and only the keys no other account's row names are deleted.

Every presign reads `blob.s3_key`. Accept holds a `for share` lock on the source asset and blob rows while it inserts, so
a concurrent delete of the source either finishes first (the asset has left the offer) or waits and then sees the new row
naming the key. Accept also locks the offer row, so a second Accept, a Decline or a Withdraw waits and then finds it no
longer open.

## Schema

Management's `021_share_offers.sql`: `share_offer`, `share_offer_asset` (cascading from the offer and from the asset),
`share_block`; all cascade from `account`. It drops `asset.shared_at` and its index: an asset shared with everyone before
is offered to no one, and nothing stays public. `blob_s3_key_idx` stays.

## Endpoints

| Route | Who | Answer |
| --- | --- | --- |
| `GET /v1/offers/players` | caller | `PlayerDto[]` |
| `POST /v1/offers` `{ recipientIds, assetIds \| folderId }` | sharer | `{ offers, notFound }`; 404 recipient or folder, 422, 429 |
| `GET /v1/offers/sent`, `/received` (keyset, newest first) | sharer, recipient | `Page<OfferDto>` |
| `GET /v1/offers/received/count` | recipient | `{ open }` |
| `GET /v1/offers/{id}` | sharer, recipient | `OfferDto` |
| `GET /v1/offers/{id}/assets/{assetId}/download` | recipient, open | `DownloadResponse` |
| `POST /v1/offers/{id}/accept` `{ folderId? }` | recipient, open | `{ copied: AssetDto[] }`; 404, 409, 422 |
| `POST /v1/offers/{id}/decline` `{ block }` | recipient, open | 204 |
| `POST /v1/offers/{id}/withdraw` | sharer | 204; 409 when not open |
| `GET /v1/offers/blocked`, `DELETE /v1/offers/blocked/{accountId}` | recipient | `BlockedDto[]`, 204 |

`OfferDto`: id, state, sharer and recipient (id, name), folder, created, decided, and the offered assets the sharer
still has (name, kind, hash, size, content type, scale, orientation, modelled base).

## Web app

- Library: the selection bar's **Offer…** and, with a folder open, **Offer folder…** open the offer dialog; the asset page
  has **Offer to a player…**. The dialog lists the players with their games and dates (a name filter past eight), ticks
  any number and sends.
- **Offered to you** (`/library/offered`, the tab carries the open count): per offer the sharer, date, folder, models,
  sizes and *in your library* for a hash already held; **Preview** shows the mesh or texture; **Into folder**, **Accept**,
  **Decline**, **Block** *name*. Blocked players are listed below with **Unblock**.
- **Sent** (`/library/sent`): recipient, what, date, state (Waiting, Accepted, Declined, Withdrawn) and **Withdraw** on a
  waiting offer.

## Not done

- Notifying a recipient outside the website (email, the Godot client).
- Telling a recipient that a source changed. A copy is a snapshot.
