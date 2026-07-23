using System;
using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.OpenIDConnect.Api;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OpenIDConnect.Services;

/// <summary>
///     The OIDC user manager.
/// </summary>
/// <param name="logger">Instance of the <see cref="ILogger{TCategoryName}" /> interface.</param>
/// <param name="userManager">Instance of the <see cref="IUserManager" /> interface.</param>
/// <param name="sessionManager">Instance of the <see cref="ISessionManager" /> interface.</param>
/// <param name="providerManager">Instance of the <see cref="IProviderManager" /> interface.</param>
/// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager" /> interface.</param>
/// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory" /> interface.</param>
public interface IOidcUserManager
{
    /// <summary>
    ///     Tries to get or create a jellyfin user from the given sub or username.
    /// </summary>
    /// <param name="provider">Provider the user logged in with.</param>
    /// <param name="timedState">TimedState belonging to the flow</param>
    /// <param name="config">The server config.</param>
    /// <returns></returns>
    Task<Guid> GetOrCreateUser(string provider, TimedAuthorizeState timedState, Config config);

    /// <summary>
    ///     Authenticates the user with the given information.
    /// </summary>
    /// <param name="userId">The user id of the user to authenticate.</param>
    /// <param name="authResponse">The client information to authenticate the user with.</param>
    /// <param name="config">The provider config.</param>
    /// <param name="timedState">The state belonging to the flow.</param>
    /// <param name="ipAddress">The IP address of the authenticating user.</param>
    Task<AuthenticationResult> AuthenticateUser(
        Guid userId,
        AuthResponse authResponse,
        Config config,
        TimedAuthorizeState timedState,
        string ipAddress);
    
    
    /// <summary>
    ///     Removes a user from OIDC auth and switches it back to another auth provider.
    /// </summary>
    /// <param name="user">The user to remove from OIDC.</param>
    /// <param name="provider">The new jellyfin auth provider to switch to (not an IdP).</param>
    Task UnregisterUser(User user, string provider);
}