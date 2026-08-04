using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Duende.IdentityModel.OidcClient;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.OpenIDConnect.Services;
using Jellyfin.Plugin.OpenIDConnect.Views;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.OpenIDConnect.Api;

/// <summary>
///     The oidc api controller.
/// </summary>
/// <param name="logger">Instance of the <see cref="ILogger{OpenIDConnectController}" /> interface.</param>
/// <param name="loggerFactory">Instance of the <see cref="ILoggerFactory" /> interface.</param>
/// <param name="userManager">Instance of the <see cref="IUserManager" /> interface.</param>
/// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory" /> interface.</param>
/// <param name="stateManager">Instance of the <see cref="IStateManager" /> interface.</param>
[ApiController]
[Route("[controller]")]
public class OpenIDConnectController(
    ILogger<OpenIDConnectController> logger,
    ILoggerFactory loggerFactory,
    IUserManager userManager,
    IHttpClientFactory httpClientFactory,
    IStateManager stateManager,
    IOidcUserManager oidcUserManager
) : ControllerBase
{
    /// <summary>
    ///     The GET endpoint for the OpenID provider to call back to. Returns a webpage that parses client data and completes
    ///     auth.
    /// </summary>
    /// <param name="provider">The ID of the provider which will use the callback information.</param>
    /// <param name="state">The current request state.</param>
    /// <returns>A webpage that will complete the client-side flow.</returns>
    // Actually a GET: https://github.com/IdentityModel/IdentityModel.OidcClient/issues/325
    [HttpGet("redirect/{provider}")]
    public async Task<ActionResult> Callback(
        [FromRoute] string provider,
        [FromQuery] string state)
    {
        // If the config doesn't have an active provider matching the request, show an error
        if (!OpenIDConnect.Instance.Configuration.Configs.TryGetValue(provider, out Config config)
            || !config.Enabled)
        {
            return BadRequest("No matching provider found");
        }

        if (string.IsNullOrEmpty(state))
        {
            return BadRequest("Missing state");
        }

        if (!stateManager.TryGetValue(state, out TimedAuthorizeState timedState)
            || timedState.IsExpired())
        {
            return BadRequest("Invalid or expired state");
        }

        OidcClient oidcClient = CreateClient(provider, config);

        AuthorizeState currentState = timedState.State;
        LoginResult result = await oidcClient.ProcessResponseAsync(Request.QueryString.Value, currentState)
            .ConfigureAwait(false);

        if (result.IsError)
        {
            return BadRequest($"Error logging in: {result.Error} - {result.ErrorDescription}");
        }

        if (timedState.IsTesting)
        {
            stateManager.TryRemove(state, out _);

            string htmlOutput = WebResponse.GenerateHtmlTestingPage(provider, result.User.Claims);

            return Content(htmlOutput, "text/html");
        }

        timedState.DefaultAllowedFolders = [.. config.EnabledFolders ?? []];
        timedState.RbacFolders = [];

        Claim avatarClaim = result.User.Claims.FirstOrDefault(claim =>
            claim.Type == (string.IsNullOrWhiteSpace(config.AvatarClaim) ? "picture" : config.AvatarClaim));

        if (avatarClaim != null
            && Uri.TryCreate(avatarClaim.Value, UriKind.Absolute, out Uri uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            timedState.AvatarURL = uri.ToString();
        }

        // Role processing
        // The regex matches any "." not preceded by a "\": a.b.c will be split into a, b, and c, but a.b\.c will be split into a, b.c (after processing the escaped dots)
        // We have to first process the RoleClaim string
        string[] segments = string.IsNullOrEmpty(config.RoleClaim?.Trim())
            ? ["groups"]
            : Regex.Split(config.RoleClaim.Trim(), @"(?<!\\)\.");

        // Now we make sure that any escaped "."s ("\.") are replaced with "."
        segments = segments.Select(i => i.Replace("\\.", ".")).ToArray();

        Claim[] roleClaims = [.. result.User.Claims.Where(claim => claim.Type == segments[0])];
        
        if (roleClaims.Length > 0)
        {
            ProcessRoles(segments, roleClaims, config, timedState);
        }

        Claim subClaim = result.User.Claims.FirstOrDefault(claim => claim.Type == "sub");
        if (subClaim != null)
        {
            timedState.Sub = subClaim.Value;
            if (config.Roles is not { Length: > 0 })
            {
                timedState.Valid = true;
            }
        }
        else
        {
            logger.LogWarning("OpenID user {Username} does not have a sub claim", timedState.Sub);
            return Unauthorized("Error. Check IdP or plugin config.");
        }

        Claim usernameClaim = result.User.Claims.FirstOrDefault(claim =>
            claim.Type == (config.DefaultUsernameClaim?.Trim() ?? "preferred_username"));
        timedState.Username = usernameClaim?.Value ?? timedState.Sub;

        if (!timedState.Valid)
        {
            logger.LogWarning(
                "OpenID user {Username} has one or more incorrect role claims: {@Claims}. Expected any one of: {@ExpectedClaims}",
                timedState.Username,
                result.User.Claims.Select(o => new { o.Type, o.Value }),
                config.Roles);

            return Unauthorized("Error. Check permissions.");
        }

        logger.LogInformation("Is request linking: {IsLinking}", timedState.IsLinking);
        return Content(WebResponse.Generator(state,
                provider,
                GetRequestBase(config.UseHTTP, config.PortOverride),
                timedState.IsLinking),
            MediaTypeNames.Text.Html);
    }

    private OidcClient CreateClient(string provider, Config config)
    {
        string[] scopes = config.Scopes ?? new string[2];
        var options = new OidcClientOptions
        {
            Authority = config.Endpoint,
            ClientId = config.ClientId,
            ClientSecret = config.Secret,
            RedirectUri = GetRequestBase(config.UseHTTP, config.PortOverride)
                          + $"/OpenIDConnect/redirect/{provider}",
            Scope = string.Join(" ", scopes.Prepend("openid profile")),
            DisablePushedAuthorization = config.DisablePushedAuthorization,
            LoggerFactory = loggerFactory,
            LoadProfile = !config.DoNotLoadProfile,
            HttpClientFactory = _ =>
            {
                HttpClient client = httpClientFactory.CreateClient();
                var assembly = Assembly.GetExecutingAssembly();
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                string version = fvi.FileVersion;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    $"Jellyfin-Plugin-OpenID-Connect +{version} (https://github.com/DaniCatGames/jellyfin-openid-connect)");
                return client;
            },
            Policy =
            {
                Discovery =
                {
                    AdditionalEndpointBaseAddresses = { new Uri(config.Endpoint).GetLeftPart(UriPartial.Authority) },
                    ValidateEndpoints = !config.DoNotValidateEndpoints,
                    RequireHttps = !config.DisableHttps,
                    ValidateIssuerName = !config.DoNotValidateIssuerName,
                },
            },
        };

        var oidcClient = new OidcClient(options);
        return oidcClient;
    }

    private void ProcessRoles(
        string[] segments,
        IEnumerable<Claim> claims,
        Config config,
        TimedAuthorizeState timedState)
    {
        List<string> roles = [];
        foreach (Claim claim in claims)
        {
            if (segments.Length == 1)
            {
                // If we are not using JSON values, just use the raw info from the claim value
                roles.Add(claim.Value);
            }
            else
            {
                try
                {
                    JToken currentToken = segments
                        .Skip(1)
                        .Aggregate(JToken.Parse(claim.Value), (current, segment) => current[segment]);

                    if (currentToken is JArray rolesArray)
                    {
                        roles.AddRange(rolesArray.ToObject<List<string>>() ?? []);
                    }
                    else
                    {
                        throw new JsonException("Role claim is not an array");
                    }
                }
                catch (JsonException error)
                {
                    logger.LogError(error, "Error parsing JSON role claim: {Claim}", claim.Value);
                }
            }
        }

        timedState.Valid = roles.Any(role => config.Roles?.Contains(role) == true)
                           || roles.Any(role => config.AdminRoles?.Contains(role) == true);

        timedState.Admin = roles.Any(role => config.AdminRoles?.Contains(role) == true);

        timedState.EnableLiveTv = roles.Any(role => config.LiveTvRoles?.Contains(role) == true);
        timedState.EnableLiveTvManagement = roles.Any(role => config.LiveTvManagementRoles?.Contains(role) == true);

        // Get allowed folders from roles
        if (config.FolderRoleMapping is { Count: >= 1 })
        {
            IEnumerable<string> folders = config.FolderRoleMapping
                .Where(map => roles.Any(role => role.Equals(map.Role.Trim(), StringComparison.Ordinal)))
                .SelectMany(map => map.Folders ?? []);
            timedState.RbacFolders.AddRange(folders);
        }
    }

    /// <summary>
    ///     Initiates the login flow for OpenID. This redirects the user to the auth provider.
    /// </summary>
    /// <param name="provider">The name of the provider.</param>
    /// <param name="isLinking">Whether or not this request is to link accounts (Rather than authenticate).</param>
    /// <param name="isTesting">Whether or not this request is to test an IdP (Rather than authenticate).</param>
    /// <returns>An asynchronous result for the authentication.</returns>
    [HttpGet("start/{provider}")]
    public async Task<ActionResult> Challenge(
        string provider,
        [FromQuery] bool isLinking = false,
        [FromQuery] bool isTesting = false)
    {
        stateManager.Invalidate();

        if (!OpenIDConnect.Instance.Configuration.Configs.TryGetValue(provider, out Config config)
            || !config.Enabled)
        {
            return BadRequest("Provider does not exist");
        }

        OidcClient oidcClient = CreateClient(provider, config);

        AuthorizeState state = await oidcClient.PrepareLoginAsync().ConfigureAwait(false);

        if (state.IsError)
        {
            return BadRequest($"Error preparing login: {state.Error} - {state.ErrorDescription}");
        }

        var timedState = new TimedAuthorizeState(state, DateTime.UtcNow)
        {
            IsLinking = isLinking,
            IsTesting = isTesting,
        };

        stateManager.TryAdd(state.State, timedState);

        return Redirect(state.StartUrl);
    }


    /// <summary>
    ///     This is a debug endpoint to list all running OpenID flows. Requires administrator privileges.
    /// </summary>
    /// <returns>The list of OpenID flows in progress.</returns>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpGet("States")]
    public ActionResult GetRunningFlows()
    {
        return Ok(stateManager.GetStates());
    }

    /// <summary>
    ///     This endpoint accepts JSON and will authorize the user from the device values passed from the client.
    /// </summary>
    /// <param name="provider">Name of provider to authenticate against.</param>
    /// <param name="response">The data passed to the client to ensure it is the right one.</param>
    /// <returns>JSON for the client to populate information with.</returns>
    [HttpPost("Auth/{provider}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> Authenticate(string provider, [FromBody] AuthResponse response)
    {
        if (!OpenIDConnect.Instance.Configuration.Configs.TryGetValue(provider, out Config config)
            || !config.Enabled)
        {
            return BadRequest("Provider does not exist");
        }

        if (!stateManager.TryGetValue(response.Data, out TimedAuthorizeState timedState))
        {
            return Problem("State not found");
        }

        if (!timedState.IsValid())
        {
            return Problem("State is not valid or expired.");
        }

        Guid userId = await oidcUserManager.GetOrCreateUser(provider, timedState, config);

        if (userId == Guid.Empty)
        {
            stateManager.TryRemove(response.Data, out _);
            return Unauthorized("User provisioning or linking with this user is disabled.");
        }

        AuthenticationResult authenticationResult = await oidcUserManager.AuthenticateUser(userId,
                response,
                config,
                timedState,
                HttpContext.Connection.RemoteIpAddress?.ToString())
            .ConfigureAwait(false);

        stateManager.TryRemove(response.Data, out _);
        return Ok(authenticationResult);
    }

    /// <summary>
    ///     Removes a user from SSO auth and switches it back to another auth provider. Requires administrator privileges.
    /// </summary>
    /// <param name="username">The username to switch to the new provider.</param>
    /// <param name="provider">The new jellyfin auth provider to switch to (not an IdP).</param>
    /// <returns>Whether this API endpoint succeeded.</returns>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpPost("Unregister/{username}")]
    public async Task<ActionResult> UnregisterUserFromOidc(string username, [FromBody] string provider)
    {
        User user = userManager.GetUserByName(username);
        if (user == null)
        {
            return NotFound("User not found");
        }

        await oidcUserManager.UnregisterUser(user, provider).ConfigureAwait(false);

        return Ok();
    }

    private string GetRequestBase(bool useHttp = false, int? portOverride = null)
    {
        int requestPort = portOverride ?? Request.Host.Port ?? -1;

        if ((requestPort, Request.Scheme) is (80, "http") or (443, "https"))
        {
            requestPort = -1;
        }

        return new UriBuilder
        {
            Scheme = useHttp ? "http" : "https",
            Host = Request.Host.Host,
            Port = requestPort,
            Path = Request.PathBase,
        }.ToString().TrimEnd('/');
    }
}

/// <summary>
///     The data the client should pass back to the API.
/// </summary>
/// <param name="DeviceId">The device ID of the client.</param>
/// <param name="DeviceName">The device name of the client.</param>
/// <param name="AppName">The app name of the client.</param>
/// <param name="AppVersion">The app version of the client.</param>
/// <param name="Data">The auth data of the client (for authorizing the response).</param>
public record AuthResponse(string DeviceId, string DeviceName, string AppName, string AppVersion, string Data);