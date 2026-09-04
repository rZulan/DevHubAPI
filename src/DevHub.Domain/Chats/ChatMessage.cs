using DevHub.Domain.Common;
using DevHub.Domain.Users;

namespace DevHub.Domain.Chats;

public sealed class ChatMessage : BaseEntity
{
    private ChatMessage()
    {
    }

    private ChatMessage(
        Guid conversationId,
        Guid senderUserId,
        Guid clientMessageId,
        byte[] cipherText,
        byte[] nonce,
        byte[] authenticationTag,
        string keyVersion,
        DateTimeOffset createdAtUtc)
        : base(Guid.NewGuid(), createdAtUtc)
    {
        ConversationId = conversationId;
        SenderUserId = senderUserId;
        ClientMessageId = clientMessageId;
        CipherText = cipherText;
        Nonce = nonce;
        AuthenticationTag = authenticationTag;
        KeyVersion = keyVersion;
    }

    public Guid ConversationId { get; private set; }
    public Guid SenderUserId { get; private set; }
    public Guid ClientMessageId { get; private set; }
    public byte[] CipherText { get; private set; } = [];
    public byte[] Nonce { get; private set; } = [];
    public byte[] AuthenticationTag { get; private set; } = [];
    public string KeyVersion { get; private set; } = string.Empty;
    public ChatConversation Conversation { get; private set; } = null!;
    public User Sender { get; private set; } = null!;

    public static ChatMessage Create(
        Guid conversationId,
        Guid senderUserId,
        Guid clientMessageId,
        byte[] cipherText,
        byte[] nonce,
        byte[] authenticationTag,
        string keyVersion,
        DateTimeOffset createdAtUtc) =>
        new(conversationId, senderUserId, clientMessageId, cipherText, nonce, authenticationTag, keyVersion, createdAtUtc);
}
