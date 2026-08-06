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
- `POST /api/auth/refresh` - rotates the refresh token and returns a new token pair.
- `POST /api/auth/logout` - clears the authentication cookie.
- `GET /api/users/me` - requires an authentication cookie or bearer token.

Registration, login, and token refresh also issue a Secure, HttpOnly, SameSite=Strict
authentication cookie. The API accepts either this cookie or a JWT bearer token. When
using Scalar over HTTPS, the browser stores and sends the cookie automatically.
