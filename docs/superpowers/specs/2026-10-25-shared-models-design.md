# Shared models

Design session, 2026-10-25. One player lets every other player copy assets from their library into their own.

Owner: "We also need a share models option so one player can let other users use their models in their own army.
Player A uploads models and Player B can copy them over to their model collection and use them if they want."

This replaces two statements of [the asset library](2026-08-28-asset-library-design.md): "nobody can browse or search
another account's assets" (a shared asset is browsable by every signed-in account) and "nobody shares storage with
anybody else" (a copy names the sharer's object in S3). Everything else there stands: a copy is an ordinary asset of
its new owner, and nothing points across accounts in the rows the rest of the system reads.

## Sharing

- `asset.shared_at timestamptz null`. Null is not shared. Any kind is shareable: miniatures, terrain and textures go
  through the same upload, the same rows and the same download, so they share the same way.
- The owner shares and unshares on the website, one asset at a time from its page, many from the library's selection,
  or a whole folder: **Share folder** shares every asset in the folder and its sub-folders at that moment (what the
  library shows for that folder). It is not a property of the folder: an asset filed there later is not shared until
  the owner shares it.
- A shared asset is visible to every signed-in account, as the games list is. Unsharing removes it from the shared
  listing at once; copies already made are their owners' and stay.
- Only an uploaded asset (its blob confirmed) is listed or copied.

## Browsing

`GET /v1/shared-assets` lists every account's shared, uploaded assets except the caller's own, keyset by id like every
list. Each item carries the asset's name, kind, size, scale, orientation and modelled base, the owner's display name
(`account.name`) and the names of the owner's folders holding it. The library has no tags; folders are its grouping,
so the shared view shows the owner's folder names on each item and searches them.

- `q`: a case-insensitive substring of the asset name, the owner's name or one of those folder names.
- `kind`: `miniature`, `terrain` or `texture`, as `GET /v1/assets`.

`GET /v1/shared-assets/{id}` answers one item, and `GET /v1/shared-assets/{id}/download` presigns its bytes for the
preview, as `/v1/assets/{id}/download` does for one's own. Not shared, not uploaded or the caller's own is 404.

## Copying

`POST /v1/shared-assets/copy` `{ assetIds, folderId? }` (1 to `Limits.Bulk` ids) answers
`{ copied: AssetDto[], notFound: Guid[] }`. In one transaction, for each id that is a shared, uploaded asset of another
account:

- the caller gets an asset row of their own: the source's name, kind, scale, orientation and modelled base, unshared,
  in no folder but `folderId`; a terrain source's volumes are copied as new `asset_volume` rows.
- the caller gets a blob row for the hash unless they already have an uploaded one. Its `s3_key` is the source blob's
  key, so no bytes are copied.
- a pending (not uploaded) blob row of the caller's for the same hash carries a declared, unverified size and content
  type. It takes the source's key, size and content type and becomes uploaded. If one of the caller's assets on it is of
  a kind those bytes do not fit (a pending texture on a mesh's hash), the copy is 409 and nothing is written.

`folderId` must be the caller's folder (else 404). The quota is the upload quota (500 assets, 1 GiB) over the caller's
blob rows, so a copied 20 MB mesh counts 20 MB against the copier; a hash the caller already has uploaded adds nothing,
and a pending row on a source hash counts at the source's size, not its declared one. Over quota is 422 and nothing is
written.

After the copy the asset is the copier's in every way: renamed, rescaled, filed, shared, deleted, assigned to army
slots and layouts (`AssetReferences` checks the caller's own uploaded assets, which a copy is), listed in a game's
manifest for the other participants, downloaded by the Godot client and the browser. Nothing records where it came
from.

## Storage: a key is referenced by blob rows

Blob rows are per owner, `(owner_id, hash)`, and each names an S3 key. Before this, every row named its own
`{owner}/{hash}` key; now a copied row names the sharer's. So an S3 object is deleted only when **no blob row names its
key**. Every path that deletes a content object goes through that check:

- deleting an asset (single and bulk) when it was the owner's last asset on the blob;
- the upload sweep, whose abandoned blob row may name a key a copy still names (the sharer deleted their asset, a copy
  survived, and the sharer's re-upload of the same file was abandoned);
- a confirm that lost its rows to the sweep;
- deleting an account: its blob rows cascade, and only the keys no other account's row names are deleted.

Every presign reads the key from the blob row (`blob.s3_key`) instead of deriving `{owner}/{hash}`: the asset download,
the game manifest and confirm's triangle count for an already-uploaded blob.

A copy holds a `for share` lock on the source asset and blob rows while it inserts, so a concurrent delete of the
source either finishes first (the copy finds nothing, 404 for that id) or waits for the copy and then sees the new row
naming the key.

A sharer's confirm of the same bytes again writes the same verified bytes to the same key, which is harmless to a copy.

`blob.s3_key` gets an index for the reference check.

## Web app

- Library tiles show **Shared** on the owner's shared assets. The selection bar adds **Share** and **Unshare**; a folder
  in view adds **Share folder** and **Unshare folder**. The asset page has a **Shared** checkbox.
- **Shared** beside **My library** on the Library page opens `/library/shared`: a search box (name, owner or folder),
  the kind filter, and tiles with the name, kind, modelled base, owner and folders. Tiles multi-select as the library's
  do; **Copy to library** takes the selection into the folder picked beside it (none by default). The tile opens
  `/library/shared/{id}`: the mesh preview, its facts, and **Copy to library**.

## Not done

- Notifying a copier that a source changed or was unshared. A copy is a snapshot.
- Sharing with one named account rather than everyone.
