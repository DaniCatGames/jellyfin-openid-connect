using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.OpenIDConnect.Api;

/// <summary>
///     The provider api controller.
/// </summary>
[ApiController]
[Route("OpenIDConnect/Providers")]
public class ProviderController : ControllerBase
{
    /// <summary>
    ///     Adds an OpenID auth configuration. Requires administrator privileges. If the provider already exists,
    ///     it will be overwritten.
    /// </summary>
    /// <param name="provider">The name of the provider to add.</param>
    /// <param name="config">The OID configuration (deserialized from a JSON post).</param>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpPut("{provider}")]
    public ActionResult AddProvider(string provider, [FromBody] Config config)
    {
        PluginConfiguration configuration = OpenIDConnect.Instance.Configuration;
        configuration.Configs[provider] = config;
        OpenIDConnect.Instance.UpdateConfiguration(configuration);
        return Ok();
    }

    /// <summary>
    ///     Deletes an OpenID provider.
    /// </summary>
    /// <param name="provider">Name of provider to delete.</param>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpDelete("{provider}")]
    public ActionResult DeleteProvider(string provider)
    {
        PluginConfiguration configuration = OpenIDConnect.Instance.Configuration;
        configuration.Configs.Remove(provider);
        OpenIDConnect.Instance.UpdateConfiguration(configuration);
        return Ok();
    }

    /// <summary>
    ///     Lists the OpenID providers configured. Requires administrator privileges.
    /// </summary>
    /// <returns>The list of OpenID configurations.</returns>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpGet("")]
    public ActionResult GetProviders()
    {
        return Ok(OpenIDConnect.Instance.Configuration.Configs);
    }

    /// <summary>
    ///     Lists the OpenID providers names only.
    /// </summary>
    /// <returns>The list of OpenID configurations.</returns>
    [HttpGet("Names")]
    public ActionResult GetProviderNames()
    {
        return Ok(OpenIDConnect.Instance.Configuration.Configs.Keys);
    }
}