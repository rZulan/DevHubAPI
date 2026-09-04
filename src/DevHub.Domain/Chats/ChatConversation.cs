using DevHub.Domain.Common;

namespace DevHub.Domain.Chats;

public enum ChatConversationKind
{
    Organization = 0,
    Team = 1,
    Direct = 2,
    Group = 3
}

public enum ChatParticipantRole
{
    Member = 0,
    Admin = 1
}

public sealed class ChatConversation : BaseEntity
{
    private readonly List<ChatParticipant> _participants = [];
    private readonly List<ChatMessage> _messages = [];

    private ChatConversation()
    {
    }

    private ChatConversation(
        Guid organizationId,
        ChatConversationKind kind,
        string name,
        Guid? creatorUserId,
        Guid? teamId,
        string? directKey,
        DateTimeOffset createdAtUtc)
        : base(Guid.NewGuid(), createdAtUtc)
    {
        OrganizationId = organizationId;
        Kind = kind;
        Name = name.Trim();
        CreatorUserId = creatorUserId;
        TeamId = teamId;
        DirectKey = directKey;
    }

    public Guid OrganizationId { get; private set; }
    public ChatConversationKind Kind { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid? CreatorUserId { get; private set; }
    public Guid? TeamId { get; private set; }
    public string? DirectKey { get; private set; }
    public IReadOnlyCollection<ChatParticipant> Participants => _participants;
    public IReadOnlyCollection<ChatMessage> Messages => _messages;
    public bool IsSpecial => Kind is ChatConversationKind.Organization or ChatConversationKind.Team;

    public static ChatConversation CreateOrganization(
        Guid organizationId,
        string organizationName,
        DateTimeOffset createdAtUtc) =>
        new(organizationId, ChatConversationKind.Organization, $"{organizationName} — Organization", null, null, null, createdAtUtc);

    public static ChatConversation CreateTeam(
        Guid organizationId,
        Guid teamId,
        string teamName,
        DateTimeOffset createdAtUtc) =>
        new(organizationId, ChatConversationKind.Team, teamName, null, teamId, null, createdAtUtc);

    public static ChatConversation CreateDirect(
        Guid organizationId,
        Guid firstUserId,
        Guid secondUserId,
        DateTimeOffset createdAtUtc) =>
        new(organizationId, ChatConversationKind.Direct, "Direct message", null, null, CreateDirectKey(firstUserId, secondUserId), createdAtUtc);

    public static ChatConversation CreateGroup(
        Guid organizationId,
        Guid creatorUserId,
        string name,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new(organizationId, ChatConversationKind.Group, name, creatorUserId, null, null, createdAtUtc);
    }

    public void Rename(string name, DateTimeOffset updatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        MarkUpdated(updatedAtUtc);
    }

    public ChatParticipant AddParticipant(
        Guid userId,
        ChatParticipantRole role,
        DateTimeOffset joinedAtUtc)
    {
        var existing = _participants.SingleOrDefault(participant => participant.UserId == userId);
        if (existing is not null)
        {
            existing.SetRole(role);
            return existing;
        }

        var participant = ChatParticipant.Create(Id, userId, role, joinedAtUtc);
        _participants.Add(participant);
        return participant;
    }

    public bool RemoveParticipant(Guid userId)
    {
        var participant = _participants.SingleOrDefault(candidate => candidate.UserId == userId);
        return participant is not null && _participants.Remove(participant);
    }

    public bool HasParticipant(Guid userId) =>
        _participants.Any(participant => participant.UserId == userId);

    public static string CreateDirectKey(Guid firstUserId, Guid secondUserId)
    {
        var ids = new[] { firstUserId, secondUserId }.OrderBy(id => id).ToArray();
        return $"{ids[0]:N}:{ids[1]:N}";
    }
}
