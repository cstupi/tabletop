# Shared folders: plan

Spec: [2026-11-07-shared-folders-design.md](../specs/2026-11-07-shared-folders-design.md). Test-first per task; each task
ends with its tests green.

## Tasks

1. **Migration** `server/management/Persistence/Migrations/029_shared_folders.sql`: `account.username` (format check,
   `account_username_idx` unique on `lower(username)`), `folder.shared` (+ top-level check, partial unique
   `folder_shared_idx`), `shared_copy`. `MigrationTests` (E2E): a second username differing only in case is refused, a
   bad format is refused, a second shared folder and a shared sub-folder are refused.
2. **Username.** `AccountRow(Id, Name, Username)`; `IAccountRepository.SetUsername` (`UsernameResult`: Set, Taken,
   Missing) and `FindByUsername`; SQL, fake, `RepositoryContract.Accounts`. `PUT /v1/account/username` in
   `AccountEndpoints`, `Limits.Username` (format); `AccountTests`.
3. **Shared folder.** `FolderDto.Shared`; `IFolderRepository.CreateShared`; `Patch` refuses a parent for the shared folder;
   `POST /v1/folders/shared`; SQL, fake, `RepositoryContract.Folders`, `FolderTests`.
4. **Copy core.** Extract the Accept copy into `Content/Assets/AssetCopies.Copy` (SQL) and `InMemoryRows.CopyAssets`
   (fake); `OfferRepository.Accept` and `InMemoryOfferRepository.Accept` call them. Offer tests stay green.
5. **Shared repository.** `Content/Shared/`: `SharedDtos`, `ISharedRepository` (`Browse`, `BlobKey`, `Copy`),
   `SharedRepository`, `InMemorySharedRepository`, `RepositoryContract.Shared` (browse is the subtree only; copy of assets
   and of a nested tree; destination; quota whole-or-nothing; per-copy cap; folder cap; daily cap; own folder; the sharer
   deleting after a copy; a blob's key kept while a copy names it).
6. **Endpoints.** `SharedEndpoints` (`/v1/shared/{username}`, `/assets/{id}/download`, `/copy`), registered in
   `Program.cs` with `SharedCopyLimits.Default`; `ManagementFactory` registers the fake. `SharedFolderTests` over HTTP: A
   shares, B browses, previews and copies a model and a nested folder into a destination, C sees nothing of A's other
   folders, A deletes the source and B's copy still downloads; quota and cap refusals; 404s.
7. **Web.** `types.ts`, `client.ts` (`account.setUsername`, `folders.createShared`, `shared.*`); `src/lib/library/shared.ts`
   (`usernameProblem`, `copySummary`, `sharedFirst`) with `tests/shared.test.ts`; Account page Username section;
   `FolderTree` shared mark, no Move on it, **Make a Shared folder**; `/library/shared` page; `LibraryTabs` tab.
8. **Docs.** `docs/schema.md`, `docs/data-model.md`, `server/management/AGENTS.md`, `web/AGENTS.md`, the 2026-10-25 spec's
   "nothing is public" paragraph.

## Validation

- `dotnet test tests/Tabletop.Management.Tests` (unit, in-memory fakes): the username lookup (exact, case-insensitive,
  unique), browse permission (only the Shared subtree, never other folders or unfiled assets), copy of a model and of a
  nested folder, quota refusal whole-or-nothing, per-copy and folder and daily caps, the sharer deleting after a copy leaves
  the copy downloading, blob deletion only when no row names the key.
- `dotnet test tests/Tabletop.Management.Tests --settings tests/e2e.runsettings --filter "FullyQualifiedName~PostgresRepositoryContract|FullyQualifiedName~MigrationTests"`:
  the SQL repositories and migration 029 against the compose Postgres (each `TestDatabase` is its own migrated database;
  the compose management container is not involved).
- `dotnet build` and `dotnet test` (the whole unit suite).
- `cd web && npm test` and `npm run check`.
- End to end over HTTP: management from this worktree on a free port, dev mode, over a database of its own on the compose
  Postgres and the compose MinIO; two dev accounts (`POST /v1/dev/sessions`); A sets a username, makes a Shared folder with a
  sub-folder, uploads and files a mesh; B browses, previews, copies the folder; A deletes the mesh; B downloads the copy.
- Screenshots: the web dev server from this worktree on a free port proxying `/v1` to that management; headless Chromium
  (Playwright) signed in as B on `/library/shared?user=<A>`, and as A on `/library`.

## Results

(filled in after the run)
