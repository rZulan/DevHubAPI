using DevHub.Application.Common;

namespace DevHub.Application.Chats;

public sealed record ChatMemberResponse(
    Guid UserId,
    string Name,
    string Username,
    string? AvatarUrl,
    string Role,
    bool IsCreator);

public sealed record ChatMessageResponse(
    Guid Id,
    Guid ConversationId,
    Guid SenderUserId,
    Guid ClientMessageId,
    string Body,
    DateTimeOffset SentAtUtc);

public sealed record ChatConversationResponse(
    Guid Id,
    Guid OrganizationId,
    string Kind,
    string Name,
    Guid? TeamId,
    Guid? CreatorUserId,
    bool IsSpecial,
    int UnreadCount,
    ChatMessageResponse? LastMessage,
    IReadOnlyList<ChatMemberResponse> Members);

public sealed record ChatDeliveryResponse(
    ChatMessageResponse Message,
    IReadOnlyList<Guid> RecipientUserIds);

public interface IChatService
{
    Task<Result<IReadOnlyList<ChatConversationResponse>>> ListConversationsAsync(
        Guid organizationId,
        Guid requestingUserId,
        CancellationToken cancellationToken = default);

    Task<Result<ChatConversationResponse>> CreateDirectAsync(
        Guid organizationId,
        Guid requestingUserId,
        Guid otherUserId,
        CancellationToken cancellationToken = default);

    Task<Result<ChatConversationResponse>> CreateGroupAsync(
        Guid organizationId,
        Guid requestingUserId,
        string name,
        IReadOnlyCollection<Guid> memberUserIds,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ChatMessageResponse>>> ListMessagesAsync(
        Guid organizationId,
        Guid conversationId,
        Guid requestingUserId,
        DateTimeOffset? before,
        int take,
        CancellationToken cancellationToken = default);

    Task<Result<ChatDeliveryResponse>> SendMessageAsync(
        Guid organizationId,
        Guid conversationId,
        Guid requestingUserId,
        Guid clientMessageId,
        string body,
        CancellationToken cancellationToken = default);

    Task<Result<ChatConversationResponse>> MarkReadAsync(
        Guid organizationId,
        Guid conversationId,
        Guid requestingUserId,
        CancellationToken cancellationToken = default);

    Task<Result<ChatConversationResponse>> AddMemberAsync(
        Guid organizationId,
        Guid conversationId,
        Guid requestingUserId,
        Guid memberUserId,
        CancellationToken cancellationToken = default);

    Task<Result<ChatConversationResponse>> RemoveMemberAsync(
        Guid organizationId,
        Guid conversationId,
        Guid requestingUserId,
        Guid memberUserId,
        CancellationToken cancellationToken = default);

    Task<Result<ChatConversationResponse>> SetAdminAsync(
        Guid organizationId,
        Guid conversationId,
        Guid requestingUserId,
        Guid memberUserId,
        bool isAdmin,
        CancellationToken cancellationToken = default);
}

public sealed record EncryptedChatContent(
    byte[] CipherText,
    byte[] Nonce,
    byte[] AuthenticationTag,
    string KeyVersion);

public interface IChatMessageCipher
{
    EncryptedChatContent Encrypt(string plainText);
    string Decrypt(byte[] cipherText, byte[] nonce, byte[] authenticationTag, string keyVersion);
}

public static class ChatErrors
{
    public static readonly Error OrganizationNotFound = new(
        "Chats.OrganizationNotFound",
        "The organization was not found or is not available to you.",
        ErrorType.NotFound);

    public static readonly Error ConversationNotFound = new(
        "Chats.NotFound",
        "The conversation was not found or is not available to you.",
        ErrorType.NotFound);

    public static readonly Error MemberRequired = new(
        "Chats.MemberRequired",
        "Only conversation members can perform this action.",
        ErrorType.Forbidden);

    public static readonly Error AdminRequired = new(
        "Chats.AdminRequired",
        "Only a group administrator can manage members.",
        ErrorType.Forbidden);

    public static readonly Error CreatorRequired = new(
        "Chats.CreatorRequired",
        "Only the original group creator can change administrator permissions.",
        ErrorType.Forbidden);

    public static readonly Error FixedMembership = new(
        "Chats.FixedMembership",
        "Membership for this conversation is managed automatically.",
        ErrorType.Conflict);

    public static readonly Error CreatorProtected = new(
        "Chats.CreatorProtected",
        "The original group creator cannot be removed or demoted.",
        ErrorType.Conflict);

    public static readonly Error InvalidMember = new(
        "Chats.InvalidMember",
        "Every selected person must be a member of this organization.",
        ErrorType.Validation);

    public static Error Invalid(IReadOnlyDictionary<string, string[]> errors) => new(
        "Chats.ValidationFailed",
        "One or more chat fields are invalid.",
        ErrorType.Validation,
        errors);
}
