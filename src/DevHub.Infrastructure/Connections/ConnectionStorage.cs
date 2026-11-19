using System.Text.Json;
using DevHub.Application.Connections;
using DevHub.Domain.Projects;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Connections;

internal static class ConnectionStorage
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)!;
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);
}

internal sealed class StoredConnection
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Revision { get; set; }
    public string NodesJson { get; set; } = "[]";
    public string RulesJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public bool Deleted { get; set; }
    public StoredRule[] Rules => ConnectionStorage.Read<StoredRule[]>(RulesJson);
    public ConnectionNode[] Nodes => ConnectionStorage.Read<ConnectionNode[]>(NodesJson);
}

internal sealed record StoredRule(ConnectionRule Rule, long ActiveAfter, long? ActiveThrough, Guid ActivationId,
    int ConnectionRevision, Guid OwnerUserId, DateTimeOffset ActiveAtUtc, Guid? TriggeredRunId = null);

internal sealed class QueuedConnectionEvent
{
    public long Position { get; set; }
    public Guid ProjectId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ActorUserId { get; set; }
    public string DraftJson { get; set; } = string.Empty;
    public string ChainJson { get; set; } = "[]";
    public long? RootPosition { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeasedUntilUtc { get; set; }
    public int Attempts { get; set; }
    public ConnectionEventDraft Draft => ConnectionStorage.Read<ConnectionEventDraft>(DraftJson);
    public Guid[] Chain => ConnectionStorage.Read<Guid[]>(ChainJson);
}

internal sealed class StoredConnectionRun
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid RuleId { get; set; }
    public Guid OwnerUserId { get; set; }
    public int ConnectionRevision { get; set; }
    public long EventPosition { get; set; }
    public long RootPosition { get; set; }
    public string RuleJson { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
    public string SourceJson { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Status { get; set; } = "pending";
    public string? Detail { get; set; }
    public string StepsJson { get; set; } = "[]";
    public byte[] Version { get; set; } = [];
    public StoredRule Definition => ConnectionStorage.Read<StoredRule>(RuleJson);
    public ConnectionRule Rule => Definition.Rule;
    public StoredStep[] Steps => ConnectionStorage.Read<StoredStep[]>(StepsJson);
    public ConnectionRunResponse Response() => new(Id, ConnectionId, RuleId, Rule.Title, Event,
        ConnectionStorage.Read<ConnectionNodeRef>(SourceJson), OccurredAtUtc, Status, Detail,
        Steps.Select(step => step.Response).ToArray());
}

internal sealed record StoredStep(ConnectionStepResponse Response, ConnectionNodeRef? CreatedNode = null);

internal sealed class StoredConnectionConfiguration : IEntityTypeConfiguration<StoredConnection>
{
    public void Configure(EntityTypeBuilder<StoredConnection> entity)
    {
        entity.ToTable("ProjectConnections");
        entity.Ignore(connection => connection.Rules);
        entity.Ignore(connection => connection.Nodes);
        entity.HasKey(connection => connection.Id);
        entity.Property(connection => connection.Title).HasMaxLength(150);
        entity.Property(connection => connection.Revision).IsConcurrencyToken();
        entity.HasIndex(connection => new { connection.ProjectId, connection.Deleted });
        entity.HasOne<Project>().WithMany().HasForeignKey(connection => connection.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class QueuedConnectionEventConfiguration : IEntityTypeConfiguration<QueuedConnectionEvent>
{
    public void Configure(EntityTypeBuilder<QueuedConnectionEvent> entity)
    {
        entity.ToTable("ProjectConnectionEvents");
        entity.Ignore(evt => evt.Draft);
        entity.Ignore(evt => evt.Chain);
        entity.HasKey(evt => evt.Position);
        entity.Property(evt => evt.Position).UseIdentityColumn();
        entity.HasIndex(evt => new { evt.ProjectId, evt.Position });
        entity.HasIndex(evt => new { evt.FinishedAtUtc, evt.Position });
        entity.HasOne<Project>().WithMany().HasForeignKey(evt => evt.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class StoredConnectionRunConfiguration : IEntityTypeConfiguration<StoredConnectionRun>
{
    public void Configure(EntityTypeBuilder<StoredConnectionRun> entity)
    {
        entity.ToTable("ProjectConnectionRuns");
        entity.Ignore(run => run.Definition);
        entity.Ignore(run => run.Rule);
        entity.Ignore(run => run.Steps);
        entity.HasKey(run => run.Id);
        entity.Property(run => run.Event).HasMaxLength(100);
        entity.Property(run => run.Status).HasMaxLength(24);
        entity.Property(run => run.Detail).HasMaxLength(400);
        entity.Property(run => run.Version).IsRowVersion();
        entity.HasIndex(run => new { run.ConnectionId, run.RuleId, run.EventPosition }).IsUnique();
        entity.HasIndex(run => new { run.ProjectId, run.OccurredAtUtc });
        entity.HasIndex(run => new { run.ProjectId, run.RootPosition });
        entity.HasOne<Project>().WithMany().HasForeignKey(run => run.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConnectionExecutionContext
{
    public IReadOnlyList<Guid> Chain { get; set; } = [];
    public long? RootPosition { get; set; }
}

internal sealed class NullConnectionChangeNotifier : IConnectionChangeNotifier
{
    public Task Changed(ConnectionScope scope, ConnectionNodeRef target, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class ConnectionEvents(ApplicationDbContext db, TimeProvider clock, ConnectionExecutionContext execution) : IConnectionEvents
{
    public Task Enqueue(Guid organizationId, Guid projectId, Guid actorUserId, ConnectionEventDraft draft, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        db.Set<QueuedConnectionEvent>().Add(new()
        {
            ProjectId = projectId,
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            DraftJson = ConnectionStorage.Write(draft),
            ChainJson = ConnectionStorage.Write(execution.Chain),
            RootPosition = execution.RootPosition,
            OccurredAtUtc = draft.OccurredAtUtc ?? clock.GetUtcNow()
        });
        return Task.CompletedTask;
    }
}
