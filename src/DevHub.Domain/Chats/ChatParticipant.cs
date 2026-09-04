using DevHub.Domain.Users;

namespace DevHub.Domain.Chats;

public sealed class ChatParticipant
{
    private ChatParticipant()
    {
    }

    private ChatParticipant(
        Guid conversationId,
        Guid userId,
        ChatParticipantRole role,
        DateTimeOffset joinedAtUtc)
    {
        ConversationId = conversationId;
        UserId = userId;
        Role = role;
        JoinedAtUtc = joinedAtUtc;
    }

    public Guid ConversationId { get; private set; }
    public Guid UserId { get; private set; }
    public ChatParticipantRole Role { get; private set; }
    public DateTimeOffset JoinedAtUtc { get; private set; }
    public DateTimeOffset? LastReadAtUtc { get; private set; }
    public ChatConversation Conversation { get; private set; } = null!;
    public User User { get; private set; } = null!;

    internal static ChatParticipant Create(
        Guid conversationId,
        Guid userId,
        ChatParticipantRole role,
        DateTimeOffset joinedAtUtc) =>
        new(conversationId, userId, role, joinedAtUtc);

    public void SetRole(ChatParticipantRole role) => Role = role;

    public void MarkRead(DateTimeOffset readAtUtc)
    {
        if (!LastReadAtUtc.HasValue || readAtUtc > LastReadAtUtc.Value)
        {
            LastReadAtUtc = readAtUtc;
        }
    }
}
