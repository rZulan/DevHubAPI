using System.IdentityModel.Tokens.Jwt;
using DevHub.Application.Chats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DevHub.Api.Realtime;

[Authorize]
public sealed class ChatHub(IChatService chatService) : Hub
{
    public async Task<ChatMessageResponse> SendMessage(
        Guid organizationId,
        Guid conversationId,
        Guid clientMessageId,
        string body)
    {
        var cancellationToken = Context.ConnectionAborted;
        var result = await chatService.SendMessageAsync(
            organizationId,
            conversationId,
            GetAuthenticatedUserId(),
            clientMessageId,
            body,
            cancellationToken);
        if (!result.IsSuccess) throw new HubException(result.Error!.Description);

        var delivery = result.Value!;
        await Clients.Users(delivery.RecipientUserIds.Select(id => id.ToString()))
            .SendAsync("MessageReceived", delivery.Message, cancellationToken);
        return delivery.Message;
    }

    private Guid GetAuthenticatedUserId()
    {
        var subject = Context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(subject, out var userId)
            ? userId
            : throw new HubException("The authenticated user is invalid.");
    }
}
