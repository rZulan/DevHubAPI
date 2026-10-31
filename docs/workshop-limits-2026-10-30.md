# Workshop limits — 30 October 2026

**Status: deployed and verified.** Production release `20261030133610-3879df12` is live, including the additive `20261030052632_AddWorkshopUsageLimits` migration.

| Area | Enforced default |
| --- | --- |
| Chat | Sustained 30 messages/minute/user; five-message burst; 4,000 characters/message; at most ten `@name` tokens/message. Email addresses are excluded from the mention count. |
| Document saves | 30 attempts/minute/user/project/tool, using a sliding window. Each editor serializes saves and combines pending changes, pacing requests slightly over two seconds apart. |
| Invitations | Existing five/minute/user action budget, plus 20 new links/user/UTC day and 50/workshop/UTC day. Revocation does not replenish the daily budget. |
| TODO boards, tasks, notes | Shared 30 creations/minute/user across workshops. New note IDs count individually; changing an existing note does not consume creation units. A save may introduce at most 30 new notes. |
| Selection sharing | Client combines changes at 100 ms; server accepts up to 20 updates/second/user across connections. |
| Ideas and UI canvases | 2,000 objects for new content, warning from 1,000. Existing larger documents remain readable, editable and exportable, and may shrink; they cannot grow beyond their currently stored count. Existing document/schema size limits also apply. |
| List responses | Chat defaults to 50/max 100 messages. TODO list defaults to 50/max 100 boards; existing paged TODO views retain their smaller page sizes. The calendar retrieves pages so later boards are not silently omitted. Notifications retain 25-item cursor pages; bug inboxes retain their existing bounded pages. Membership/permission aggregates are unchanged. |
| Attachments | 100 MiB/workshop plus a 1 GiB service-wide budget. Existing five-file, 10 MiB/file, 25 MiB/submission and two-concurrent-upload limits remain. Counts cover stored bug-report attachments, across public and team submissions. Inline design images remain governed by the existing document-size limits. |
| Activity notifications | At most 30 notification rows/minute/actor across producers/workshops. Public reporting has a separate workshop-wide budget. Assignment notices take priority within an operation; excess notices are suppressed while the underlying authorized action still saves. |

Daily invitations, creation counts and notification reservations are stored in SQL, including across API restarts and multiple instances. Reservations are conservative: a failed downstream write can still consume a reserved allowance. Expired counters are reclaimed in bounded hourly batches. Attachment writers hold a transaction-owned global database lock through their capacity check and commit, preventing simultaneous uploads from exceeding shared quotas. Existing attachments are preserved.

Limits are configured under `Security:Workshop` using the property names in `WorkshopLimits.cs`. API/general/realtime concurrency budgets remain additional layers. No simultaneous-collaborator cap was guessed; that requires a separate staging capacity test.

## User experience

- The member-only workshop limits endpoint reports configured values and attachment usage, without exposing other workshops or global storage figures.
- Storage warns at 80% and at capacity. Canvas warnings appear before the object limit.
- Saves preserve recovery drafts, honor `Retry-After`, display a countdown, and retry the newest pending content. Pressing Retry or Save does not bypass the cooldown.
- Validation failures retain useful server details. Existing oversized documents are not truncated or deleted.
- Ordinary privileged actions and authorization checks still apply; quotas grant no additional permissions.

## Verification

- 24 dedicated disposable-SQL checks: additive migration, daily reset, restart persistence, atomic multi-budget rejection, concurrent notification reservations, capped fan-out without discarding business work, shared task/note budgets, existing oversized canvas behavior, concurrent upload capacity and service-wide storage boundaries.
- 45 dedicated security checks, including document-save throttling and isolation between tools/projects.
- Existing workshop membership/presence, project-document, TODO, notification, and bug-report HTTP/SQL regression suites passed. Database suites used disposable local databases; no production flood/load test was performed.
- 366 frontend tests passed, including serialized autosave, cooldown enforcement, preservation of edits during a throttled save, and complete calendar pagination without treating failed pages as complete data.
- Mobile (390 px) and desktop (1280 px) quota-notice browser checks passed without runtime errors or horizontal overflow.
- Release builds passed. One existing Fast Refresh lint warning remains in `tests/fixtures/erd-overlap.tsx`.

## Production rollout

The read-only production inventory found one empty Ideas canvas, one UI document with three layers, and zero stored attachments. No existing content exceeds the new caps. The first Azure SQL connection returned transient database-unavailable error 40613; the subsequent read-only attempt succeeded.

An authenticated encrypted pre-migration BACPAC was exported, validated, encrypted and verified while the API was stopped. The retained ciphertext is `/var/backups/devhub/20261030133610-3879df12/before-migration.bacpac.aesgcm` (198,428 bytes, root-only mode 600). The root-only encryption key is preserved separately in `/etc/devhub/backup-aes.key`; no plaintext BACPAC remains.

The migration completed, API and Nginx are active, and deployed API/Infrastructure assembly and frontend index hashes match the tested release. Production checks passed: homepage/theme 200, private profile and unauthenticated quota endpoint 401 with no-store, database-backed nonexistent-account login 401, and mobile login rendering without runtime/CSP errors. No production data was created by these availability checks.

Previous API/frontend directories and current compressed release archives remain available for rollback. Reproducible migration and backup executables were removed after successful verification to recover server space; encrypted database backups and their key were retained. As before, file rollback does not reverse a database migration.

These controls reduce application abuse and resource exhaustion. Upstream network DDoS protection remains separate infrastructure work.
