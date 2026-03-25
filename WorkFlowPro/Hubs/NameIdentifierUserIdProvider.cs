using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace WorkFlowPro.Hubs;

/// <summary>Map SignalR <c>Clients.User(userId)</c> với claim NameIdentifier.</summary>
public sealed class NameIdentifierUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirstValue(ClaimTypes.NameIdentifier);
}
