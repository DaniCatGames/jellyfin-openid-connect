using System.Linq;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SecureAuth.Api.OpenIdConnect;

/// <summary>
///     The provider api controller.
/// </summary>
[ApiController]
[Route("SecureAuth/OIDC")]
[Route("OpenIDConnect")]
public class AuthProviderController : ControllerBase
{
    /// <summary>
    ///     Gets the available auth providers
    /// </summary>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpGet("AuthProviders")]
    public ActionResult AuthProviders([FromServices] IServerApplicationHost appHost)
    {
        var providers = appHost.GetExports<IAuthenticationProvider>()
            .Select(p => new
            {
                Name = p.Name == "Default" ? "Jellyfin (Username + Password)" : p.Name,
                Type = p.GetType().FullName,
            })
            .Where(p => p.Name != "InvalidOrMissingAuthenticationProvider")
            .ToList();

        return Ok(providers);
    }
}