using System;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Authentication;

namespace Jellyfin.Plugin.OpenIDConnect;

/// <summary>
///     Authentication provider for OIDC Users
/// </summary>
public class AuthProvider : IAuthenticationProvider
{
    /// <summary>
    ///     Attempt to authenticate will fail as OIDC is enabled for this user
    /// </summary>
    /// <param name="username"></param>
    /// <param name="password"></param>
    public Task<ProviderAuthenticationResult> Authenticate(string username, string password)
    {
        throw new AuthenticationException("OIDC is enabled for this user.");
    }

    /// <summary>
    ///     Attempt to change the password will fail as OIDC is enabled for this user
    /// </summary>
    /// <param name="user"></param>
    /// <param name="newPassword"></param>
    public Task ChangePassword(User user, string newPassword)
    {
        throw new Exception("OIDC is enabled for this user.");
    }

    /// <summary>
    ///     Provider Name
    /// </summary>
    public string Name => "OpenID Connect";

    /// <summary>
    ///     Provider Status
    /// </summary>
    public bool IsEnabled => true;
}