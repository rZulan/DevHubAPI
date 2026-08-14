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

1. Clone both repositories into sibling directories named `DevHubAPI` and
   `DevHubClient`, then confirm both are on `master` and synchronized with `origin`.
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
     it as the `Jwt:Key` user-secret for `src/DevHub.Api/DevHub.Api.csproj`.
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
10. Create an untracked `DevHubClient/.env.local` containing:

    ```dotenv
    VITE_API_BASE_URL=/api
    VITE_OAUTH_API_BASE_URL=https://localhost:7116/api
    VITE_OAUTH_ENABLED=true
    ```

11. Verify the backend with:
    - `dotnet build DevHub.sln --no-restore`
    - `dotnet format DevHub.sln --no-restore --verify-no-changes`
    - `dotnet ef migrations has-pending-model-changes --project src/DevHub.Infrastructure --startup-project src/DevHub.Api --no-build`
12. Verify the frontend with `npm run build` and `npm run lint`.
13. Start the backend on `https://localhost:7116` and the frontend on
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
