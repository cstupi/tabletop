# Shared folders

Design session, 2026-11-07. A player keeps a Shared folder in their library; any signed-in player who types that player's
username sees the folder's tree and copies models or whole sub-folders from it into their own library.

Owner: "handle better sharing of models between users. Allow users to have a shared folder where other users can enter their
username and copy models into their own folders (you can organize the shared folder with more subfolders so users can copy
entire subfolders)."

This is a later instruction than [shared models](2026-10-25-shared-models-design.md)' "it shouldnt just go to EVERYONE", and
narrows its "nothing is public": a player's Shared folder is readable by every signed-in player who knows the username.
Nothing outside a Shared folder becomes visible. Offers to a named player stay as they are. Nothing reaches a library
without the copier's own Copy.

## Username

The display name (`account.name`) is not unique: two players may both be "Clay", and the default `Player-xxxx` can repeat.
A lookup needs a unique, exact key, so an account gets a **username** of its own, apart from the display name:

- `account.username`, nullable, 3 to 32 of `A–Z a–z 0–9 _ -`, unique case-insensitively (`unique (lower(username))`).
  The stored spelling is the one the player typed; a lookup matches it ignoring case.
- `PUT /v1/account/username` `{ username }` sets it (`null` clears it): 200 `AccountRow`; 422 for the format; 409
  "That username is taken." for another account's username in any case.
- An account without one cannot be looked up. No migration renames anyone: existing accounts start without a username.
- No listing or search of accounts. A lookup is one exact username; an unknown username and an account without a Shared
  folder answer the same 404, so a refusal tells only that nothing is shared under that name. Guessing usernames costs requests against the per-account and per-IP limits.

The display name stays what games, offers and the shared view show; the shared view shows both.

## The Shared folder

A flag on the existing folder model: `folder.shared`, true on at most one folder per account (`unique (owner_id) where
shared`), and only on a top-level folder (`check (not shared or parent_id is null)`).

- `POST /v1/folders/shared` makes the caller's Shared folder, named `Shared`, or answers the one they have: 200 `FolderDto`.
  It counts against the 200-folder cap like any folder (422 at the cap).
- It is a folder in every other way: renamed, given sub-folders, assets filed into it and its sub-folders (an asset can sit
  in several folders, so filing a model there leaves it where it was too), deleted (its sub-folders go, assets stay; making
  it again starts empty).
- It cannot be moved under another folder: `PATCH` with a `parentId` is 422. Other folders move into it and out of it as
  usual, and what moves in becomes shared.
- `FolderDto` carries `shared`.

What is shared is the Shared folder's subtree at the moment of each read: its folders and the uploaded assets filed in
them. An asset filed there and elsewhere is shared; unfiling it from the subtree stops sharing it.

## Browsing

- `GET /v1/shared/{username}`: `SharedLibraryDto` — the owner's username (as stored) and display name; `folders`: the
  subtree's folders (id, parent id, name, the ids of the shared assets filed in it; the Shared folder's parent id is null);
  `assets`: each shared uploaded asset (id, name, kind, hash, size, content type, scale, orientation, modelled base).
  An asset in no folder of the subtree, a pending upload and every folder outside the subtree are absent. 404 as above.
- `GET /v1/shared/{username}/assets/{assetId}/download`: a presigned GET (60 min) of a shared asset, for the preview. 404
  for any asset not shared right now.
- A player browsing their own username sees their Shared folder as anyone does.

## Copying

`POST /v1/shared/{username}/copy` `{ assetIds | folderId, destinationId? }`, exactly one of `assetIds` and `folderId`:

- `assetIds`: 1 to `Limits.Bulk` (200) ids; the shared ones are copied, the rest come back in `notFound`.
- `folderId`: a folder of the subtree (the Shared folder itself included). Its tree is copied: a new folder of the copier's
  with the source's name under `destinationId` (top level without one), a new folder per sub-folder below it, each copied
  asset filed in the copy of every folder of the tree it was filed in. A copied folder is never shared.
