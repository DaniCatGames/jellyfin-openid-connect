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
public partial class UserDeletedHandler : IEventConsumer<UserDeletedEventArgs>
{
    private readonly ILinkManager _linkManager;
    private readonly ILogger<UserDeletedHandler> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UserDeletedHandler" /> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{UserDeletedHandler}" /> interface.</param>
    /// <param name="linkManager">Instance of the <see cref="ILinkManager" /> interface.</param>
    public UserDeletedHandler(ILogger<UserDeletedHandler> logger, ILinkManager linkManager)
    {
        _logger = logger;
        _linkManager = linkManager;
    }

    /// <inheritdoc />
    public Task OnEvent(UserDeletedEventArgs eventArgs)
    {
        User user = eventArgs.Argument;
        LogCleaningLinksFor(user.Username);

        _linkManager.DeleteLinksToUser(user.Id);

        return Task.CompletedTask;
    }

    [LoggerMessage(LogLevel.Information, "Cleaning up oidc links for deleted user {Username}")]
    partial void LogCleaningLinksFor(string username);
}