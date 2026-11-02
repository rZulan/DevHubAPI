# DevHub

ASP.NET Core API organized using Clean Architecture:

- `DevHub.Api` - HTTP endpoints, middleware, OpenAPI, and composition root.
- `DevHub.Application` - CQRS commands/queries, handlers, DTOs, and abstractions.
- `DevHub.Domain` - domain entities and business rules.
- `DevHub.Infrastructure` - EF Core, SQL Server repositories, password hashing, and JWT implementation.

## Universal UML documents

The `uml` project tool stores a versioned library of named diagrams. Each diagram has an optional UML type and its own Playground canvas items. Writes require Edit UML diagrams, with legacy Edit ideation compatibility. Validation checks diagram identities, names, types, UML symbols, shape properties, and per-canvas capacity. The separate diagram endpoints are retired; their data and applied migrations remain intact.

## Local setup

The local connection string uses SQL Server Express. Change `DefaultConnection` in
`src/DevHub.Api/appsettings.Development.json` when using another SQL Server instance.

Store the JWT signing key outside source control. It must contain at least 32 bytes:

```powershell
dotnet user-secrets set `
  --project src/DevHub.Api/DevHub.Api.csproj `
  "Jwt:Key" `
  "replace-with-a-long-random-development-key"
```

Configure the server-side OAuth credentials in user-secrets (never commit provider
client secrets):

```powershell
dotnet user-secrets set --project src/DevHub.Api/DevHub.Api.csproj `
  "Authentication:Google:ClientId" "your-google-client-id"
dotnet user-secrets set --project src/DevHub.Api/DevHub.Api.csproj `
  "Authentication:Google:ClientSecret" "your-google-client-secret"
dotnet user-secrets set --project src/DevHub.Api/DevHub.Api.csproj `
  "Authentication:GitHub:ClientId" "your-github-client-id"
dotnet user-secrets set --project src/DevHub.Api/DevHub.Api.csproj `
  "Authentication:GitHub:ClientSecret" "your-github-client-secret"
```

Use these local callback URLs in the provider consoles:

- Google: `https://localhost:7116/signin-google`
- GitHub: `https://localhost:7116/signin-github`

Trust the ASP.NET Core development certificate before testing OAuth:

```powershell
dotnet dev-certs https --trust
```

Restore the repository-local EF Core tool and create the database:

```powershell
dotnet tool restore
dotnet ef database update `
  --project src/DevHub.Infrastructure `
  --startup-project src/DevHub.Api
```

Run or restart the API. This launcher stops an existing DevHub API process from this
repository before starting the new instance. It refuses to stop unrelated applications
that happen to use the same ports:

```powershell
.\run-api.ps1
```

Use `dotnet run --project src/DevHub.Api` when automatic restart behavior is not needed.

In development, Scalar is available at `http://localhost:5288/scalar/v1`.

## Project editor documents


