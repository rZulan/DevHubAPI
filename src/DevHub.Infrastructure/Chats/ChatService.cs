using DevHub.Application.Chats;
using DevHub.Application.Common;
using DevHub.Domain.Chats;
using DevHub.Domain.Organizations;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Chats;

internal sealed class ChatService(
    ApplicationDbContext dbContext,
    IChatMessageCipher cipher,
    TimeProvider timeProvider) : IChatService
{
    private const int MaximumMessageLength = 4000;
    private const int MaximumGroupNameLength = 150;

    public async Task<Result<IReadOnlyList<ChatConversationResponse>>> ListConversationsAsync(
        Guid organizationId,
        Guid requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var organization = await GetOrganizationAsync(organizationId, cancellationToken);
        if (organization is null || !organization.HasMember(requestingUserId))
        {
            return Result<IReadOnlyList<ChatConversationResponse>>.Failure(ChatErrors.OrganizationNotFound);
        }

        await SynchronizeAsync(organization, cancellationToken);
        var conversations = await ConversationQuery()
            .Where(conversation =>
                conversation.OrganizationId == organizationId &&
                conversation.Participants.Any(participant => participant.UserId == requestingUserId))
            .ToListAsync(cancellationToken);

        var result = conversations
            .Where(conversation => conversation.Kind != ChatConversationKind.Direct || conversation.Participants.Count == 2)
            .OrderBy(conversation => conversation.Kind switch
            {
                ChatConversationKind.Organization => 0,
                ChatConversationKind.Team => 1,
                ChatConversationKind.Group => 2,
                _ => 3
            })
            .ThenByDescending(conversation => conversation.Messages.Max(message => (DateTimeOffset?)message.CreatedAtUtc) ?? conversation.CreatedAtUtc)
            .Select(conversation => ToResponse(conversation, requestingUserId))
            .ToArray();
        return Result<IReadOnlyList<ChatConversationResponse>>.Success(result);
    }

    public async Task<Result<ChatConversationResponse>> CreateDirectAsync(
        Guid organizationId,
        Guid requestingUserId,
        Guid otherUserId,
        CancellationToken cancellationToken = default)
    {
        var organization = await GetOrganizationAsync(organizationId, cancellationToken);
        if (organization is null || !organization.HasMember(requestingUserId))
        {
            return Result<ChatConversationResponse>.Failure(ChatErrors.OrganizationNotFound);
        }
        if (otherUserId == requestingUserId || !organization.HasMember(otherUserId))
        {
            return Result<ChatConversationResponse>.Failure(ChatErrors.InvalidMember);
        }

        await SynchronizeAsync(organization, cancellationToken);
        var directKey = ChatConversation.CreateDirectKey(requestingUserId, otherUserId);
        var existing = await ConversationQuery().SingleOrDefaultAsync(
            conversation => conversation.OrganizationId == organizationId && conversation.DirectKey == directKey,
            cancellationToken);
        if (existing is not null)
        {
            return Result<ChatConversationResponse>.Success(ToResponse(existing, requestingUserId));
        }

        var now = timeProvider.GetUtcNow();
        var conversation = ChatConversation.CreateDirect(organizationId, requestingUserId, otherUserId, now);
        conversation.AddParticipant(requestingUserId, ChatParticipantRole.Member, now);
        conversation.AddParticipant(otherUserId, ChatParticipantRole.Member, now);
        dbContext.ChatConversations.Add(conversation);
        await dbContext.SaveChangesAsync(cancellationToken);
        await LoadUsersAsync(conversation, cancellationToken);
        return Result<ChatConversationResponse>.Success(ToResponse(conversation, requestingUserId));
    }

    public async Task<Result<ChatConversationResponse>> CreateGroupAsync(
        Guid organizationId,
        Guid requestingUserId,
        string name,
        IReadOnlyCollection<Guid> memberUserIds,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaximumGroupNameLength)
        {
            errors[nameof(name)] = [$"A group name between 1 and {MaximumGroupNameLength} characters is required."];
        }

        var organization = await GetOrganizationAsync(organizationId, cancellationToken);
        if (organization is null || !organization.HasMember(requestingUserId))
        {
            return Result<ChatConversationResponse>.Failure(ChatErrors.OrganizationNotFound);
        }

        var selectedIds = memberUserIds.Where(id => id != requestingUserId).Distinct().ToArray();
        if (selectedIds.Length == 0)
        {
            errors[nameof(memberUserIds)] = ["Select at least one other organization member."];
        }
        if (selectedIds.Any(id => !organization.HasMember(id)))
        {
            return Result<ChatConversationResponse>.Failure(ChatErrors.InvalidMember);
        }
        if (errors.Count > 0)
        {
            return Result<ChatConversationResponse>.Failure(ChatErrors.Invalid(errors));
        }

        var now = timeProvider.GetUtcNow();
        var conversation = ChatConversation.CreateGroup(organizationId, requestingUserId, name, now);
        conversation.AddParticipant(requestingUserId, ChatParticipantRole.Admin, now);
        foreach (var memberUserId in selectedIds)
        {
            conversation.AddParticipant(memberUserId, ChatParticipantRole.Member, now);
        }

        dbContext.ChatConversations.Add(conversation);
        await dbContext.SaveChangesAsync(cancellationToken);
        await LoadUsersAsync(conversation, cancellationToken);
        return Result<ChatConversationResponse>.Success(ToResponse(conversation, requestingUserId));
    }

    public async Task<Result<IReadOnlyList<ChatMessageResponse>>> ListMessagesAsync(
        Guid organizationId,
        Guid conversationId,
        Guid requestingUserId,
        DateTimeOffset? before,
        int take,
        CancellationToken cancellationToken = default)
    {
        var access = await GetAccessibleConversationAsync(organizationId, conversationId, requestingUserId, cancellationToken);
        if (!access.IsSuccess)
        {
            return Result<IReadOnlyList<ChatMessageResponse>>.Failure(access.Error!);
        }

        var boundedTake = Math.Clamp(take, 1, 100);
        var query = dbContext.ChatMessages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId);
        if (before.HasValue)
        {
            query = query.Where(message => message.CreatedAtUtc < before.Value);
        }

        var messages = await query
            .OrderByDescending(message => message.CreatedAtUtc)
            .Take(boundedTake)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<ChatMessageResponse>>.Success(
            messages.OrderBy(message => message.CreatedAtUtc).Select(ToMessageResponse).ToArray());
    }

    public async Task<Result<ChatDeliveryResponse>> SendMessageAsync(
        Guid organizationId,
        Guid conversationId,
        Guid requestingUserId,
        Guid clientMessageId,
        string body,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (clientMessageId == Guid.Empty)
        {
            errors[nameof(clientMessageId)] = ["A client message identifier is required."];
        }
        if (string.IsNullOrWhiteSpace(body) || body.Trim().Length > MaximumMessageLength)
        {
            errors[nameof(body)] = [$"A message between 1 and {MaximumMessageLength} characters is required."];
        }
        if (errors.Count > 0)
        {
            return Result<ChatDeliveryResponse>.Failure(ChatErrors.Invalid(errors));
        }

        var access = await GetAccessibleConversationAsync(organizationId, conversationId, requestingUserId, cancellationToken);
        if (!access.IsSuccess)
        {
            return Result<ChatDeliveryResponse>.Failure(access.Error!);
        }
        var conversation = access.Value!;

        var duplicate = await dbContext.ChatMessages.SingleOrDefaultAsync(
            message => message.ConversationId == conversationId && message.ClientMessageId == clientMessageId,
            cancellationToken);
        ChatMessage message;
        if (duplicate is not null)
        {
            message = duplicate;
        }
        else
        {
            var encrypted = cipher.Encrypt(body.Trim());
            message = ChatMessage.Create(
                conversationId,
                requestingUserId,
                clientMessageId,
                encrypted.CipherText,
                encrypted.Nonce,
                encrypted.AuthenticationTag,
                encrypted.KeyVersion,
                timeProvider.GetUtcNow());
            dbContext.ChatMessages.Add(message);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var recipientIds = conversation.Participants.Select(participant => participant.UserId).Distinct().ToArray();
        return Result<ChatDeliveryResponse>.Success(new ChatDeliveryResponse(ToMessageResponse(message), recipientIds));
    }

    public async Task<Result<ChatConversationResponse>> MarkReadAsync(
        Guid organizationId,
        Guid conversationId,
        Guid requestingUserId,
        CancellationToken cancellationToken = default)
    {
        var access = await GetAccessibleConversationAsync(organizationId, conversationId, requestingUserId, cancellationToken);
        if (!access.IsSuccess) return Result<ChatConversationResponse>.Failure(access.Error!);

        access.Value!.Participants.Single(participant => participant.UserId == requestingUserId)
            .MarkRead(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<ChatConversationResponse>.Success(ToResponse(access.Value, requestingUserId));
    }

    public async Task<Result<ChatConversationResponse>> AddMemberAsync(
        Guid organizationId,
        Guid conversationId,
        Guid requestingUserId,
        Guid memberUserId,
        CancellationToken cancellationToken = default)
    {
        var access = await GetManageableGroupAsync(organizationId, conversationId, requestingUserId, cancellationToken);
        if (!access.IsSuccess) return Result<ChatConversationResponse>.Failure(access.Error!);
        var (organization, conversation) = access.Value!;
        if (!organization.HasMember(memberUserId)) return Result<ChatConversationResponse>.Failure(ChatErrors.InvalidMember);

        conversation.AddParticipant(memberUserId, ChatParticipantRole.Member, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        await LoadUsersAsync(conversation, cancellationToken);
        return Result<ChatConversationResponse>.Success(ToResponse(conversation, requestingUserId));
    }

    public async Task<Result<ChatConversationResponse>> RemoveMemberAsync(
        Guid organizationId,
        Guid conversationId,
        Guid requestingUserId,
        Guid memberUserId,
        CancellationToken cancellationToken = default)
    {
        var access = await GetManageableGroupAsync(organizationId, conversationId, requestingUserId, cancellationToken);
        if (!access.IsSuccess) return Result<ChatConversationResponse>.Failure(access.Error!);
        var conversation = access.Value!.Conversation;
        if (conversation.CreatorUserId == memberUserId) return Result<ChatConversationResponse>.Failure(ChatErrors.CreatorProtected);

        var target = conversation.Participants.SingleOrDefault(participant => participant.UserId == memberUserId);
        if (target?.Role == ChatParticipantRole.Admin && conversation.CreatorUserId != requestingUserId)
        {
            return Result<ChatConversationResponse>.Failure(ChatErrors.CreatorRequired);
        }
        if (target is not null)
        {
            conversation.RemoveParticipant(memberUserId);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return Result<ChatConversationResponse>.Success(ToResponse(conversation, requestingUserId));
    }

    public async Task<Result<ChatConversationResponse>> SetAdminAsync(
        Guid organizationId,
        Guid conversationId,
        Guid requestingUserId,
        Guid memberUserId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var access = await GetAccessibleConversationAsync(organizationId, conversationId, requestingUserId, cancellationToken);
        if (!access.IsSuccess) return Result<ChatConversationResponse>.Failure(access.Error!);
        var conversation = access.Value!;
        if (conversation.Kind != ChatConversationKind.Group) return Result<ChatConversationResponse>.Failure(ChatErrors.FixedMembership);
        if (conversation.CreatorUserId != requestingUserId) return Result<ChatConversationResponse>.Failure(ChatErrors.CreatorRequired);
        if (conversation.CreatorUserId == memberUserId && !isAdmin) return Result<ChatConversationResponse>.Failure(ChatErrors.CreatorProtected);

        var target = conversation.Participants.SingleOrDefault(participant => participant.UserId == memberUserId);
        if (target is null) return Result<ChatConversationResponse>.Failure(ChatErrors.ConversationNotFound);
        target.SetRole(isAdmin ? ChatParticipantRole.Admin : ChatParticipantRole.Member);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<ChatConversationResponse>.Success(ToResponse(conversation, requestingUserId));
    }

    private async Task<Organization?> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Organizations
            .AsSplitQuery()
            .Include(organization => organization.Members)
            .Include(organization => organization.Teams)
                .ThenInclude(team => team.Members)
            .SingleOrDefaultAsync(organization => organization.Id == organizationId, cancellationToken);

    private async Task SynchronizeAsync(Organization organization, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var conversations = await dbContext.ChatConversations
            .Include(conversation => conversation.Participants)
            .Where(conversation => conversation.OrganizationId == organization.Id)
            .ToListAsync(cancellationToken);
        var organizationChat = conversations.SingleOrDefault(conversation => conversation.Kind == ChatConversationKind.Organization);
        if (organizationChat is null)
        {
            organizationChat = ChatConversation.CreateOrganization(organization.Id, organization.Name, now);
            dbContext.ChatConversations.Add(organizationChat);
            conversations.Add(organizationChat);
        }
        else if (organizationChat.Name != $"{organization.Name} — Organization")
        {
            organizationChat.Rename($"{organization.Name} — Organization", now);
        }
        SynchronizeParticipants(
            organizationChat,
            organization.Members.Select(member => member.UserId),
            userId => userId == organization.OwnerUserId,
            now);

        foreach (var team in organization.Teams)
        {
            var teamChat = conversations.SingleOrDefault(conversation =>
                conversation.Kind == ChatConversationKind.Team && conversation.TeamId == team.Id);
            if (teamChat is null)
            {
                teamChat = ChatConversation.CreateTeam(organization.Id, team.Id, team.Name, now);
                dbContext.ChatConversations.Add(teamChat);
                conversations.Add(teamChat);
            }
            else if (teamChat.Name != team.Name)
            {
                teamChat.Rename(team.Name, now);
            }

            SynchronizeParticipants(
                teamChat,
                team.Members.Select(member => member.UserId).Append(organization.OwnerUserId),
                userId => userId == organization.OwnerUserId || userId == team.LeaderUserId,
                now);
        }

        var validTeamIds = organization.Teams.Select(team => team.Id).ToHashSet();
        foreach (var obsolete in conversations.Where(conversation =>
                     conversation.Kind == ChatConversationKind.Team &&
                     (!conversation.TeamId.HasValue || !validTeamIds.Contains(conversation.TeamId.Value))).ToArray())
        {
            dbContext.ChatConversations.Remove(obsolete);
        }

        var organizationMemberIds = organization.Members.Select(member => member.UserId).ToHashSet();
        foreach (var conversation in conversations.Where(conversation => !conversation.IsSpecial))
        {
            foreach (var participant in conversation.Participants.Where(participant => !organizationMemberIds.Contains(participant.UserId)).ToArray())
            {
                conversation.RemoveParticipant(participant.UserId);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void SynchronizeParticipants(
        ChatConversation conversation,
        IEnumerable<Guid> requiredUserIds,
        Func<Guid, bool> isAdmin,
        DateTimeOffset now)
    {
        var required = requiredUserIds.Distinct().ToHashSet();
        foreach (var participant in conversation.Participants.Where(participant => !required.Contains(participant.UserId)).ToArray())
        {
            conversation.RemoveParticipant(participant.UserId);
        }
        foreach (var userId in required)
        {
            conversation.AddParticipant(userId, isAdmin(userId) ? ChatParticipantRole.Admin : ChatParticipantRole.Member, now);
        }
    }

    private IQueryable<ChatConversation> ConversationQuery() =>
        dbContext.ChatConversations
            .AsSplitQuery()
            .Include(conversation => conversation.Participants)
                .ThenInclude(participant => participant.User)
                    .ThenInclude(user => user.ExternalAccounts)
            .Include(conversation => conversation.Messages);

    private async Task<Result<ChatConversation>> GetAccessibleConversationAsync(
        Guid organizationId,
        Guid conversationId,
        Guid requestingUserId,
        CancellationToken cancellationToken)
    {
        var organization = await GetOrganizationAsync(organizationId, cancellationToken);
        if (organization is null || !organization.HasMember(requestingUserId))
        {
            return Result<ChatConversation>.Failure(ChatErrors.OrganizationNotFound);
        }
        await SynchronizeAsync(organization, cancellationToken);
        var conversation = await ConversationQuery().SingleOrDefaultAsync(
            candidate => candidate.Id == conversationId && candidate.OrganizationId == organizationId,
            cancellationToken);
        if (conversation is null || !conversation.HasParticipant(requestingUserId))
        {
            return Result<ChatConversation>.Failure(ChatErrors.ConversationNotFound);
        }
        return Result<ChatConversation>.Success(conversation);
    }

    private async Task<Result<(Organization Organization, ChatConversation Conversation)>> GetManageableGroupAsync(
        Guid organizationId,
        Guid conversationId,
        Guid requestingUserId,
        CancellationToken cancellationToken)
    {
        var organization = await GetOrganizationAsync(organizationId, cancellationToken);
        if (organization is null || !organization.HasMember(requestingUserId))
        {
            return Result<(Organization, ChatConversation)>.Failure(ChatErrors.OrganizationNotFound);
        }
        await SynchronizeAsync(organization, cancellationToken);
        var conversation = await ConversationQuery().SingleOrDefaultAsync(
            candidate => candidate.Id == conversationId && candidate.OrganizationId == organizationId,
            cancellationToken);
        if (conversation is null || !conversation.HasParticipant(requestingUserId))
        {
            return Result<(Organization, ChatConversation)>.Failure(ChatErrors.ConversationNotFound);
        }
        if (conversation.Kind != ChatConversationKind.Group)
        {
            return Result<(Organization, ChatConversation)>.Failure(ChatErrors.FixedMembership);
        }
        var requester = conversation.Participants.Single(participant => participant.UserId == requestingUserId);
        if (requester.Role != ChatParticipantRole.Admin)
        {
            return Result<(Organization, ChatConversation)>.Failure(ChatErrors.AdminRequired);
        }
        return Result<(Organization, ChatConversation)>.Success((organization, conversation));
    }

    private async Task LoadUsersAsync(ChatConversation conversation, CancellationToken cancellationToken)
    {
        foreach (var participant in conversation.Participants)
        {
            await dbContext.Entry(participant).Reference(candidate => candidate.User).LoadAsync(cancellationToken);
            await dbContext.Entry(participant.User).Collection(user => user.ExternalAccounts).LoadAsync(cancellationToken);
        }
    }

    private ChatConversationResponse ToResponse(ChatConversation conversation, Guid requestingUserId)
    {
        var participant = conversation.Participants.Single(candidate => candidate.UserId == requestingUserId);
        var lastMessage = conversation.Messages.OrderByDescending(message => message.CreatedAtUtc).FirstOrDefault();
        var name = conversation.Name;
        if (conversation.Kind == ChatConversationKind.Direct)
        {
            var other = conversation.Participants.FirstOrDefault(candidate => candidate.UserId != requestingUserId)?.User;
            name = other is null ? "Direct message" : $"{other.FirstName} {other.LastName}".Trim();
        }
        var unreadCount = conversation.Messages.Count(message =>
            message.SenderUserId != requestingUserId &&
            (!participant.LastReadAtUtc.HasValue || message.CreatedAtUtc > participant.LastReadAtUtc.Value));

        return new ChatConversationResponse(
            conversation.Id,
            conversation.OrganizationId,
            conversation.Kind.ToString().ToLowerInvariant(),
            name,
            conversation.TeamId,
            conversation.CreatorUserId,
            conversation.IsSpecial,
            unreadCount,
            lastMessage is null ? null : ToMessageResponse(lastMessage),
            conversation.Participants
                .OrderByDescending(member => member.Role)
                .ThenBy(member => member.User.FirstName)
                .Select(member => new ChatMemberResponse(
                    member.UserId,
                    $"{member.User.FirstName} {member.User.LastName}".Trim(),
                    member.User.Username,
                    member.User.AvatarUrl ?? member.User.ExternalAccounts
                        .Select(account => account.AvatarUrl)
                        .FirstOrDefault(avatarUrl => !string.IsNullOrWhiteSpace(avatarUrl)),
                    member.Role.ToString().ToLowerInvariant(),
                    conversation.CreatorUserId == member.UserId))
                .ToArray());
    }

    private ChatMessageResponse ToMessageResponse(ChatMessage message) =>
        new(
            message.Id,
            message.ConversationId,
            message.SenderUserId,
            message.ClientMessageId,
            cipher.Decrypt(message.CipherText, message.Nonce, message.AuthenticationTag, message.KeyVersion),
            message.CreatedAtUtc);
}
