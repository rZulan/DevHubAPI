using System.ComponentModel.DataAnnotations;
using DevHub.Api.Realtime;
using DevHub.Application.Chats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace DevHub.Api.Controllers;

public sealed record CreateDirectChatRequest(Guid UserId);

public sealed record CreateGroupChatRequest(
    [Required, StringLength(150)] string Name,
    IReadOnlyCollection<Guid> MemberUserIds);

public sealed record SetChatAdminRequest(bool IsAdmin);

/// <summary>Creates and manages private and group conversations.</summary>
[Tags("Chats")]
[Route("api/organizations/{organizationId:guid}/chats")]
[Authorize]
public sealed class ChatsController(
    IChatService chatService,
    IHubContext<ChatHub> chatHub) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid organizationId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await chatService.ListConversationsAsync(organizationId, userId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPost("direct")]
    public async Task<IActionResult> CreateDirect(
        Guid organizationId,
        [FromBody] CreateDirectChatRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await chatService.CreateDirectAsync(organizationId, userId, request.UserId, cancellationToken);
        if (!result.IsSuccess) return Failure(result.Error!);
        await NotifyConversationChangedAsync(result.Value!, cancellationToken);
        return Ok(result.Value);
    }

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup(
        Guid organizationId,
        [FromBody] CreateGroupChatRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await chatService.CreateGroupAsync(
            organizationId,
            userId,
            request.Name,
            request.MemberUserIds,
            cancellationToken);
        if (!result.IsSuccess) return Failure(result.Error!);
        await NotifyConversationChangedAsync(result.Value!, cancellationToken);
        return Created($"/api/organizations/{organizationId}/chats/{result.Value!.Id}", result.Value);
    }

    [HttpGet("{conversationId:guid}/messages")]
    public async Task<IActionResult> ListMessages(
        Guid organizationId,
        Guid conversationId,
        [FromQuery] DateTimeOffset? before,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await chatService.ListMessagesAsync(
            organizationId,
            conversationId,
            userId,
            before,
            take,
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPut("{conversationId:guid}/read")]
    public async Task<IActionResult> MarkRead(
        Guid organizationId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await chatService.MarkReadAsync(organizationId, conversationId, userId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPut("{conversationId:guid}/members/{memberUserId:guid}")]
    public async Task<IActionResult> AddMember(
        Guid organizationId,
        Guid conversationId,
        Guid memberUserId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await chatService.AddMemberAsync(
            organizationId,
            conversationId,
            userId,
            memberUserId,
            cancellationToken);
        if (!result.IsSuccess) return Failure(result.Error!);
        await NotifyConversationChangedAsync(result.Value!, cancellationToken, memberUserId);
        return Ok(result.Value);
    }

    [HttpDelete("{conversationId:guid}/members/{memberUserId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid organizationId,
        Guid conversationId,
        Guid memberUserId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await chatService.RemoveMemberAsync(
            organizationId,
            conversationId,
            userId,
            memberUserId,
            cancellationToken);
        if (!result.IsSuccess) return Failure(result.Error!);
        await NotifyConversationChangedAsync(result.Value!, cancellationToken, memberUserId);
        return Ok(result.Value);
    }

    [HttpPut("{conversationId:guid}/members/{memberUserId:guid}/admin")]
    public async Task<IActionResult> SetAdmin(
        Guid organizationId,
        Guid conversationId,
        Guid memberUserId,
        [FromBody] SetChatAdminRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await chatService.SetAdminAsync(
            organizationId,
            conversationId,
            userId,
            memberUserId,
            request.IsAdmin,
            cancellationToken);
        if (!result.IsSuccess) return Failure(result.Error!);
        await NotifyConversationChangedAsync(result.Value!, cancellationToken);
        return Ok(result.Value);
    }

    private async Task NotifyConversationChangedAsync(
        ChatConversationResponse conversation,
        CancellationToken cancellationToken,
        Guid? additionalUserId = null)
    {
        var userIds = conversation.Members.Select(member => member.UserId);
        if (additionalUserId.HasValue) userIds = userIds.Append(additionalUserId.Value);
        await chatHub.Clients.Users(userIds.Distinct().Select(id => id.ToString()))
            .SendAsync("ConversationsChanged", conversation.OrganizationId, cancellationToken);
    }
}
