using DevHub.Application.Common;

namespace DevHub.Application.GitHub;

public sealed record GitHubRepositorySelection(long InstallationId, long RepositoryId, string? Label);
public sealed record GitHubRepositoryLink(long InstallationId, long RepositoryId, string Label, string FullName, string Url, string DefaultBranch, bool Private);
public interface IGitHubRepositoryResolver
{
    Task<Result<IReadOnlyList<GitHubRepositoryLink>>> Resolve(Guid userId, IReadOnlyList<GitHubRepositorySelection> selected,
        IReadOnlyList<GitHubRepositoryLink> existing, CancellationToken ct);
}
