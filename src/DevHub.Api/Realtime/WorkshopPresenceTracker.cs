namespace DevHub.Api.Realtime;

public sealed record WorkshopPresence(Guid UserId, string Status);
public sealed record PresenceJoinResult(bool StatusChanged, string Status, IReadOnlyList<WorkshopPresence> Members);
public sealed record PresenceHeartbeatResult(bool StatusChanged, string Status, IReadOnlyList<WorkshopPresence> Members);
public sealed record PresenceLeaveResult(Guid OrganizationId, Guid UserId, bool IsNowOffline);

public sealed class WorkshopPresenceTracker(TimeProvider timeProvider)
{
    private static readonly TimeSpan HeartbeatLifetime = TimeSpan.FromMinutes(2);
    // Fixed shards bound lock overhead and let unrelated organizations progress concurrently.
    private readonly Shard[] _shards = Enumerable.Range(0, 64).Select(_ => new Shard()).ToArray();
    private sealed class Shard
    {
        public readonly Lock Gate = new();
        public readonly Dictionary<Guid, Dictionary<Guid, Entry>> Organizations = [];
        public readonly Dictionary<string, Dictionary<Guid, Guid>> Connections = [];
    }
    private sealed class Entry
    {
        public readonly HashSet<string> Connections = [];
        public DateTimeOffset? Heartbeat;
        public string Status = "online";
    }
    private Shard GetShard(Guid id) => _shards[(uint)id.GetHashCode() % (uint)_shards.Length];
    public static string NormalizeStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "away" => "away", "dnd" => "dnd", "invisible" => "invisible", _ => "online"
    };
    private static bool IsActive(Entry entry, DateTimeOffset now) =>
        entry.Connections.Count > 0 || entry.Heartbeat is { } lastSeen && now - lastSeen <= HeartbeatLifetime;
    private static string PublicStatus(Entry entry, DateTimeOffset now) =>
        IsActive(entry, now) && entry.Status != "invisible" ? entry.Status : "offline";
    private static Entry GetEntry(Shard shard, Guid organizationId, Guid userId)
    {
        if (!shard.Organizations.TryGetValue(organizationId, out var users))
            shard.Organizations[organizationId] = users = [];
        if (!users.TryGetValue(userId, out var entry)) users[userId] = entry = new();
        return entry;
    }
    private static WorkshopPresence[] GetMembers(Shard shard, Guid organizationId, DateTimeOffset now) =>
        shard.Organizations[organizationId]
            .Select(pair => new WorkshopPresence(pair.Key, PublicStatus(pair.Value, now)))
            .Where(member => member.Status != "offline").ToArray();

    public PresenceJoinResult Join(Guid organizationId, Guid userId, string connectionId, string? status)
    {
        var shard = GetShard(organizationId);
        lock (shard.Gate)
        {
            var now = timeProvider.GetUtcNow();
            var entry = GetEntry(shard, organizationId, userId);
            var previous = PublicStatus(entry, now);
            entry.Connections.Add(connectionId);
            entry.Heartbeat = null; // Transfer the fallback lease to the live connection.
            entry.Status = NormalizeStatus(status);
            if (!shard.Connections.TryGetValue(connectionId, out var organizations))
                shard.Connections[connectionId] = organizations = [];
            organizations[organizationId] = userId;
            var current = PublicStatus(entry, now);
            return new(previous != current, current, GetMembers(shard, organizationId, now));
        }
    }
    public PresenceHeartbeatResult Heartbeat(Guid organizationId, Guid userId, string? status)
    {
        var shard = GetShard(organizationId);
        lock (shard.Gate)
        {
            var now = timeProvider.GetUtcNow();
            var entry = GetEntry(shard, organizationId, userId);
            var previous = PublicStatus(entry, now);
            entry.Heartbeat = now;
            entry.Status = NormalizeStatus(status);
            var current = PublicStatus(entry, now);
            return new(previous != current, current, GetMembers(shard, organizationId, now));
        }
    }
    public string? SetStatus(Guid organizationId, Guid userId, string connectionId, string? status)
    {
        var shard = GetShard(organizationId);
        lock (shard.Gate)
        {
            if (!shard.Organizations.TryGetValue(organizationId, out var users) ||
                !users.TryGetValue(userId, out var entry) || !entry.Connections.Contains(connectionId)) return null;
            var now = timeProvider.GetUtcNow();
            var previous = PublicStatus(entry, now);
            entry.Status = NormalizeStatus(status);
            var current = PublicStatus(entry, now);
            return previous == current ? null : current;
        }
    }
    public IReadOnlyList<PresenceLeaveResult> Leave(string connectionId)
    {
        List<PresenceLeaveResult> results = [];
        foreach (var shard in _shards)
        {
            lock (shard.Gate)
            {
                if (!shard.Connections.Remove(connectionId, out var organizations)) continue;
                var now = timeProvider.GetUtcNow();
                foreach (var (organizationId, userId) in organizations)
                {
                    var users = shard.Organizations[organizationId];
                    var entry = users[userId];
                    var wasVisible = PublicStatus(entry, now) != "offline";
                    entry.Connections.Remove(connectionId);
                    if (IsActive(entry, now)) continue;
                    users.Remove(userId);
                    if (users.Count == 0) shard.Organizations.Remove(organizationId);
                    results.Add(new(organizationId, userId, wasVisible));
                }
            }
        }
        return results;
    }
    public IReadOnlyList<PresenceLeaveResult> ExpireHeartbeats()
    {
        List<PresenceLeaveResult> results = [];
        var now = timeProvider.GetUtcNow();
        foreach (var shard in _shards)
        {
            lock (shard.Gate)
            {
                foreach (var (organizationId, users) in shard.Organizations.ToArray())
                {
                    foreach (var (userId, entry) in users.ToArray())
                    {
                        if (IsActive(entry, now)) continue;
                        users.Remove(userId);
                        results.Add(new(organizationId, userId, entry.Status != "invisible"));
                    }
                    if (users.Count == 0) shard.Organizations.Remove(organizationId);
                }
            }
        }
        return results;
    }
}
