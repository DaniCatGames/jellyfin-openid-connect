using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using MediaBrowser.Common;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OpenIDConnect.Api;

/// <summary>
///     The provider api controller.
/// </summary>
[ApiController]
[Route("OpenIDConnect/Providers")]
public class ProviderController(
    ILogger<ProviderController> logger
) : ControllerBase
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
        if (!Regex.IsMatch(provider, @"^[a-zA-Z0-9\-_]+$"))
        {
            return BadRequest("Provider name must only contain letters, numbers, dashes, and underscores.");
        }

        if (config.Endpoint == null)
        {
            return BadRequest("Endpoint is required");
        }

        if (config.ClientId == null)
        {
            return BadRequest("Client ID is required");
        }

        if (config.Secret == null)
        {
            return BadRequest("Client secret is required");
        }

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
        if (string.IsNullOrEmpty(provider))
        {
            return BadRequest("Provider name is required");
        }

        PluginConfiguration configuration = OpenIDConnect.Instance.Configuration;
        if (!configuration.Configs.Remove(provider))
        {
            return NotFound("Provider not found");
        }

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

    /// <summary>
    ///     Checks if migration from 9p4 config is available
    /// </summary>
    /// <returns></returns>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpGet("MigrationAvailable")]
    public ActionResult MigrationAvailable([FromServices] IApplicationPaths applicationPaths)
    {
        string oldPath = Path.Combine(applicationPaths.PluginConfigurationsPath, "SSO-Auth.xml");

        return Ok(System.IO.File.Exists(oldPath));
    }

    /// <summary>
    ///     Migrates providers from the old provider config to the new one
    /// </summary>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpPost("Migrate")]
    public ActionResult Migrate(
        [FromServices] IApplicationPaths applicationPaths,
        [FromServices] IApplicationHost applicationHost)
    {
        string oldPath = Path.Combine(applicationPaths.PluginConfigurationsPath, "SSO-Auth.xml");

        if (!System.IO.File.Exists(oldPath))
        {
            return BadRequest("No old config to migrate from");
        }

        SsoAuthConfig oldConfig;
        var serializer = new XmlSerializer(typeof(SsoAuthConfig));

        try
        {
            using FileStream stream = System.IO.File.OpenRead(oldPath);
            oldConfig = (SsoAuthConfig)serializer.Deserialize(stream);
        }
        catch
        {
            logger.LogError("Failed to parse config");
            return StatusCode(500, "Failed to parse config");
        }

        if (oldConfig?.OidConfigs == null || oldConfig.OidConfigs.Count == 0)
        {
            return BadRequest("No configurations found in the old file to migrate.");
        }

        PluginConfiguration currentConfig = OpenIDConnect.Instance.Configuration;

        foreach ((string name, SsoAuthProvider ssoAuthProvider) in oldConfig.OidConfigs)
        {
            string provider = null;
            if (ssoAuthProvider.DefaultProvider is not null)
            {
                if (applicationHost.GetExports<IAuthenticationProvider>()
                    .Select(p => p.GetType().FullName)
                    .Where(p => p != "InvalidOrMissingAuthenticationProvider")
                    .Any(p => p == ssoAuthProvider.DefaultProvider))
                {
                    provider = ssoAuthProvider.DefaultProvider;
                }
            }

            var newConfig = new Config
            {
                Endpoint = ssoAuthProvider.OidEndpoint,
                ClientId = ssoAuthProvider.OidClientId,
                Secret = ssoAuthProvider.OidSecret,
                Enabled = ssoAuthProvider.Enabled,
                RoleClaim = ssoAuthProvider.RoleClaim,
                DefaultUsernameClaim = ssoAuthProvider.DefaultUsernameClaim,
                AvatarClaim = null,
                EnableUserProvisioning = false,
                UpdateUsersOnLogin = false,
                Roles = ssoAuthProvider.Roles,
                AdminRoles = ssoAuthProvider.AdminRoles,
                AutoLinkingAllowList = [],
                EnableAllFolders = ssoAuthProvider.EnableAllFolders,
                EnabledFolders = ssoAuthProvider.EnabledFolders,
                EnableFolderRoles = ssoAuthProvider.EnableFolderRoles,
                FolderRoleMapping = ssoAuthProvider.FolderRoleMapping,
                EnableLiveTv = ssoAuthProvider.EnableLiveTv,
                EnableLiveTvManagement = ssoAuthProvider.EnableLiveTvManagement,
                EnableLiveTvRoles = ssoAuthProvider.EnableLiveTvRoles,
                LiveTvRoles = ssoAuthProvider.LiveTvRoles,
                LiveTvManagementRoles = ssoAuthProvider.LiveTvManagementRoles,
                Scopes = ssoAuthProvider.OidScopes,
                DefaultAuthProvider = provider,
                DisableHttps = ssoAuthProvider.DisableHttps,
                DisablePushedAuthorization = ssoAuthProvider.DisablePushedAuthorization,
                DoNotValidateEndpoints = ssoAuthProvider.DoNotValidateEndpoints,
                DoNotValidateIssuerName = ssoAuthProvider.DoNotValidateIssuerName,
                UseHTTP = ssoAuthProvider.SchemeOverride == "http",
                DoNotLoadProfile = ssoAuthProvider.DoNotLoadProfile,
                PortOverride = ssoAuthProvider.PortOverride,
                Links = null,
            };

            currentConfig.Configs[name] = newConfig;
        }

        OpenIDConnect.Instance.UpdateConfiguration(currentConfig);

        return Ok();
    }
}
