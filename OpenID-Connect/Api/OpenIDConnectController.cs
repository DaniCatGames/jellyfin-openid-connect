using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Duende.IdentityModel.OidcClient;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.OpenIDConnect.Config;
using Jellyfin.Plugin.OpenIDConnect.Views;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.OpenIDConnect.Api;

/// <summary>
///     The sso api controller.
/// </summary>
[ApiController]
[Route("[controller]")]
public class OpenIDConnectController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, TimedAuthorizeState> StateManager =
        new ConcurrentDictionary<string, TimedAuthorizeState>();

    private readonly IAuthorizationContext _authContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenIDConnectController> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IProviderManager _providerManager;
    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly ISessionManager _sessionManager;
    private readonly IUserManager _userManager;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OpenIDConnectController" /> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{OpenIDConnectController}" /> interface.</param>
    /// <param name="loggerFactory">Instance of the <see cref="ILoggerFactory" /> interface.</param>
    /// <param name="sessionManager">Instance of the <see cref="ISessionManager" /> interface.</param>
    /// <param name="authContext">Instance of the <see cref="IAuthorizationContext" /> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager" /> interface.</param>
    /// <param name="providerManager">Instance of the <see cref="IProviderManager" /> interface.</param>
    /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory" /> interface.</param>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager" /> interface.</param>
    public OpenIDConnectController(
        ILogger<OpenIDConnectController> logger,
        ILoggerFactory loggerFactory,
        ISessionManager sessionManager,
        IUserManager userManager,
        IAuthorizationContext authContext,
        IProviderManager providerManager,
        IHttpClientFactory httpClientFactory,
        IServerConfigurationManager serverConfigurationManager)
    {
        _sessionManager = sessionManager;
        _userManager = userManager;
        _authContext = authContext;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _providerManager = providerManager;
        _serverConfigurationManager = serverConfigurationManager;
        _httpClientFactory = httpClientFactory;
        _logger.LogInformation("OpenID Connect Controller initialized");
    }

    /// <summary>
    ///     The GET endpoint for OpenID provider to callback to. Returns a webpage that parses client data and completes auth.
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
        OidConfig config;

        // If the config doesn't have an active provider matching the requeset, show an error
        if (!OpenIDConnect.Instance.Configuration.OidConfigs.TryGetValue(provider, out config) || !config.Enabled)
        {
            return BadRequest("No matching provider found");
        }

        if (string.IsNullOrEmpty(state))
        {
            return BadRequest("Missing state");
        }

        if (!StateManager.TryGetValue(state, out TimedAuthorizeState timedState))
        {
            return BadRequest("Invalid or expired state");
        }

        OidcClient oidcClient = CreateClient(provider, config, out ActionResult configError);
        if (configError != null)
        {
            return configError;
        }

        AuthorizeState currentState = timedState.State;
        LoginResult result = await oidcClient.ProcessResponseAsync(Request.QueryString.Value, currentState)
            .ConfigureAwait(false);

        if (result.IsError)
        {
            return BadRequest($"Error logging in: {result.Error} - {result.ErrorDescription}");
        }

        if (!config.EnableFolderRoles && config.EnabledFolders != null)
        {
            timedState.Folders = new List<string>(config.EnabledFolders);
        }
        else
        {
            timedState.Folders = [];
        }

        timedState.EnableLiveTv = config.EnableLiveTv;
        timedState.EnableLiveTvManagement = config.EnableLiveTvManagement;

        if (config.AvatarUrlFormat is not null)
        {
            timedState.AvatarURL = result.User.Claims.Aggregate(
                config.AvatarUrlFormat,
                (s, claim) => s.Contains($"@{{{claim.Type}}}") ? s.Replace($"@{{{claim.Type}}}", claim.Value) : s);
        }

        foreach (Claim claim in result.User.Claims)
        {
            // Role processing
            // The regex matches any "." not preceded by a "\": a.b.c will be split into a, b, and c, but a.b\.c will be split into a, b.c (after processing the escaped dots)
            // We have to first process the RoleClaim string
            string[] segments = string.IsNullOrEmpty(config.RoleClaim)
                ? []
                : Regex.Split(config.RoleClaim.Trim(), @"(?<!\\)\.");

            if (segments.Length == 0 || segments[0] == "")
            {
                continue;
            }

            // Now we make sure that any escaped "."s ("\.") are replaced with "."
            segments = segments.Select(i => i.Replace("\\.", ".")).ToArray();

            // if current claim is configured role claim, process roles
            if (claim.Type == segments[0])
            {
                ProcessRoles(segments, claim, config, timedState);
            }
        }

        Claim usernameClaim = result.User.Claims.FirstOrDefault(claim =>
            claim.Type == (config.DefaultUsernameClaim?.Trim() ?? "preferred_username"));

        if (usernameClaim != null)
        {
            timedState.Username = usernameClaim.Value;
            if (config.Roles == null || config.Roles.Length == 0)
            {
                timedState.Valid = true;
            }
        }
        else
        {
            // Fallback to the sub as a username
            Claim subClaim = result.User.Claims.FirstOrDefault(claim => claim.Type == "sub");
            if (subClaim != null)
            {
                timedState.Username = subClaim.Value;
                if (config.Roles == null || config.Roles.Length == 0)
                {
                    timedState.Valid = true;
                }
            }
        }

        if (!timedState.Valid)
        {
            _logger.LogWarning(
                "OpenID user {Username} has one or more incorrect role claims: {@Claims}. Expected any one of: {@ExpectedClaims}",
                timedState.Username,
                result.User.Claims.Select(o => new { o.Type, o.Value }),
                config.Roles);

            return Unauthorized("Error. Check permissions.");
        }

        bool isLinking = timedState.IsLinking;
        _logger.LogInformation($"Is request linking: {isLinking}");
        return Content(WebResponse.Generator(state,
                provider,
                GetRequestBase(config.UseHTTP, config.PortOverride),
                isLinking),
            MediaTypeNames.Text.Html);
    }

    private OidcClient CreateClient(string provider, OidConfig config, out ActionResult configError)
    {
        string endpoint = config.OidEndpoint.Trim();
        if (string.IsNullOrEmpty(endpoint))
        {
            configError = BadRequest("No IdP endpoint configured for provider");
            return null;
        }

        string clientId = config.OidClientId.Trim();
        if (string.IsNullOrEmpty(clientId))
        {
            configError = BadRequest("No client ID configured for provider");
            return null;
        }

        string clientSecret = config.OidSecret.Trim();
        if (string.IsNullOrEmpty(clientSecret))
        {
            configError = BadRequest("No client secret configured for provider");
            return null;
        }

        configError = null;

        string[] scopes = config.OidScopes ?? new string[2];
        var options = new OidcClientOptions
        {
            Authority = endpoint,
            ClientId = clientId,
            ClientSecret = clientSecret,
            RedirectUri = GetRequestBase(config.UseHTTP, config.PortOverride)
                          + $"/OpenIDConnect/redirect/{provider}",
            Scope = string.Join(" ", scopes.Prepend("openid profile")),
            DisablePushedAuthorization = config.DisablePushedAuthorization,
            LoggerFactory = _loggerFactory,
            LoadProfile = !config.DoNotLoadProfile,
            HttpClientFactory = _ =>
            {
                HttpClient client = _httpClientFactory.CreateClient();
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
                    AdditionalEndpointBaseAddresses = { new Uri(endpoint).GetLeftPart(UriPartial.Authority) },
                    ValidateEndpoints = !config.DoNotValidateEndpoints,
                    RequireHttps = !config.DisableHttps,
                    ValidateIssuerName = !config.DoNotValidateIssuerName,
                },
            },
        };

        var oidcClient = new OidcClient(options);
        return oidcClient;
    }

    private static void ProcessRoles(
        string[] segments,
        Claim claim,
        OidConfig config,
        TimedAuthorizeState timedState)
    {
        List<string> roles;
        // If we are not using JSON values, just use the raw info from the claim value
        if (segments.Length == 1)
        {
            roles = [claim.Value];
        }
        else
        {
            // We recursively traverse through the JSON data for the roles and parse it
            var json = JsonConvert.DeserializeObject<IDictionary<string, object>>(claim.Value);
            if (json is null)
            {
                roles = [];
            }
            else
            {
                bool missingSegment = false;
                for (int i = 1; i < segments.Length - 1; i++)
                {
                    string segment = segments[i];
                    if (!json.TryGetValue(segment, out object nextToken)
                        || nextToken is not JObject nextObject)
                    {
                        missingSegment = true;
                        break;
                    }

                    json = nextObject.ToObject<IDictionary<string, object>>();
                    if (json is null)
                    {
                        missingSegment = true;
                        break;
                    }
                }

                if (missingSegment || !json.TryGetValue(segments[^1], out object rolesToken)
                                   || rolesToken is not JArray rolesArray)
                {
                    roles = [];
                }
                else
                {
                    // The final step is to take the JSON and turn it from a dictionary into a string
                    roles = rolesArray.ToObject<List<string>>();
                }
            }
        }

        foreach (string role in roles)
        {
            // Check if allowed to login based on roles
            if (config.Roles?.Contains(role) ?? false)
            {
                timedState.Valid = true;
            }

            // Check if admin based on roles
            if (config.AdminRoles?.Contains(role) ?? false)
            {
                timedState.Admin = true;
                // Also allow login (as the user is an admin)
                timedState.Valid = true;
            }

            // Get allowed folders from roles
            if (config.EnableFolderRoles)
            {
                foreach (FolderRoleMap map in config.FolderRoleMapping.Where(map =>
                             role.Equals(map.Role.Trim(), StringComparison.Ordinal)))
                {
                    timedState.Folders.AddRange(map.Folders);
                }
            }

            if (config.EnableLiveTvRoles)
            {
                // Check if allowed Live TV based on roles
                if (config.LiveTvRoles?.Contains(role) ?? false)
                {
                    timedState.EnableLiveTv = true;
                }


                // Check if allowed Live TV management based on roles
                if (config.LiveTvManagementRoles?.Contains(role) ?? false)
                {
                    timedState.EnableLiveTvManagement = true;
                }
            }
        }
    }

    /// <summary>
    ///     Initiates the login flow for OpenID. This redirects the user to the auth provider.
    /// </summary>
    /// <param name="provider">The name of the provider.</param>
    /// <param name="isLinking">Whether or not this request is to link accounts (Rather than authenticate).</param>
    /// <returns>An asynchronous result for the authentication.</returns>
    [HttpGet("start/{provider}")]
    public async Task<ActionResult> Challenge(string provider, [FromQuery] bool isLinking = false)
    {
        Invalidate();

        if (!OpenIDConnect.Instance.Configuration.OidConfigs.TryGetValue(provider, out OidConfig config)
            || !config.Enabled)
        {
            throw new ArgumentException("Provider does not exist");
        }

        OidcClient oidcClient = CreateClient(provider, config, out ActionResult configError);
        if (configError != null)
        {
            return configError;
        }

        AuthorizeState state = await oidcClient.PrepareLoginAsync().ConfigureAwait(false);

        if (state.IsError)
        {
            return BadRequest($"Error preparing login: {state.Error} - {state.ErrorDescription}");
        }

        var timedState = new TimedAuthorizeState(state, DateTime.UtcNow)
        {
            IsLinking = isLinking,
        };

        StateManager.TryAdd(state.State, timedState);

        return Redirect(state.StartUrl);
    }

    /// <summary>
    ///     Adds an OpenID auth configuration. Requires administrator privileges. If the provider already exists, it will be
    ///     removed and readded.
    /// </summary>
    /// <param name="provider">The name of the provider to add.</param>
    /// <param name="config">The OID configuration (deserialized from a JSON post).</param>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpPost("Add/{provider}")]
    public static void AddProvider(string provider, [FromBody] OidConfig config)
    {
        PluginConfiguration configuration = OpenIDConnect.Instance.Configuration;
        configuration.OidConfigs[provider] = config;
        OpenIDConnect.Instance.UpdateConfiguration(configuration);
    }

    /// <summary>
    ///     Deletes an OpenID provider.
    /// </summary>
    /// <param name="provider">Name of provider to delete.</param>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpGet("Del/{provider}")]
    public static void DeleteProvider(string provider)
    {
        PluginConfiguration configuration = OpenIDConnect.Instance.Configuration;
        configuration.OidConfigs.Remove(provider);
        OpenIDConnect.Instance.UpdateConfiguration(configuration);
    }

    /// <summary>
    ///     Lists the OpenID providers configured. Requires administrator privileges.
    /// </summary>
    /// <returns>The list of OpenID configurations.</returns>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpGet("Get")]
    public ActionResult GetProviders()
    {
        return Ok(OpenIDConnect.Instance.Configuration.OidConfigs);
    }

    /// <summary>
    ///     Lists the OpenID providers names only.
    /// </summary>
    /// <returns>The list of OpenID configurations.</returns>
    [HttpGet("GetNames")]
    public ActionResult GetProviderNames()
    {
        return Ok(OpenIDConnect.Instance.Configuration.OidConfigs.Keys);
    }

    /// <summary>
    ///     This is a debug endpoint to list all running OpenID flows. Requires administrator privileges.
    /// </summary>
    /// <returns>The list of OpenID flows in progress.</returns>
    [Authorize(Policy = Policies.RequiresElevation)]
    [HttpGet("States")]
    public ActionResult GetRunningFlows()
    {
        return Ok(StateManager);
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
        OidConfig config;
        try
        {
            config = OpenIDConnect.Instance.Configuration.OidConfigs[provider];

            if (!config.Enabled)
            {
                throw new KeyNotFoundException();
            }
        }
        catch (KeyNotFoundException)
        {
            return BadRequest("No matching provider found");
        }

        if (!StateManager.TryGetValue(response.Data, out TimedAuthorizeState timedState))
        {
            return Problem("State not found");
        }

        if (!timedState.Valid || timedState.Created < DateTime.UtcNow.AddMinutes(-1))
        {
            return Problem("State is not valid.");
        }

        Guid userId = await CreateCanonicalLinkAndUserIfNotExist(provider, timedState.Username);
        AuthenticationResult authenticationResult = await AuthenticateUser(
                userId,
                timedState.Admin,
                config.EnableAuthorization,
                config.EnableAllFolders,
                timedState.Folders.ToArray(),
                timedState.EnableLiveTv,
                timedState.EnableLiveTvManagement,
                response,
                timedState.AvatarURL)
            .ConfigureAwait(false);

        StateManager.TryRemove(response.Data, out _);
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
        User user = _userManager.GetUserByName(username);
        if (user == null)
        {
            return NotFound("User not found");
        }
        user.AuthenticationProviderId = provider;
        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

        // TODO: remove zombie canconical links?
        return Ok();
    }

    private static SerializableDictionary<string, Guid> GetCanonicalLinks(string provider)
    {
        SerializableDictionary<string, Guid> links = OpenIDConnect.Instance.Configuration.OidConfigs[provider]
            .CanonicalLinks ?? new SerializableDictionary<string, Guid>();

        return links;
    }

    private async Task<Guid> CreateCanonicalLinkAndUserIfNotExist(string provider, string canonicalName)
    {
        // First try to get the user by its id in case it was already registered before
        Guid userId;
        try
        {
            userId = GetCanonicalLink(provider, canonicalName);
        }
        catch (KeyNotFoundException)
        {
            userId = Guid.Empty;
        }

        // No userId found? Let's try and find the user by name instead
        User user = userId == Guid.Empty ? _userManager.GetUserByName(canonicalName) : _userManager.GetUserById(userId);

        if (user == null)
        {
            _logger.LogInformation($"SSO user {canonicalName} doesn't exist, creating...");
            user = await _userManager.CreateUserAsync(canonicalName).ConfigureAwait(false);

            userId = user.Id;

            // PATCH: Strip default Jellyfin permissions exactly once on creation
            // Either permissions will be overwritten by provider, or this will let them default to none
            // like the text says it does.
            UserPolicy policy = _userManager.GetUserDto(user).Policy;
            policy.EnableAllFolders = false;
            policy.EnabledFolders = [];
            await _userManager.UpdatePolicyAsync(user.Id, policy).ConfigureAwait(false);

            user.AuthenticationProviderId = "Jellyfin.Plugin.OpenIDConnect.AuthProvider";
            await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

            // Make sure there aren't any trailing existing links
            SerializableDictionary<string, Guid> links = GetCanonicalLinks(provider);
            links.Remove(canonicalName);
            UpdateCanonicalLinkConfig(links, provider);
        }

        if (userId == Guid.Empty)
        {
            _logger.LogInformation("SSO user link doesn't exist, creating...");
            userId = user.Id;
            CreateCanonicalLink(provider, userId, canonicalName);
        }

        return userId;
    }

    private static Guid GetCanonicalLink(string provider, string canonicalName)
    {
        SerializableDictionary<string, Guid> links = GetCanonicalLinks(provider);
        Guid userId = links[canonicalName];
        return userId;
    }

    /// <summary>
    ///     Create a canonical link for a given user. Must be performed by the user being changed, or admin.
    /// </summary>
    /// <param name="provider">The name of the provider to link to a jellyfin account.</param>
    /// <param name="jellyfinUserId">The user ID within jellyfin to link to the provider.</param>
    /// <param name="authResponse">The client information to authenticate the user with.</param>
    /// <returns>Whether this API endpoint succeeded.</returns>
    [Authorize]
    [HttpPost("Link/{provider}/{jellyfinUserId}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> AddCanonicalLink(
        [FromRoute] string provider,
        [FromRoute] Guid jellyfinUserId,
        [FromBody] AuthResponse authResponse)
    {
        if (!await RequestHelpers.AssertCanUpdateUser(_authContext, HttpContext.Request, jellyfinUserId, true)
                .ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "User is not allowed to link SSO providers.");
        }

        return Link(provider, jellyfinUserId, authResponse);
    }

    /// <summary>
    ///     Unregisters a given mapping from id within provider to user.
    /// </summary>
    /// <param name="provider">The name of the provider from which the link should be removed.</param>
    /// <param name="jellyfinUserId">The user ID within jellyfin to unlink from the provider.</param>
    /// <param name="canonicalName">The user ID within jellyfin to unlink.</param>
    /// <returns>Whether this API endpoint succeeded.</returns>
    [Authorize]
    [HttpDelete("Link/{provider}/{jellyfinUserId}/{canonicalName}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult> DeleteCanonicalLink(
        [FromRoute] string provider,
        [FromRoute] Guid jellyfinUserId,
        [FromRoute] string canonicalName)
    {
        if (!await RequestHelpers.AssertCanUpdateUser(_authContext, HttpContext.Request, jellyfinUserId, true)
                .ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                "Current user is not allowed to unlink SSO providers for user ID.");
        }

        Guid linkedId = GetCanonicalLink(provider, canonicalName);

        if (linkedId != jellyfinUserId)
        {
            return Conflict("Jellyfin User ID does not match the user id registered to that canonical name.");
        }

        SerializableDictionary<string, Guid> links = GetCanonicalLinks(provider);

        links.Remove(canonicalName);

        return UpdateCanonicalLinkConfig(links, provider);
    }

    /// <summary>
    ///     Gets all the canonical links for a user.
    /// </summary>
    /// <param name="jellyfinUserId">The user ID within jellyfin for which to return the links.</param>
    /// <returns>A dictionary of provider : link mappings.</returns>
    [Authorize]
    [HttpGet("Links/{jellyfinUserId}")]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ActionResult<SerializableDictionary<string, IEnumerable<string>>>> GetLinksByUser(
        Guid jellyfinUserId)
    {
        if (!await RequestHelpers.AssertCanUpdateUser(_authContext, HttpContext.Request, jellyfinUserId, true)
                .ConfigureAwait(false))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                "Non-admin is not allowed to query other user's mappings.");
        }

        var mappings = new SerializableDictionary<string, IEnumerable<string>>();
        SerializableDictionary<string, OidConfig> providerList = OpenIDConnect.Instance.Configuration.OidConfigs;

        foreach (string providerName in providerList.Keys)
        {
            SerializableDictionary<string, Guid> canonLinks = providerList[providerName].CanonicalLinks;
            IEnumerable<string> canonKeys = canonLinks
                .Where(link => link.Value == jellyfinUserId)
                .Select(link => link.Key);
            mappings[providerName] = canonKeys;
        }

        return mappings;
    }

    /// <summary>
    ///     Validate an OIDC link request and create the link if it is valid.
    /// </summary>
    /// <param name="provider">The provider to authenticate against.</param>
    /// <param name="jellyfinUserId">
    ///     The ID of the account to be linked to the provider.
    ///     Must be performed by this user, or an admin.
    /// </param>
    /// <param name="response">The data passed to the client to ensure it is the right one.</param>
    /// <returns>JSON for the client to populate information with.</returns>
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    private ActionResult Link(string provider, Guid jellyfinUserId, AuthResponse response)
    {
        if (!OpenIDConnect.Instance.Configuration.OidConfigs.TryGetValue(provider, out _))
        {
            return BadRequest("No matching provider found");
        }

        if (!StateManager.TryGetValue(response.Data, out TimedAuthorizeState timedState))
        {
            return Problem("State not found");
        }

        // check if state is still valid
        if (!timedState.Valid || timedState.Created < DateTime.UtcNow.AddMinutes(-1))
        {
            return Problem("State is not valid");
        }

        StateManager.TryRemove(response.Data, out _);
        return CreateCanonicalLink(provider, jellyfinUserId, timedState.Username);
    }

    private ActionResult CreateCanonicalLink(string provider, [FromRoute] Guid jellyfinUserId, string providerUserId)
    {
        SerializableDictionary<string, Guid> links;
        try
        {
            links = GetCanonicalLinks(provider);
        }
        catch (KeyNotFoundException)
        {
            return BadRequest("No matching provider found");
        }

        links[providerUserId] = jellyfinUserId;
        UpdateCanonicalLinkConfig(links, provider);

        return NoContent();
    }

    private OkResult UpdateCanonicalLinkConfig(SerializableDictionary<string, Guid> links, string provider)
    {
        PluginConfiguration configuration = OpenIDConnect.Instance.Configuration;
        configuration.OidConfigs[provider].CanonicalLinks = links;
        OpenIDConnect.Instance.UpdateConfiguration(configuration);
        return Ok();
    }

    /// <summary>
    ///     Authenticates the user with the given information.
    /// </summary>
    /// <param name="userId">The user id of the user to authenticate.</param>
    /// <param name="isAdmin">Determines whether this user is an administrator.</param>
    /// <param name="enableAuthorization">Determines whether RBAC is used for this user.</param>
    /// <param name="enableAllFolders">Determines whether all folders are enabled.</param>
    /// <param name="enabledFolders">Determines which folders should be enabled for this client.</param>
    /// <param name="enableLiveTv">Determines whether live TV access is allowed for this user.</param>
    /// <param name="enableLiveTvAdmin">Determines whether live TV can be managed by this user.</param>
    /// <param name="authResponse">The client information to authenticate the user with.</param>
    /// <param name="avatarUrl">The new avatar url for the user.</param>
    private async Task<AuthenticationResult> AuthenticateUser(
        Guid userId,
        bool isAdmin,
        bool enableAuthorization,
        bool enableAllFolders,
        string[] enabledFolders,
        bool enableLiveTv,
        bool enableLiveTvAdmin,
        AuthResponse authResponse,
        string avatarUrl)
    {
        User user = _userManager.GetUserById(userId);

        // TODO: should have been fixed in https://github.com/jellyfin/jellyfin/pull/16944

        // Persist permissions via UpdatePolicyAsync rather than SetPermission + UpdateUserAsync:
        // on Jellyfin 10.11 the latter does not save the Permissions table (jellyfin/jellyfin#16298).
        // UpdatePolicyAsync loads the user tracked and uses dbContext.Update(user), persisting the
        // whole entity graph. Seed from the current policy so only the fields we manage change.
        UserPolicy policy = _userManager.GetUserDto(user).Policy;

        if (enableAuthorization)
        {
            policy.IsAdministrator = isAdmin;
            policy.EnableAllFolders = enableAllFolders;
            if (!enableAllFolders)
            {
                // Folder IDs arrive as strings; UserPolicy needs Guids. Parse once, dropping any unparseable entries.
                var folderGuids = new List<Guid>(enabledFolders.Length);
                foreach (string folderId in enabledFolders)
                {
                    if (Guid.TryParse(folderId, out Guid folderGuid))
                    {
                        folderGuids.Add(folderGuid);
                    }
                }

                policy.EnabledFolders = folderGuids.ToArray();
            }
        }

        policy.EnableLiveTvAccess = enableLiveTv;
        policy.EnableLiveTvManagement = enableLiveTvAdmin;

        await _userManager.UpdatePolicyAsync(userId, policy).ConfigureAwait(false);

        // UpdatePolicyAsync saved through its own DbContext, so the `user` handle loaded above is
        // now stale (its row-version no longer matches). Re-fetch before any further UpdateUserAsync,
        // otherwise the next save throws DbUpdateConcurrencyException ("0 rows affected").
        user = _userManager.GetUserById(userId);

        // TODO: use claim (default to 'picture'?) instead of custom picture urls

        if (avatarUrl is not null)
        {
            try
            {
                using HttpClient client = _httpClientFactory.CreateClient();

                var assembly = Assembly.GetExecutingAssembly();
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                string version = fvi.FileVersion;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    $"Jellyfin-OpenID-Connect +{version} (https://github.com/DaniCatGames/jellyfin-openid-connect)");

                HttpResponseMessage avatarResponse = await client.GetAsync(avatarUrl);

                if (!avatarResponse.Content.Headers.TryGetValues("content-type",
                        out IEnumerable<string> contentTypeList))
                {
                    throw new Exception("Cannot get Content-Type of image : " + avatarUrl);
                }

                string contentType = contentTypeList.First();
                if (!contentType.StartsWith("image"))
                {
                    throw new Exception("Content type of avatar URL is not an image, got :  " + contentType);
                }

                string extension = contentType.Split("/").Last();
                Stream stream = await avatarResponse.Content.ReadAsStreamAsync();

                if (user != null)
                {
                    string userDataPath =
                        Path.Combine(
                            _serverConfigurationManager.ApplicationPaths.UserConfigurationDirectoryPath,
                            user.Username);
                    if (user.ProfileImage is not null)
                    {
                        await _userManager.ClearProfileImageAsync(user).ConfigureAwait(false);
                    }

                    user.ProfileImage = new ImageInfo(Path.Combine(userDataPath, "profile" + extension));

                    await _providerManager.SaveImage(stream, contentType, user.ProfileImage.Path)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
            }
        }

        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);

        var authRequest = new AuthenticationRequest
        {
            UserId = user.Id,
            Username = user.Username,
            App = authResponse.AppName,
            AppVersion = authResponse.AppVersion,
            DeviceId = authResponse.DeviceID,
            DeviceName = authResponse.DeviceName,
        };
        _logger.LogInformation("Auth request created...");

        return await _sessionManager.AuthenticateDirect(authRequest).ConfigureAwait(false);
    }

    private static void Invalidate()
    {
        DateTime cutoff = DateTime.UtcNow.AddMinutes(-1);

        foreach (KeyValuePair<string, TimedAuthorizeState> kvp in StateManager.Where(kvp => kvp.Value.Created < cutoff))
        {
            StateManager.TryRemove(kvp);
        }
    }

    private string GetRequestBase(bool useHttp = false, int? portOverride = null)
    {
        int requestPort = portOverride ?? Request.Host.Port ?? -1;

        if (requestPort == 80 && string.Equals(Request.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            || requestPort == 443 && string.Equals(Request.Scheme, "https", StringComparison.OrdinalIgnoreCase))
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
public class AuthResponse
{
    /// <summary>
    ///     Gets or sets the device ID of the client.
    /// </summary>
    public string DeviceID { get; set; }

    /// <summary>
    ///     Gets or sets the device name of the client.
    /// </summary>
    public string DeviceName { get; set; }

    /// <summary>
    ///     Gets or sets the app name of the client.
    /// </summary>
    public string AppName { get; set; }

    /// <summary>
    ///     Gets or sets the app version of the client.
    /// </summary>
    public string AppVersion { get; set; }

    /// <summary>
    ///     Gets or sets the auth data of the client (for authorizing the response).
    /// </summary>
    public string Data { get; set; }
}

/// <summary>
///     A manager for OpenID to manage the state of the clients.
/// </summary>
public class TimedAuthorizeState
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TimedAuthorizeState" /> class.
    /// </summary>
    /// <param name="state">The AuthorizeState to time.</param>
    /// <param name="created">When this state was created.</param>
    public TimedAuthorizeState(AuthorizeState state, DateTime created)
    {
        State = state;
        Created = created;
        Valid = false;
        Admin = false;
        IsLinking = false;
        EnableLiveTv = false;
        EnableLiveTvManagement = false;
        AvatarURL = null;
    }

    /// <summary>
    ///     Gets or sets the Authorization State of the client.
    /// </summary>
    public AuthorizeState State { get; set; }

    /// <summary>
    ///     Gets or sets when this object was created to time it out.
    /// </summary>
    public DateTime Created { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the user is valid.
    /// </summary>
    public bool Valid { get; set; }

    /// <summary>
    ///     Gets or sets the user tied to the state.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the user is an administrator.
    /// </summary>
    public bool Admin { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the state is
    ///     tied to a linking flow (instead of a login flow).
    /// </summary>
    public bool IsLinking { get; set; }

    /// <summary>
    ///     Gets or sets the folders the user is allowed access to.
    /// </summary>
    public List<string> Folders { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the user is allowed to view live TV.
    /// </summary>
    public bool EnableLiveTv { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the user is allowed to manage live TV.
    /// </summary>
    public bool EnableLiveTvManagement { get; set; }

    /// <summary>
    ///     Gets or sets the user avatar url.
    /// </summary>
    public string AvatarURL { get; set; }
}