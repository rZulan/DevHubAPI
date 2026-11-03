using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using DevHub.Application.Common;
using DevHub.Application.GitHub;
using DevHub.Domain.Authentication;
using DevHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DevHub.Infrastructure.GitHub;

public sealed class GitHubIntegrationException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}
public sealed record GitHubInstallation(long Id, string Account, string SettingsUrl);
public sealed record GitHubRepositoryPage(IReadOnlyList<GitHubRepositoryLink> Repositories, bool HasMore);

public sealed class GitHubIntegration(ApplicationDbContext db, HttpClient http, IDataProtectionProvider protection,
    IConfiguration config, TimeProvider clock) : IGitHubRepositoryResolver
{
    private readonly IDataProtector tokens = protection.CreateProtector("DevHub.GitHub.UserTokens.v1");
    private string Setting(string name) => config[$"Integrations:GitHub:{name}"] is {Length: > 0} value
        ? value : throw new GitHubIntegrationException("GitHub repository integration is not configured.", 503);
    public bool Configured => !string.IsNullOrWhiteSpace(config["Integrations:GitHub:ClientId"])
        && !string.IsNullOrWhiteSpace(config["Integrations:GitHub:ClientSecret"])
        && Uri.TryCreate(config["Integrations:GitHub:CallbackUrl"], UriKind.Absolute, out var uri) && uri.Scheme == "https";
    public string ClientId => Setting("ClientId");
    public string CallbackUrl => Setting("CallbackUrl");
    public string InstallUrl => $"https://github.com/apps/{Uri.EscapeDataString(Setting("AppSlug"))}/installations/new";

    public async Task<object> Status(Guid userId, CancellationToken ct)
    {
        var connection = await db.Set<GitHubConnection>().AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId, ct);
        return new {configured = Configured, connected = connection is not null, login = connection?.Login,
            installUrl = Configured ? InstallUrl : null};
    }

    public async Task Connect(Guid userId, string code, string verifier, CancellationToken ct)
    {
        if (verifier.Length is < 43 or > 128 || verifier.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '.' and not '_' and not '~'))
            throw new GitHubIntegrationException("Restart GitHub authorization to reconnect.", 409);
        var fields = new Dictionary<string, string> { ["code"] = code, ["redirect_uri"] = CallbackUrl, ["code_verifier"] = verifier };
        using var result = await Exchange(fields, ct);
        var root = result.RootElement;
        var access = root.GetProperty("access_token").GetString()!;
        using var identity = await Get("user", access, ct);
        var account = identity.RootElement;
        var id = account.GetProperty("id").GetInt64();
        var linkedIdentity = await db.ExternalAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId && a.Provider == "github", ct);
        if (linkedIdentity is not null && linkedIdentity.ProviderUserId != id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new GitHubIntegrationException("Choose the GitHub account already linked to your Zevont login.");
        var connection = await db.Set<GitHubConnection>().SingleOrDefaultAsync(c => c.UserId == userId, ct);
        if (connection is null) {connection = new() {UserId = userId}; db.Add(connection);}
        connection.GitHubUserId = id;
        connection.Login = account.GetProperty("login").GetString()!;
        ApplyToken(connection, root);
        await db.SaveChangesAsync(ct);
    }

    public async Task Disconnect(Guid userId, CancellationToken ct)
    {
        // Removing the integration credential does not remove GitHub as a sign-in method.
        await db.Set<GitHubConnection>().Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
    }

    private void ApplyToken(GitHubConnection connection, JsonElement root)
    {
        connection.ProtectedAccessToken = tokens.Protect(root.GetProperty("access_token").GetString()!);
        connection.ProtectedRefreshToken = root.TryGetProperty("refresh_token", out var refresh) ? tokens.Protect(refresh.GetString()!) : null;
        connection.ExpiresAtUtc = root.TryGetProperty("expires_in", out var expires) ? clock.GetUtcNow().AddSeconds(expires.GetInt32()) : null;
        connection.RefreshExpiresAtUtc = root.TryGetProperty("refresh_token_expires_in", out var until) ? clock.GetUtcNow().AddSeconds(until.GetInt32()) : null;
    }

    private async Task<string> Token(Guid userId, CancellationToken ct)
    {
        // Serialize refreshes across API instances using the member's database row, not a process-local lock.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () => {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var connection = await db.Set<GitHubConnection>().FromSqlInterpolated(
                $"SELECT * FROM [GitHubConnections] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = {userId}").SingleOrDefaultAsync(ct);
            if (connection is null) throw new GitHubIntegrationException("Connect your GitHub account to browse repositories.", 409);
            await db.Entry(connection).ReloadAsync(ct);
            if (connection.ExpiresAtUtc is { } expires && expires <= clock.GetUtcNow().AddMinutes(1))
            {
                if (connection.ProtectedRefreshToken is null || connection.RefreshExpiresAtUtc <= clock.GetUtcNow())
                    throw new GitHubIntegrationException("Your GitHub authorization expired. Reconnect GitHub.", 409);
                using var refreshed = await Exchange(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = tokens.Unprotect(connection.ProtectedRefreshToken) }, ct);
                ApplyToken(connection, refreshed.RootElement);
                await db.SaveChangesAsync(ct);
            }
            var access = tokens.Unprotect(connection.ProtectedAccessToken);
            await transaction.CommitAsync(ct);
            return access;
        });
    }

    private async Task<JsonDocument> Exchange(Dictionary<string, string> fields, CancellationToken ct)
    {
        fields["client_id"] = ClientId; fields["client_secret"] = Setting("ClientSecret");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token") {Content = new FormUrlEncodedContent(fields)};
        request.Headers.Accept.Add(new("application/json"));
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new GitHubIntegrationException("GitHub authorization failed. Please reconnect.", 409);
        var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!document.RootElement.TryGetProperty("access_token", out var token) || string.IsNullOrWhiteSpace(token.GetString()))
        {document.Dispose(); throw new GitHubIntegrationException("GitHub authorization expired or was declined. Please reconnect.", 409);}
        return document;
    }

    private async Task<JsonDocument> Get(string path, string access, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.UserAgent.ParseAdd("Zevont/1.0");
        request.Headers.Accept.Add(new("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new GitHubIntegrationException("GitHub access was revoked or expired. Reconnect GitHub.", 409);
        if (!response.IsSuccessStatusCode) throw new GitHubIntegrationException("GitHub could not return this resource. Check repository access or try again later.", response.StatusCode == HttpStatusCode.NotFound ? 404 : 502);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }

    public async Task<IReadOnlyList<GitHubInstallation>> Installations(Guid userId, CancellationToken ct)
    {
        var access = await Token(userId, ct);
        var items = new List<GitHubInstallation>();
        for (var page = 1; page <= 100; page++)
        {
            using var result = await Get($"user/installations?per_page=100&page={page}", access, ct);
            var values = result.RootElement.GetProperty("installations");
            foreach (var value in values.EnumerateArray())
                if (value.GetProperty("app_id").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture) == Setting("AppId"))
                {
                    var id = value.GetProperty("id").GetInt64();
                    var account = value.GetProperty("account");
                    var login = account.GetProperty("login").GetString()!;
                    var organization = account.TryGetProperty("type", out var type) && type.GetString() == "Organization";
                    var settingsUrl = organization ? $"https://github.com/organizations/{Uri.EscapeDataString(login)}/settings/installations/{id}" : $"https://github.com/settings/installations/{id}";
                    items.Add(new(id, login, settingsUrl));
                }
            if (values.GetArrayLength() < 100) return items;
        }
        throw new GitHubIntegrationException("Too many GitHub installations to list. Please contact support.");
    }

    public async Task<GitHubRepositoryPage> Repositories(Guid userId, long installationId, int page, CancellationToken ct)
    {
        if (page is < 1 or > 10000) throw new GitHubIntegrationException("Invalid repository page.");
        if (!(await Installations(userId, ct)).Any(i => i.Id == installationId)) throw new GitHubIntegrationException("This GitHub installation is not available to your account.", 403);
        return await RepositoryPage(await Token(userId, ct), installationId, page, ct);
    }

    private async Task<GitHubRepositoryPage> RepositoryPage(string access, long installationId, int page, CancellationToken ct)
    {
        using var result = await Get($"user/installations/{installationId}/repositories?per_page=100&page={page}", access, ct);
        var array = result.RootElement.GetProperty("repositories");
        var repositories = array.EnumerateArray().Select(repo => new GitHubRepositoryLink(installationId,
            repo.GetProperty("id").GetInt64(), "", repo.GetProperty("full_name").GetString()!,
            repo.GetProperty("html_url").GetString()!, repo.GetProperty("default_branch").GetString() ?? "", repo.GetProperty("private").GetBoolean())).ToArray();
        return new(repositories, repositories.Length == 100);
    }

    public async Task<Result<IReadOnlyList<GitHubRepositoryLink>>> Resolve(Guid userId, IReadOnlyList<GitHubRepositorySelection> selected,
        IReadOnlyList<GitHubRepositoryLink> existing, CancellationToken ct)
    {
        Result<IReadOnlyList<GitHubRepositoryLink>> Invalid(string message) => Result<IReadOnlyList<GitHubRepositoryLink>>.Failure(new("GitHub.InvalidRepositories", message, ErrorType.Validation));
        if (selected.Count > 10 || selected.Any(s => s is null || s.RepositoryId <= 0 || s.InstallationId <= 0 || s.Label?.Length > 80)
            || selected.Select(s => s.RepositoryId).Distinct().Count() != selected.Count)
            return Invalid("Choose up to 10 distinct GitHub repositories with labels of at most 80 characters.");
        var resolved = new List<GitHubRepositoryLink>();
        try {
            foreach (var group in selected.GroupBy(s => s.InstallationId))
            {
                var missing = group.Where(s => !existing.Any(e => e.RepositoryId == s.RepositoryId && e.InstallationId == s.InstallationId)).Select(s => s.RepositoryId).ToHashSet();
                var available = new List<GitHubRepositoryLink>(existing.Where(e => e.InstallationId == group.Key));
                if (missing.Count > 0)
                {
                    if (!(await Installations(userId, ct)).Any(i => i.Id == group.Key)) return Invalid("The selected installation is not available to your GitHub account.");
                    var access = await Token(userId, ct);
                    for (var page = 1; page <= 10000 && missing.Count > 0; page++)
                    {
                        var result = await RepositoryPage(access, group.Key, page, ct);
                        foreach (var repo in result.Repositories) if (missing.Remove(repo.RepositoryId)) available.Add(repo);
                        if (!result.HasMore) break;
                    }
                    if (missing.Count > 0) return Invalid("A selected repository is no longer accessible. Choose it again or remove it.");
                }
                foreach (var selection in group)
                {
                    var repo = available.Single(e => e.RepositoryId == selection.RepositoryId);
                    resolved.Add(repo with {Label = string.IsNullOrWhiteSpace(selection.Label) ? repo.FullName.Split('/').Last() : selection.Label.Trim()});
                }
            }
            return Result<IReadOnlyList<GitHubRepositoryLink>>.Success(resolved);
        } catch (GitHubIntegrationException error) { return Invalid(error.Message); }
        catch (CryptographicException) { return Invalid("GitHub credentials could not be read. Reconnect GitHub."); }
        catch (HttpRequestException) { return Invalid("GitHub is unavailable. Try again before saving repository changes."); }
    }
}
