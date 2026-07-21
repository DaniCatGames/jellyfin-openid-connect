using System;
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
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.OpenIDConnect.Services;
using Jellyfin.Plugin.OpenIDConnect.Views;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
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
/// <param name="sessionManager">Instance of the <see cref="ISessionManager" /> interface.</param>
/// <param name="userManager">Instance of the <see cref="IUserManager" /> interface.</param>
/// <param name="providerManager">Instance of the <see cref="IProviderManager" /> interface.</param>
/// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory" /> interface.</param>
/// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager" /> interface.</param>
/// <param name="stateManager">Instance of the <see cref="IStateManager" /> interface.</param>
/// <param name="linkManager">Instance of the <see cref="ILinkManager" /> interface.</param>
[ApiController]
[Route("[controller]")]
public class OpenIDConnectController(
    ILogger<OpenIDConnectController> logger,
    ILoggerFactory loggerFactory,
    ISessionManager sessionManager,
    IUserManager userManager,
    IProviderManager providerManager,
    IHttpClientFactory httpClientFactory,
    IServerConfigurationManager serverConfigurationManager,
    IStateManager stateManager,
    ILinkManager linkManager
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
            || stateManager.IsExpired(timedState))
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

        if (timedState.IsTesting)
        {
            stateManager.TryRemove(state, out _);

            string htmlOutput = WebResponse.GenerateHtmlTestingPage(provider, result.User.Claims);

            return Content(htmlOutput, "text/html");
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

        Claim avatarClaim = result.User.Claims.FirstOrDefault(claim =>
            claim.Type == (string.IsNullOrWhiteSpace(config.AvatarClaim) ? "picture" : config.AvatarClaim));

        if (avatarClaim != null
            && Uri.TryCreate(avatarClaim.Value, UriKind.Absolute, out Uri uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            timedState.AvatarURL = uri.ToString();
        }

        foreach (Claim claim in result.User.Claims)
        {
            // Role processing
            // The regex matches any "." not preceded by a "\": a.b.c will be split into a, b, and c, but a.b\.c will be split into a, b.c (after processing the escaped dots)
            // We have to first process the RoleClaim string
            string[] segments = string.IsNullOrEmpty(config.RoleClaim)
                ? ["groups"]
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

        Claim subClaim = result.User.Claims.FirstOrDefault(claim => claim.Type == "sub");
        if (subClaim != null)
        {
            timedState.Sub = subClaim.Value;
            if (config.Roles == null || config.Roles.Length == 0)
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
        timedState.Username = usernameClaim != null ? usernameClaim.Value : timedState.Sub;

        if (!timedState.Valid)
        {
            logger.LogWarning(
                "OpenID user {Username} has one or more incorrect role claims: {@Claims}. Expected any one of: {@ExpectedClaims}",
                timedState.Username,
                result.User.Claims.Select(o => new { o.Type, o.Value }),
                config.Roles);

            return Unauthorized("Error. Check permissions.");
        }

        bool isLinking = timedState.IsLinking;
        logger.LogInformation($"Is request linking: {isLinking}");
        return Content(WebResponse.Generator(state,
                provider,
                GetRequestBase(config.UseHTTP, config.PortOverride),
                isLinking),
            MediaTypeNames.Text.Html);
    }

    private OidcClient CreateClient(string provider, Config config, out ActionResult configError)
    {
        string endpoint = config.Endpoint.Trim();
        if (string.IsNullOrEmpty(endpoint))
        {
            configError = BadRequest("No IdP endpoint configured for provider");
            return null;
        }

        string clientId = config.ClientId.Trim();
        if (string.IsNullOrEmpty(clientId))
        {
            configError = BadRequest("No client ID configured for provider");
            return null;
        }

        string clientSecret = config.Secret.Trim();
        if (string.IsNullOrEmpty(clientSecret))
        {
            configError = BadRequest("No client secret configured for provider");
            return null;
        }

        configError = null;

        string[] scopes = config.Scopes ?? new string[2];
        var options = new OidcClientOptions
        {
            Authority = endpoint,
            ClientId = clientId,
            ClientSecret = clientSecret,
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

    private void ProcessRoles(
        string[] segments,
        Claim claim,
        Config config,
        TimedAuthorizeState timedState)
    {
        List<string> roles;
        if (segments.Length == 1)
        {
            // If we are not using JSON values, just use the raw info from the claim value
            roles = [claim.Value];
        }
        else
        {
            try
            {
                JToken currentToken = JToken.Parse(claim.Value);

                for (int i = 1; i < segments.Length; i++) currentToken = currentToken?[segments[i]];

                if (currentToken is JArray rolesArray)
                {
                    roles = rolesArray.ToObject<List<string>>() ?? [];
                }
                else
                {
                    throw new JsonException("Role claim is not an array");
                }
            }
            catch (JsonException error)
            {
                logger.LogError(error, "Error parsing JSON role claim: {Claim}", claim.Value);
                return;
            }
        }

        foreach (string role in roles)
        {
            // Check if allowed to login based on roles
            if (config.Roles?.Contains(role) == true)
            {
                timedState.Valid = true;
            }

            // Check if admin based on roles
            if (config.AdminRoles?.Contains(role) == true)
            {
                timedState.Admin = true;
                // Also allow login (as the user is an admin)
                timedState.Valid = true;
            }

            // Get allowed folders from roles
            if (config.EnableFolderRoles)
            {
                IEnumerable<string> folders = config.FolderRoleMapping
                    .Where(map => role.Equals(map.Role.Trim(), StringComparison.Ordinal))
                    .SelectMany(map => map.Folders ?? []);

                timedState.Folders.AddRange(folders);
            }

            if (config.EnableLiveTvRoles)
            {
                // Check if allowed Live TV based on roles
                if (config.LiveTvRoles?.Contains(role) == true)
                {
                    timedState.EnableLiveTv = true;
                }


                // Check if allowed Live TV management based on roles
                if (config.LiveTvManagementRoles?.Contains(role) == true)
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

        if (!timedState.Valid || timedState.Created < DateTime.UtcNow.AddMinutes(-1))
        {
            return Problem("State is not valid.");
        }

        Guid userId = await GetOrCreateUser(provider, timedState.Sub, timedState.Username, config);

        if (userId == Guid.Empty)
        {
            stateManager.TryRemove(response.Data, out _);
            return Unauthorized("User provisioning or linking with this user is disabled.");
        }

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

        user.AuthenticationProviderId = provider;
        await userManager.UpdateUserAsync(user).ConfigureAwait(false);

        linkManager.DeleteLinksToUser(user.Id);

        return Ok();
    }

    private async Task<Guid> GetOrCreateUser(string provider, string sub, string username, Config config)
    {
        // Check if there is already a link for this sub, else get empty id
        bool linkExists = linkManager.TryGetLink(provider, sub, out Guid userId);
        User user;

        if (linkExists)
        {
            user = userManager.GetUserById(userId);
            if (user != null)
            {
                logger.LogInformation("Found link to jellyfin user {username} from sub {sub} on IdP {provider}.",
                    user.Username,
                    sub,
                    provider);
                return user.Id;
            }

            logger.LogWarning(
                "OIDC user link exists between sub {sub} and jellyfin userId {userid}, but jellyfin user doesn't exist.",
                sub,
                userId);
            return Guid.Empty;
        }

        // There is no link to this user yet.
        // Try to get user by username
        user = userManager.GetUserByName(username);

        if (user != null)
        {
            // If a user is found with the same username, check if they can be linked, else stop and dont continue
            if (config.AutoLinkingAllowList == null
                || !config.AutoLinkingAllowList.Contains(username, StringComparer.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "OIDC user {Username} already exists, but is not in the linking allowlist. Not linking user.",
                    username);
                return Guid.Empty;
            }

            config.AutoLinkingAllowList = config.AutoLinkingAllowList
                .Where(u => !u.Equals(username, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            OpenIDConnect.Instance.UpdateConfiguration(OpenIDConnect.Instance.Configuration);

            logger.LogInformation(
                "OIDC user link doesn't exist, creating new link between sub {sub} and jellyfin user {username}.",
                sub,
                username);
            linkManager.TryCreateLink(provider, sub, user.Id);
            return user.Id;
        }


        // There is no jellyfin user with the same username at all, so create a new one, if provisioning is enabled
        if (!config.EnableUserProvisioning)
        {
            return Guid.Empty;
        }

        logger.LogInformation("OIDC user {Username} doesn't exist, creating...", username);
        user = await CreateUserAndLink(provider, sub, username);
        return user.Id;
    }

    private async Task<User> CreateUserAndLink(string provider, string sub, string username)
    {
        User user = await userManager.CreateUserAsync(username).ConfigureAwait(false);

        user.SetPermission(PermissionKind.EnableAllFolders, false);
        user.SetPreference(PreferenceKind.EnabledFolders, []);

        user.AuthenticationProviderId = "Jellyfin.Plugin.OpenIDConnect.AuthProvider";
        await userManager.UpdateUserAsync(user).ConfigureAwait(false);

        linkManager.TryCreateLink(provider, sub, user.Id);
        return user;
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
        User user = userManager.GetUserById(userId);

        if (user == null)
        {
            throw new Exception("User not found");
        }

        if (enableAuthorization)
        {
            user.SetPermission(PermissionKind.IsAdministrator, isAdmin);
            user.SetPermission(PermissionKind.EnableAllFolders, enableAllFolders);

            if (!enableAllFolders)
            {
                // Folder IDs arrive as strings; EnabledFolders preference needs Guids. Parse once, dropping any
                // unparseable entries.
                var folderGuids = new List<Guid>(enabledFolders.Length);
                foreach (string folderId in enabledFolders)
                {
                    if (Guid.TryParse(folderId, out Guid folderGuid))
                    {
                        folderGuids.Add(folderGuid);
                    }
                }

                user.SetPreference(PreferenceKind.EnabledFolders, folderGuids.ToArray());
            }
        }

        user.SetPermission(PermissionKind.EnableLiveTvAccess, enableLiveTv);
        user.SetPermission(PermissionKind.EnableLiveTvManagement, enableLiveTvAdmin);

        if (avatarUrl is not null)
        {
            await SetUserAvatar(avatarUrl, user).ConfigureAwait(false);
        }

        await userManager.UpdateUserAsync(user).ConfigureAwait(false);

        var authRequest = new AuthenticationRequest
        {
            UserId = user.Id,
            Username = user.Username,
            App = authResponse.AppName,
            AppVersion = authResponse.AppVersion,
            DeviceId = authResponse.DeviceId,
            DeviceName = authResponse.DeviceName,
        };
        logger.LogInformation("Auth request created...");

        return await sessionManager.AuthenticateDirect(authRequest).ConfigureAwait(false);
    }

    private async Task SetUserAvatar(string avatarUrl, User user)
    {
        try
        {
            using HttpClient client = httpClientFactory.CreateClient();

            var assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            string version = fvi.FileVersion;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"Jellyfin-OpenID-Connect +{version} (https://github.com/DaniCatGames/jellyfin-openid-connect)");

            HttpResponseMessage avatarResponse = await client.GetAsync(avatarUrl);

            if (!avatarResponse.IsSuccessStatusCode)
            {
                throw new Exception("Cannot get avatar image: " + avatarUrl);
            }

            if (!avatarResponse.Content.Headers.TryGetValues("content-type",
                    out IEnumerable<string> contentTypeList))
            {
                throw new Exception("Cannot get Content-Type of image : " + avatarUrl);
            }

            string contentType = contentTypeList.First().ToLowerInvariant();
            string extension = contentType switch
            {
                "image/jpeg" or "image/jpg" => ".jpg",
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "image/avif" => ".avif",
                "image/apng" => ".apng",
                "image/bmp" => ".bmp",
                "image/heic" => ".heic",
                "image/heif" => ".heif",
                "image/jxl" => ".jxl",
                _ => throw new Exception("Content type of avatar URL is not an image, got :  " + contentType),
            };

            Stream stream = await avatarResponse.Content.ReadAsStreamAsync();

            string userDataPath =
                Path.Combine(
                    serverConfigurationManager.ApplicationPaths.UserConfigurationDirectoryPath,
                    user.Username);
            if (user.ProfileImage is not null)
            {
                await userManager.ClearProfileImageAsync(user).ConfigureAwait(false);
            }

            user.ProfileImage = new ImageInfo(Path.Combine(userDataPath, "profile" + extension));

            await providerManager.SaveImage(stream, contentType, user.ProfileImage.Path)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e.Message);
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

// ReSharper disable UnusedAutoPropertyAccessor.Global
/// <summary>
///     The data the client should pass back to the API.
/// </summary>
public class AuthResponse
{
    /// <summary>
    ///     Gets or sets the device ID of the client.
    /// </summary>
    public string DeviceId { get; set; }

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
// ReSharper restore UnusedAutoPropertyAccessor.Global