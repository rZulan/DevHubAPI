# Changelog

## 2026-10-25

### Development roadmap
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
