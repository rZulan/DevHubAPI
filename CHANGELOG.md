# Changelog

## 2026-11-03

### Authentication hardening
- Pin access-token validation to HS256 and the JWT type; reject unsupported critical JOSE headers.
- Resolve GitHub sign-in emails from explicitly verified provider email records, including public-profile email cases.
- Follow GitHub App installation with a fresh, session-bound S256 authorization instead of exchanging an installation code without PKCE.
- Reject legacy or invalid verifier values before exchanging repository integration credentials.
- Add isolated authentication regressions and disposable-SQL coverage for the installation-to-authorization flow; no database migration is required.

## 2026-10-30

### Workshop quotas
- Add configurable save, chat, task/note creation, daily invitation, notification fan-out, canvas object and cumulative attachment limits.
- Persist daily/minute usage reservations across restarts, serialize attachment capacity checks, and reclaim expired counters in bounded batches.
- Preserve existing large canvases and files, expose member-only storage usage, and keep list responses paginated.
- Add disposable-SQL boundary/concurrency coverage and record rollout details in `docs/workshop-limits-2026-10-30.md`.

### Security and abuse protection
- Add authentication, API, creation, invitation, upload, and realtime budgets, including server-wide and per-user concurrent realtime work limits.
- Reject untrusted browser origins for state-changing requests and realtime handshakes; keep API responses out of shared caches.
- Bound login inputs and request bodies, preserve request-size error statuses, and reduce password timing differences for missing accounts.
- Align authentication-cookie expiry with access tokens, reject older long-lived tickets, and renew through rotating refresh tokens.
- Publish the Linux API with runtime 10.0.14; add dedicated security smoke checks and an encrypted pre-migration database backup helper.
- Harden deployment file permissions and service-account checks, with rollback and restart safeguards.
- Record production verification and remaining infrastructure work in `docs/security-review-2026-10-30.md`.

## 2026-10-26

### Project document storage
- Move Ideas, ERD, UI editor, SDLC, Notes, and Wiki JSON and revision columns from Projects into ProjectDocuments.
- Preserve existing content, revisions, empty document responses, API contracts, permissions, and realtime behavior.
- Record the editor and UTC timestamp on future saves; leave unknown historical save metadata empty.
- Enforce one document per project/tool, valid JSON, nonnegative revisions, project cleanup, and retained documents when an editor is deleted.
- Migrate existing documents before removing legacy columns, and restore the latest content and revisions on rollback.
- Verify upgrades, rollback/reapply, simultaneous first and later saves, save attribution, constraints, and permissions in a disposable local SQL database.

### Organization settings and permissions
- Add individual ERD, UI/UX, Notes, Roadmap, dashboard, and invitation-management permissions while retaining legacy permission grants.
- List active invitation metadata and revoke links with organization, membership, and creator checks; reject invitation acceptance after the creator loses invite access.
- Commit adjacent role-order changes atomically, validate role colors and permission lists, and reject role assignments to nonmembers.
- Fix newly created roles being tracked as existing records by explicitly marking their domain-generated IDs.
- Exercise role creation, individual tool write permissions, project visibility, and invitation revocation against a disposable local SQL database; roll back test writes and remove the test database.
- These organization settings changes do not require new database columns.

## 2026-10-25

### UI/UX studio
- Validate optional typography, frame layout, embedded image fills, preview controls, flow starting points, and prototype interactions in project design documents.
- Reject dangling screen links and invalid control or image metadata while preserving existing designs, permissions, and revision checks. No database migration is required.

### Development roadmap
- Validate optional phase owners, real calendar dates, deliverable checklists, and blocker details across active models, inactive models, cycle history, and release paths.
- Exercise roadmap tracking through the real persistence endpoints while preserving older documents, permissions, and revision checks.
- Validate separate Agile Retrospective notes and optional-release status in active models, inactive models, and archived Sprints.
- Preserve older Agile documents that combined Sprint Review and Retrospective while accepting the revised Scrum workflow.
- Validate Waterfall, V Model, Agile, and Iterative progress with model-specific phases and notes.
- Preserve inactive models, completed cycle history, and Iterative deployment and maintenance progress in the existing project documents.
- Reject invalid phases, premature completion, malformed history, and release paths before evaluation is complete.
- Retain compatibility with older Waterfall documents and existing revision and permission checks.

### Validation and deployment
- Pass roadmap schema checks and the complete project tool persistence smoke suite; roll back all test database writes.
- Deploy with the matching client roadmap update; no database schema changes or migrations are required.

## 2026-10-21

### Notes
- Accept optional note emojis, including combined emoji and flags, and an explicit None value.
- Preserve compatibility with older notes that do not include an emoji.
- Reject malformed emoji values and strings longer than 32 UTF-16 code units.

### Validation and deployment
- Add passing regression checks for valid and malformed note emojis.
- No database schema changes or migrations are required for this release.

## 2026-10-20

### Project tools and Notes
- Add authenticated project tool endpoints for ERD, interface design, SDLC Roadmap, and Notes.
- Persist each document with an independent revision and reject stale or unauthorized writes.
- Validate document sizes, properties, ERD keys and relationships, note colors, and roadmap state.
- Broadcast project tool revisions through permission-checked SignalR subscriptions.
- Add the additive `AddProjectToolDocuments` and `AddProjectNotes` EF migrations.

### TODO task management
- Add revision-checked task editing and deletion with organization/project scope checks.
- Update task details and assignees together and remove dependent assignee records on deletion.

### Canvas compatibility
- Validate saved rotation and horizontal/vertical flip metadata for existing canvas documents.

### Validation and deployment
- Pin `System.Security.Cryptography.Pkcs` to patched version 10.0.13 after the fresh release restore identified vulnerable transitive versions.
- Build the solution in Release mode and run project tool and TODO persistence smoke checks against the development database; all test writes roll back.
- Production publishing uses the existing SSH release process, preserves server-only configuration and encryption keys, and retains previous application files for rollback.
