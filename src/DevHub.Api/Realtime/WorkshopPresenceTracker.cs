namespace DevHub.Api.Realtime;

public sealed record WorkshopPresence(Guid UserId, string Status);

public sealed record PresenceJoinResult(
    bool IsFirstConnection,
    IReadOnlyList<WorkshopPresence> Members);

public sealed record PresenceLeaveResult(
    Guid OrganizationId,
    Guid UserId,
    bool IsNowOffline);

public sealed class WorkshopPresenceTracker(TimeProvider timeProvider)
{
    // Background browser tabs can throttle timers to roughly once per minute.
    // Keep the fallback alive through that throttling without treating lost focus as offline.
    private static readonly TimeSpan HeartbeatLifetime = TimeSpan.FromMinutes(2);
    private static readonly HashSet<string> AllowedStatuses =
        ["online", "away", "dnd", "invisible"];
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Dictionary<Guid, HashSet<string>>> _organizations = [];
    private readonly Dictionary<string, (Guid OrganizationId, Guid UserId)> _connections = [];
    private readonly Dictionary<Guid, Dictionary<Guid, DateTimeOffset>> _heartbeats = [];
    private readonly Dictionary<Guid, Dictionary<Guid, string>> _statuses = [];

    public static string NormalizeStatus(string? status) =>
        status is not null && AllowedStatuses.Contains(status.ToLowerInvariant())
            ? status.ToLowerInvariant()
            : "online";

    public PresenceJoinResult Join(
        Guid organizationId,
        Guid userId,
        string connectionId,
        string? status)
    {
        lock (_lock)
        {
            if (!_organizations.TryGetValue(organizationId, out var users))
            {
                users = [];
                _organizations[organizationId] = users;
            }

            if (!users.TryGetValue(userId, out var connections))
            {
                connections = [];
                users[userId] = connections;
            }

            var isFirstConnection = connections.Count == 0;
            connections.Add(connectionId);
            _connections[connectionId] = (organizationId, userId);
            SetStatusCore(organizationId, userId, status);

            return new PresenceJoinResult(
                isFirstConnection,
                GetMembers(organizationId, timeProvider.GetUtcNow()));
        }
    }

    public IReadOnlyList<WorkshopPresence> Heartbeat(
        Guid organizationId,
        Guid userId,
        string? status)
    {
        lock (_lock)
        {
            var now = timeProvider.GetUtcNow();
            if (!_heartbeats.TryGetValue(organizationId, out var users))
            {
                users = [];
                _heartbeats[organizationId] = users;
            }

            users[userId] = now;
            SetStatusCore(organizationId, userId, status);
            return GetMembers(organizationId, now);
        }
    }

    public string SetStatus(Guid organizationId, Guid userId, string? status)
    {
        lock (_lock)
        {
            return SetStatusCore(organizationId, userId, status);
        }
    }

    public PresenceLeaveResult? Leave(string connectionId)
    {
        lock (_lock)
        {
            if (!_connections.Remove(connectionId, out var presence) ||
                !_organizations.TryGetValue(presence.OrganizationId, out var users) ||
                !users.TryGetValue(presence.UserId, out var connections))
            {
                return null;
            }

            connections.Remove(connectionId);
            if (connections.Count == 0) users.Remove(presence.UserId);
            if (users.Count == 0) _organizations.Remove(presence.OrganizationId);

            var hasRecentHeartbeat = _heartbeats.TryGetValue(presence.OrganizationId, out var heartbeatUsers) &&
                heartbeatUsers.TryGetValue(presence.UserId, out var lastSeenAtUtc) &&
                timeProvider.GetUtcNow() - lastSeenAtUtc <= HeartbeatLifetime;

            return new PresenceLeaveResult(
                presence.OrganizationId,
                presence.UserId,
                connections.Count == 0 && !hasRecentHeartbeat);
        }
    }

    private string SetStatusCore(Guid organizationId, Guid userId, string? status)
    {
        var normalizedStatus = NormalizeStatus(status);
        if (!_statuses.TryGetValue(organizationId, out var users))
        {
            users = [];
            _statuses[organizationId] = users;
        }

        users[userId] = normalizedStatus;
        return normalizedStatus;
    }

    private IReadOnlyList<WorkshopPresence> GetMembers(Guid organizationId, DateTimeOffset now)
    {
        var activeUserIds = _organizations.TryGetValue(organizationId, out var connectedUsers)
            ? connectedUsers.Keys.ToHashSet()
            : [];

        if (_heartbeats.TryGetValue(organizationId, out var heartbeatUsers))
        {
            foreach (var (userId, lastSeenAtUtc) in heartbeatUsers.ToArray())
            {
                if (now - lastSeenAtUtc <= HeartbeatLifetime)
                {
                    activeUserIds.Add(userId);
                }
                else
                {
                    heartbeatUsers.Remove(userId);
                }
            }

            if (heartbeatUsers.Count == 0) _heartbeats.Remove(organizationId);
        }

        _statuses.TryGetValue(organizationId, out var statuses);
        return activeUserIds
            .Select(userId => new WorkshopPresence(
                userId,
                statuses?.GetValueOrDefault(userId) ?? "online"))
            .Where(member => member.Status != "invisible")
            .ToArray();
    }
}
