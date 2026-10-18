using System;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SecureAuth.Api.OpenIdConnect;
using MediaBrowser.Controller.Authentication;

namespace Jellyfin.Plugin.SecureAuth.Services;

/// <summary>
///     The OIDC user manager.
/// </summary>
public interface IOidcUserManager
{
    /// <summary>
    ///     Tries to get or create a jellyfin user from the given sub or username.
    /// </summary>
    /// <param name="provider">Provider the user logged in with.</param>
    /// <param name="timedState">TimedState belonging to the flow</param>
    /// <param name="oidcProvider">The server config.</param>
    /// <returns></returns>
    Task<Guid> GetOrCreateUser(string provider, TimedOidcState timedState, OidcProvider oidcProvider);

    /// <summary>
    ///     Authenticates the user with the given information.
    /// </summary>
    /// <param name="userId">The user id of the user to authenticate.</param>
    /// <param name="authResponse">The client information to authenticate the user with.</param>
    /// <param name="oidcProvider">The provider config.</param>
    /// <param name="timedState">The state belonging to the flow.</param>
    /// <param name="ipAddress">The IP address of the authenticating user.</param>
    Task<AuthenticationResult> AuthenticateUser(
        Guid userId,
        AuthResponse authResponse,
        OidcProvider oidcProvider,
        TimedOidcState timedState,
        string ipAddress);


    /// <summary>
    ///     Removes a user from OIDC auth and switches it back to another auth provider.
    /// </summary>
    /// <param name="user">The user to remove from OIDC.</param>
    /// <param name="provider">The new jellyfin auth provider to switch to (not an IdP).</param>
    Task UnregisterUser(User user, string provider);
}