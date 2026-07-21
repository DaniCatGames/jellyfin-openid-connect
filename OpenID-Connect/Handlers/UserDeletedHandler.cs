using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.OpenIDConnect.Services;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OpenIDConnect.Handlers;

/// <summary>
///     Removes links pointing to a jellyfin user when that user is deleted
/// </summary>
/// <param name="logger">Instance of the <see cref="ILogger{UserDeletedHandler}" /> interface.</param>
/// <param name="linkManager">Instance of the <see cref="ILinkManager" /> interface.</param>
public partial class UserDeletedHandler(
    ILogger<UserDeletedHandler> logger,
    ILinkManager linkManager
) : IEventConsumer<UserDeletedEventArgs>
{
    /// <inheritdoc />
    public Task OnEvent(UserDeletedEventArgs eventArgs)
    {
        User user = eventArgs.Argument;
        LogCleaningLinksFor(user.Username);

        linkManager.DeleteLinksToUser(user.Id);

        return Task.CompletedTask;
    }

    [LoggerMessage(LogLevel.Information, "Cleaning up oidc links for deleted user {Username}")]
    partial void LogCleaningLinksFor(string username);
}