- `destinationId`: a folder of the copier's (else 404). With `assetIds` the copies are filed in it; without, in no folder.
- Each copy is the offers' Accept copy ([shared models → Copying](2026-10-25-shared-models-design.md#copying)): an asset
  row of the copier's with the source's name, kind, scale, orientation, modelled base and terrain volumes; a blob row naming
  the sharer's S3 key unless the copier holds the hash uploaded; a pending row of the copier's on the hash takes the source's
  key, size and content type (409 if a pending asset on it is of a kind those bytes do not fit). It is the copier's in every
  way and stays when the sharer unfiles, deletes, clears their username or deletes the account. Nothing records where it
  came from.
- Whole or nothing, one transaction, under the copier's account-row lock:
  - the upload quota (500 assets, 1 GiB) over all the copies at once: 422 "An account holds at most 500 assets." / "… 1 GiB
    of uploads.";
  - at most `Limits.Bulk` (200) assets in one copy: 422 "A copy takes at most 200 models. Copy a sub-folder.";
  - the copier's folders plus the new ones within 200: 422 "An account holds at most 200 folders.";
  - nothing shared to copy (every id missing, or an empty tree): 422 "Nothing to copy: the folder holds no uploaded models.";
  - copying from one's own username: 422 "That is your own Shared folder."
- 50 copies per copier in any 24 hours (one per request, recorded in `shared_copy`): over it is 429 with `Retry-After`
  until the oldest is a day old, as offers count theirs.
- The source rows are read `for share`, so a sharer's concurrent delete either finishes first (the asset is no longer
  shared) or waits and then sees the copy's blob row naming the key.
- 200 `CopySharedResponse`: `assets` (the new asset ids, in source order), `folders` (the new folders, `FolderDto`, the
  copied tree's root first), `notFound`.

Storage is unchanged from offers: an S3 object is deleted only when no blob row names its key.

## Schema

Management's `029_shared_folders.sql`: `account.username` with its format check and `account_username_idx` on
`lower(username)`; `folder.shared` (default false), `folder_shared_top_level` check, `folder_shared_idx` partial unique on
`owner_id`; `shared_copy` (`id`, `copier_id` cascading from `account`, `created_at`) with `(copier_id, created_at)`.

## Endpoints

| Route | Who | Answer |
| --- | --- | --- |
| `PUT /v1/account/username` `{ username }` | caller | `AccountRow`; 409, 422 |
| `POST /v1/folders/shared` | caller | `FolderDto`; 422 at the folder cap |
| `GET /v1/shared/{username}` | any signed-in | `SharedLibraryDto`; 404 |
| `GET /v1/shared/{username}/assets/{assetId}/download` | any signed-in | `DownloadResponse`; 404 |
| `POST /v1/shared/{username}/copy` `{ assetIds \| folderId, destinationId? }` | any signed-in but the owner | `CopySharedResponse`; 404, 409, 422, 429 |

`AccountRow` gains `username` (null when unset).

## Web app

- Account page: **Username** — the field, **Save username**, the fact "Players type your username to open your Shared
  folder." and the format.
- Library tree: the Shared folder sits first with a shared mark and no Move tool; without one, **Make a Shared folder**.
  Under it: "Players open it with your username, *name*." or, with no username, "Set a username on the Account page to
  share it." Filing models and moving folders into it are the library's existing add/move.
- **Shared by players** tab (`/library/shared?user=<username>`): a **Username** field and **Open**; the owner's display
  name; their folder tree; the selected folder's models with **Preview** (mesh or texture), tick boxes and **Copy
  selected**; **Copy folder** on the folder in view; **Into folder** (the copier's folders, or no folder) for both. A
  result says what was added and where; a failure is the server's sentence.

## Not done

- Telling a sharer who copied what.
- Hiding a sharer's folder from one copier (offers' block does not apply: the copier chooses to open it).
