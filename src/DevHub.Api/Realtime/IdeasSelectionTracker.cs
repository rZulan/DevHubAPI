namespace DevHub.Api.Realtime;

public sealed record IdeasSelection(Guid UserId, string Username, IReadOnlyList<string> ShapeIds);
public sealed record IdeasSelectionLeave(Guid OrganizationId, Guid ProjectId, IdeasSelection Selection);

public sealed class IdeasSelectionTracker
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(Guid OrganizationId, Guid ProjectId), Dictionary<Guid, Entry>> _canvases = [];
    private readonly Dictionary<string, (Guid OrganizationId, Guid ProjectId, Guid UserId)> _connections = [];

    private sealed class Entry(string username)
    {
        public string Username { get; } = username;
        public Dictionary<string, string[]> Connections { get; } = [];
    }

    private static IdeasSelection Snapshot(Guid userId, Entry entry) => new(userId, entry.Username,
        entry.Connections.Values.SelectMany(ids => ids).Distinct(StringComparer.Ordinal).Take(200).ToArray());

    public IReadOnlyList<IdeasSelection> Join(Guid organizationId, Guid projectId, Guid userId, string connectionId, string username)
    {
        lock (_gate)
        {
            var key = (organizationId, projectId);
            if (!_canvases.TryGetValue(key, out var users)) _canvases[key] = users = [];
            if (!users.TryGetValue(userId, out var entry)) users[userId] = entry = new(username);
            entry.Connections[connectionId] = [];
            _connections[connectionId] = (organizationId, projectId, userId);
            return users.Where(pair => pair.Key != userId).Select(pair => Snapshot(pair.Key, pair.Value))
                .Where(selection => selection.ShapeIds.Count > 0).ToArray();
        }
    }

    public IdeasSelection? Update(string connectionId, IReadOnlyList<string> shapeIds)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(connectionId, out var session)) return null;
            var users = _canvases[(session.OrganizationId, session.ProjectId)];
            var current = users[session.UserId];
            var normalized = shapeIds.Distinct(StringComparer.Ordinal).Take(200).ToArray();
            current.Connections[connectionId] = normalized;
            return Snapshot(session.UserId, current);
        }
    }

    public IdeasSelectionLeave? Leave(string connectionId)
    {
        lock (_gate)
        {
            if (!_connections.Remove(connectionId, out var session)) return null;
            var key = (session.OrganizationId, session.ProjectId);
            if (_canvases.TryGetValue(key, out var users))
            {
                var entry = users[session.UserId];
                entry.Connections.Remove(connectionId);
                var selection = Snapshot(session.UserId, entry);
                if (entry.Connections.Count == 0) users.Remove(session.UserId);
                if (users.Count == 0) _canvases.Remove(key);
                return new(session.OrganizationId, session.ProjectId, selection);
            }
            return null;
        }
    }
}
