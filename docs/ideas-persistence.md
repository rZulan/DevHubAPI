# Ideas canvas persistence

Each project stores its ordered shape document in `Projects.IdeasJson` and a revision in `Projects.IdeasRevision`. Shape geometry, fill/outline settings, text, typography, alignment and stacking order are saved. Selection, pan and zoom are session UI state.

- `GET /api/organizations/{organizationId}/projects/{projectId}/ideas` returns `{ revision, items }`.
- `PUT` on the same route accepts `{ revision, items }`. The revision must match the current document; a successful save returns the next revision. Stale writes return 409 without changing data.
- Access requires organization membership and project visibility. Writes additionally require **Edit ideation** permission. Invalid documents return 400.
- The client debounces edits for 600 ms and serializes writes. It flushes pending edits on navigation and keeps a local recovery draft until saving succeeds. Load failures disable editing. Save failures expose Retry; conflicts preserve the local draft until the user explicitly chooses to reload the saved version.

Apply the migration before starting the updated API:

```powershell
dotnet ef database update --project src/DevHub.Infrastructure --startup-project src/DevHub.Api --configuration Release
```

From `api/`, test real SQL Server persistence using the configured development database:

```powershell
dotnet run --project tests/DevHub.IdeasSmoke -c Release
```

The test requires an existing project and rolls back every write. Client queue/recovery tests run from `client/` with `node --test tests/ideas-document.test.mjs`.
