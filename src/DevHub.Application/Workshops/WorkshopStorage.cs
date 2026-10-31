namespace DevHub.Application.Workshops;

public sealed record WorkshopStorageUsage(long UsedBytes, long LimitBytes, bool Warning, bool Full);

public interface IWorkshopStorage
{
    Task<WorkshopStorageUsage> Get(Guid organizationId, CancellationToken ct);
}