## Initial endpoints

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/refresh` - rotates the HTTP-only refresh cookie.
- `POST /api/auth/logout` - revokes the refresh-token family and clears session cookies.
- `GET /api/auth/external/{google|github}` - starts external login or registration.
- `GET /api/users/me` - requires an authentication cookie or bearer token.
- `PATCH /api/users/me` - updates username, profile names, and date of birth.
- `GET|DELETE /api/users/me/connections/{google|github}` - links or disconnects a provider.
- `GET|POST /api/organizations` - lists memberships or creates an organization.
- `GET|PUT|DELETE /api/organizations/{organizationId}` - organization CRUD.
- `GET|PUT|DELETE /api/organizations/{organizationId}/members/{userId?}` - lists,
  adds, or removes organization members.
- `GET|POST /api/organizations/{organizationId}/teams` - lists or creates teams.
- `GET|PUT|DELETE /api/organizations/{organizationId}/teams/{teamId}` - team CRUD.
- `GET|PUT|DELETE /api/organizations/{organizationId}/teams/{teamId}/members/{userId?}` -
  lists, adds, or removes team members.
- `GET|POST /api/organizations/{organizationId}/projects` - lists visible projects or
  creates a project for a team.
- `GET|PUT|DELETE /api/organizations/{organizationId}/projects/{projectId}` - project
  details, updates, and deletion. Organization owners see every project; other members
  only see projects owned by their teams. Owners and the owning team leader can manage
  a project.
- `GET /api/organizations/{organizationId}/appearance` - the current member's workshop color
  scheme, the built-in presets, and that member's custom schemes.
- `PUT /api/organizations/{organizationId}/appearance/active` - chooses the member's scheme.
- `POST|PUT|DELETE /api/organizations/{organizationId}/appearance/schemes/{schemeId?}` -
  custom color scheme CRUD. Deleting the scheme in use returns the member to the default.

Color schemes are personal: any member can choose or create them, nobody else can see them,
and they are removed when the member leaves the organization. Smoke-test the rules against
the local database (writes are rolled back) with
`dotnet run --project tests/DevHub.AppearanceSmoke -c Release`.

Project Wiki uses `GET|PUT /api/organizations/{organizationId}/projects/{projectId}/tools/wiki`.
Members who can view a project can read its wiki; writes require **Manage wiki** (or
owner/Administrator access). Saves use the shared document revision to reject stale
writes and notify connected project viewers. A wiki supports up to 100 Markdown pages,
100,000 characters per page, 20 previous versions per page, and a 4,000,000-character
document. The `AddProjectWiki` migration adds independent wiki storage and revision
columns. Wiki validation, SQL round trips, conflicts, and permission isolation run with
`dotnet run --project tests/DevHub.ProjectToolsSmoke -c Release -- --isolated`.

## Bug reports

The `AddProjectBugReports` migration creates project reports, category definitions,
public-link settings, and private attachment storage in SQL. Use the **Bug Reports**
project resource to enable public reporting and copy `/report-bug/{token}` on the
frontend's origin. Links start disabled; disabling blocks both GET and POST, and
replacing a link invalidates its previous token without removing reports.

- `GET|POST /api/organizations/{organizationId}/projects/{projectId}/bug-reports`
- `GET|PUT /api/organizations/{organizationId}/projects/{projectId}/bug-reports/{reportId}`
- `GET|PUT .../bug-reports/portal`
- `POST .../bug-reports/categories`, `PUT .../bug-reports/categories/{categoryId}`
- `GET .../bug-reports/{reportId}/attachments/{attachmentId}`
- `GET|POST /api/public/bug-reports/{token}` (anonymous; form metadata and submission only)

Submissions use multipart fields `submissionId` (UUID, reused on retries), `email`,
`title`, `description`, optional `steps`, `environment`, and `categoryId`, `priority`
(`low|medium|high|critical`), and repeated `files`. Email is an unverified sender
reference; no account or email delivery is required. Public responses never list
reports, reveal reporter emails, expose notes, or grant attachment downloads.
Private project viewers can read reports; **Manage bug reports** is required to
triage, submit team reports, manage categories, or change links. Triage and link
updates use revisions to reject stale changes. Categories can be archived and restored.

Allow up to 5 attachments, 10 MiB each and 25 MiB total. Supported types are PNG,
JPEG, GIF, WebP, PDF, ZIP, MP4, WebM, and UTF-8 TXT/LOG/CSV/JSON. The API checks
extensions, binary signatures or text encoding, sanitizes filenames, and returns
files as authenticated downloads with `nosniff`. Request bodies are limited to
28 MiB. Public POSTs are limited per API instance to 10 attempts per IP per hour;
GETs to 60 per minute. Public project intake is capped at 200 reports and 256 MiB
of attachments per UTC day. Links are submission capabilities; distribute them
only to the intended reporters.

Run real HTTP, multipart, authorization, throttling, and SQL checks in a disposable
local database with `dotnet run --project tests/DevHub.BugReportsSmoke -c Release`.
The browser fixture at `/tests/fixtures/bug-reports.html` uses isolated in-memory
data and never writes to a project.

Registration, login, external login, and token refresh issue an authentication cookie
plus a separate rotating HTTP-only refresh-token cookie. Refresh tokens are no longer
returned in JSON. `POST /api/auth/refresh` reads the refresh cookie and accepts an empty
request body; logout revokes its active token family. The API still accepts either the
authentication cookie or a JWT bearer token.

The frontend development environment should include:

```dotenv
VITE_API_BASE_URL=/api
VITE_OAUTH_API_BASE_URL=https://localhost:7116/api
VITE_OAUTH_ENABLED=true
```

External sign-in intentionally refuses to merge with an existing user merely because
the provider email matches. Sign in normally, then connect the provider from Account
settings. Allowed frontend origins and post-OAuth paths are explicitly configured under
`Frontend` in `appsettings.json`; replace the local values for deployment.

Usernames are unique without regard to letter casing and follow X-style handle syntax:
5-15 ASCII letters, numbers, or underscores. The leading `@` is display-only and is not
stored as part of the username; spaces, hyphens, and other symbols are not allowed.

All organization and team endpoints require authentication. Creating an organization
makes the creator its owner and first member. Only the organization owner can create or
delete teams and assign team leadership. Each team has exactly one leader; the owner or
that leader can manage team members. A user must belong to the organization before they
can be added to one of its teams, and the active leader cannot be removed until leadership
is reassigned.

## GitHub repository connections

Create and edit project forms support up to ten labeled GitHub repositories in
addition to the existing manual repository URL. New selections are verified on
the server against the repositories accessible to both the connecting user and
this GitHub App installation. Existing project managers can retain or remove
previously verified links without borrowing the connector's GitHub credential.
Names and URLs of selected private repositories are shared with project viewers;
repository contents and credentials are not returned to other project members.

Configure these server-only settings with user-secrets locally:
`Integrations:GitHub:AppId`, `ClientId`, `ClientSecret`, `AppSlug`, and `CallbackUrl`.
The local callback is `https://localhost:7116/api/integrations/github/callback`.
Keep the GitHub App's private key in the existing secret store for installation
operations; this picker uses user authorization tokens. Repository metadata read
access is sufficient. Enable expiring user tokens and user authorization during
installation. The initial integration does not require webhooks or code access.

