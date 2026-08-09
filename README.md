# DevHub

ASP.NET Core API organized using Clean Architecture:

- `DevHub.Api` - HTTP endpoints, middleware, OpenAPI, and composition root.
- `DevHub.Application` - CQRS commands/queries, handlers, DTOs, and abstractions.
- `DevHub.Domain` - domain entities and business rules.
- `DevHub.Infrastructure` - EF Core, SQL Server repositories, password hashing, and JWT implementation.

## Local setup

The default connection string uses SQL Server LocalDB. Change `DefaultConnection` in
`src/DevHub.Api/appsettings.json` when using another SQL Server instance.

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

Run the API:

```powershell
dotnet run --project src/DevHub.Api
```

In development, Scalar is available at `http://localhost:5288/scalar/v1`.

## Initial endpoints

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/refresh` - rotates the HTTP-only refresh cookie.
- `POST /api/auth/logout` - revokes the refresh-token family and clears session cookies.
- `GET /api/auth/external/{google|github}` - starts external login or registration.
- `GET /api/users/me` - requires an authentication cookie or bearer token.
- `PATCH /api/users/me` - updates username and profile names.
- `GET|DELETE /api/users/me/connections/{google|github}` - links or disconnects a provider.
- `GET|POST /api/organizations` - lists memberships or creates an organization.
- `GET|PUT|DELETE /api/organizations/{organizationId}` - organization CRUD.
- `GET|PUT|DELETE /api/organizations/{organizationId}/members/{userId?}` - lists,
  adds, or removes organization members.
- `GET|POST /api/organizations/{organizationId}/teams` - lists or creates teams.
- `GET|PUT|DELETE /api/organizations/{organizationId}/teams/{teamId}` - team CRUD.
- `GET|PUT|DELETE /api/organizations/{organizationId}/teams/{teamId}/members/{userId?}` -
  lists, adds, or removes team members.

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
automatically adds the creator as a member. A user must belong to the organization before
they can be added to one of its teams.
