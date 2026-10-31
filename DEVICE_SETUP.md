# DevHub setup handoff for Codex

Use this document as the prompt for Codex on the new development device.

## Repositories

- Backend: `https://github.com/rZulan/DevHubAPI.git`
- Frontend: `https://github.com/rZulan/DevHubClient.git`
- Default branch: `master`

## Prompt for Codex

Set up the complete DevHub development environment on this device. Work autonomously
through all non-secret steps, verify each stage, and tell me only when a step requires
my account access or an interactive operating-system confirmation.

1. Clone both repositories into sibling directories named `api` and `client` under
   a `DevHub` workspace, then confirm both are on `master` and synchronized with
   `origin`. Preserve any existing work; do not reset a checkout to achieve this.
2. Read the backend and frontend READMEs and inspect their project configuration before
   changing anything.
3. Install or verify the required tooling:
   - Git
   - .NET 10 SDK
   - the repository-local `dotnet-ef` tool
   - Node.js compatible with Vite 8
   - npm
   - SQL Server LocalDB or SQL Server Express
4. Restore dependencies with `dotnet restore`, `dotnet tool restore`, and `npm ci`.
5. Configure the backend without committing secrets:
   - Set `ConnectionStrings:DefaultConnection` with .NET user-secrets if the local SQL
     Server instance name differs from the repository default.
   - Generate a cryptographically random JWT signing key of at least 32 bytes and store
     it as the `Jwt:Key` user-secret for `src/DevHub.Api/DevHub.Api.csproj`, only if
     one has not already been restored or configured.
   - For a new database, generate a separate random `Chat:EncryptionKey`. When
     restoring existing data, preserve its original chat key, including an older
     deployment's JWT-key fallback; replacing it prevents reading existing messages.
   - Do not print secret values in chat or write them to tracked files.
6. Ask me to enter these OAuth values directly in my terminal when needed; never ask me
   to paste client secrets into chat:
   - `Authentication:Google:ClientId`
   - `Authentication:Google:ClientSecret`
   - `Authentication:GitHub:ClientId`
   - `Authentication:GitHub:ClientSecret`
7. Confirm the provider applications use these development callbacks:
   - Google: `https://localhost:7116/signin-google`
   - GitHub: `https://localhost:7116/signin-github`
8. Run `dotnet dev-certs https --trust`. Pause only if I must approve the operating
   system trust dialog.
9. Apply all EF Core migrations to the configured database.
10. Use the tracked client environment profiles. If overrides are needed, create an
    untracked `client/.env.development.local` containing:

    ```dotenv
    VITE_API_BASE_URL=/api
    VITE_OAUTH_API_BASE_URL=https://localhost:7116/api
    VITE_OAUTH_ENABLED=true
    ```

11. Verify the backend with:
    - `dotnet build DevHub.sln --no-restore`
    - the relevant smoke suites, using disposable databases for SQL checks
    - `dotnet ef migrations has-pending-model-changes --project src/DevHub.Infrastructure --startup-project src/DevHub.Api --no-build`
12. Verify the frontend with `npm run build` and `npm run lint`.
13. Start the backend with `api/run-api.ps1 -Environment Local` on
    `https://localhost:7116` and the frontend with `npm run dev:local` on
    `http://localhost:5173`. Confirm regular authentication, cookie refresh, Google and
    GitHub challenge redirects, account linking, and provider disconnect behavior.
14. Leave both Git working trees clean except for intentionally untracked local secret
    or environment files, and summarize the commands needed to start both projects in
    future sessions.

## Important behavior

- Refresh tokens belong only in the HTTP-only refresh cookie, never browser storage.
- OAuth sign-in must not automatically merge with an existing account based only on a
  matching email. Sign in normally and link the provider from account settings.
- Local provider callbacks go directly to the HTTPS API rather than through the Vite
  development proxy.
- Production origins, callback URLs, cookie security, database credentials, and provider
  credentials must be configured separately from development values.

## Credentials to request

Ask only for missing values. Collect secrets through a masked terminal prompt or an
approved secret store, never chat, command history, a source file, or a Git commit.
Store local API credentials in .NET user-secrets for the API project.

| Scope | Needed input | Notes |
| --- | --- | --- |
| GitHub | Sign-in with access to both repositories | Use Git Credential Manager or the user's existing SSH setup. Do not embed a token in a remote URL. |
| Local SQL Server | Instance/database and Windows authentication, or SQL username/password | Configure `ConnectionStrings:DefaultConnection`; a new machine's SQL instance may differ. |
| API authentication | `Jwt:Key` | Generate for a new local database; preserve restored settings. |
| API chat | `Chat:EncryptionKey`, or the original JWT fallback | Keep the key that encrypted any restored chat data. |
| Google OAuth, if enabled | `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret` | Register the local callback above. |
| GitHub OAuth, if enabled | `Authentication:GitHub:ClientId` and `Authentication:GitHub:ClientSecret` | These are OAuth application credentials, separate from Git access. |
| Client | API URL, OAuth API URL, and OAuth enabled flag | These are public settings; the client does not need provider secrets or database credentials. Never put a secret in `VITE_*`. |
| Production deployment, only when requested | SSH access to the existing server | Reuse the established production settings; do not upload local user-secrets or regenerate production keys. |

For password-only development without OAuth credentials, disable OAuth in the local
client override. Production currently uses same-origin `/api`, with OAuth enabled.

## Local recovery backup

The current device's recovery folder contains Git bundles, source archives, deployment
support files, and a manifest identifying both commits. Private development settings
are stored separately as `private-settings.dpapi`: Windows DPAPI encrypts this for the
current Windows account on this device. Use the accompanying restore script from that
account. It is not a portable encrypted backup and cannot configure a different device;
use the credential checklist above on another machine. No private settings belong in
GitHub or the public frontend build.