The API exposes `/api/integrations/github/status`, `POST authorize`,
`GET installations`, `GET installations/{id}/repositories?page=1`, and
`DELETE connection`. The anonymous callback verifies a protected, expiring
correlation cookie, the initiating DevHub login session, and OAuth state.
Regular authorization uses PKCE. Tokens are protected using ASP.NET Core Data
Protection and refreshed on the server. Preserve the Data Protection key ring
across restarts/deployments; losing it requires users to reconnect. Production
deployments must configure a persistent, protected key ring shared by API instances.
Disconnecting repository access leaves the existing GitHub sign-in method intact.

Apply `AddGitHubRepositoryConnections` before running the updated API.
Run `dotnet run --project tests/DevHub.GitHubSmoke --configuration Release`
from `api/` for callback, authorization, and multi-repository persistence checks.
This suite uses simulated GitHub HTTP responses and a disposable local SQL database.

## Personal dashboards

Members can read and save their private dashboard at
`GET/PUT /api/organizations/{organizationId}/dashboard/personal`. The authenticated
account determines whose layout is accessed; the request accepts only widgets.
Every member can customize their own layout without the shared `Manage dashboard`
permission. Personal saves do not publish or broadcast to the organization.

Apply the `AddPersonalDashboards` EF migration before running the updated API.
Run `dotnet run --project tests/DevHub.DashboardSmoke -c Release` from `api/` to
verify migrations, persistence, and member isolation in a disposable local database.

## Environment switch

Run `.\run-api.ps1 -Environment Local` (default) or `.\run-api.ps1 -Environment Production`. Alternatively select **Local** or **Production** in the IDE launch profiles. Pair with the client's `npm run dev:local` or `npm run dev:production`. Neither command publishes.

Local selects ASP.NET Development, loads existing user-secrets, and returns OAuth logins to http://localhost:5173. The Google callback is https://localhost:7116/signin-google. Production selects ASP.NET Production and loads production configuration/environment variables; development user-secrets are not loaded. Supply production credentials and connection settings separately before running that profile. Existing server secrets remain on the server.

Use `builder.Environment.IsDevelopment()` (or injected `IHostEnvironment.IsDevelopment()`) in API code instead of a compile-time flag. Environment selection requires restarting the processes.
