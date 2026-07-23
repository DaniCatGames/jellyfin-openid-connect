using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.OpenIDConnect.Api;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.OpenIDConnect.Services;

/// <inheritdoc />
public class OidcUserManager(
    IUserManager userManager,
    ILinkManager linkManager,
    IProviderManager providerManager,
    ILogger<OidcUserManager> logger,
    ISessionManager sessionManager,
    IServerConfigurationManager serverConfigurationManager,
    IHttpClientFactory httpClientFactory) : IOidcUserManager
{
    /// <inheritdoc />
    public async Task<Guid> GetOrCreateUser(string provider, TimedAuthorizeState timedState, Config config)
    {
        // Check if there is already a link for this sub, else get empty id
        bool linkExists = linkManager.TryGetLink(provider, timedState.Sub, out Guid userId);
        User user;

        if (linkExists)
        {
            user = userManager.GetUserById(userId);
            if (user != null)
            {
                logger.LogInformation("Found link to jellyfin user {username} from sub {sub} on IdP {provider}.",
                    user.Username,
                    timedState.Sub,
                    provider);
                return user.Id;
            }

            logger.LogWarning(
                "OIDC user link exists between sub {sub} and jellyfin userId {userid}, but jellyfin user doesn't exist.",
                timedState.Sub,
                userId);
            return Guid.Empty;
        }

        // There is no link to this user yet.
        // Try to get user by username
        user = userManager.GetUserByName(timedState.Username);

        if (user != null)
        {
            // If a user is found with the same username, check if they can be linked, else stop and dont continue
            if (config.AutoLinkingAllowList == null
                || !config.AutoLinkingAllowList.Contains(timedState.Username, StringComparer.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "OIDC user {Username} already exists, but is not in the linking allowlist. Not linking user.",
                    timedState.Username);
                return Guid.Empty;
            }

            config.AutoLinkingAllowList = config.AutoLinkingAllowList
                .Where(u => !u.Equals(timedState.Username, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            OpenIDConnect.Instance.UpdateConfiguration(OpenIDConnect.Instance.Configuration);

            logger.LogInformation(
                "OIDC user link doesn't exist, creating new link between sub {sub} and jellyfin user {username}.",
                timedState.Sub,
                user.Username);
            linkManager.TryCreateLink(provider, timedState.Username, user.Id);
            return user.Id;
        }


        // There is no jellyfin user with the same username at all, so create a new one, if provisioning is enabled
        if (!config.EnableUserProvisioning)
        {
            return Guid.Empty;
        }

        logger.LogInformation("OIDC user {Username} doesn't exist, creating...", timedState.Username);
        user = await CreateUserAndLink(provider, timedState.Sub, timedState.Username).ConfigureAwait(false);
        await UpdateUser(config, timedState, user).ConfigureAwait(false);
        return user.Id;
    }

    /// <inheritdoc />
    public async Task<AuthenticationResult> AuthenticateUser(
        Guid userId,
        AuthResponse authResponse,
        Config config,
        TimedAuthorizeState timedState,
        string ipAddress)
    {
        User user = userManager.GetUserById(userId);

        if (user == null)
        {
            throw new Exception("User not found");
        }

        if (config.UpdateUsersOnLogin)
        {
            await UpdateUser(config, timedState, user).ConfigureAwait(false);
        }

        var authRequest = new AuthenticationRequest
        {
            UserId = user.Id,
            Username = user.Username,
            App = authResponse.AppName,
            AppVersion = authResponse.AppVersion,
            DeviceId = authResponse.DeviceId,
            DeviceName = authResponse.DeviceName,
            RemoteEndPoint = ipAddress,
        };
        logger.LogInformation("Auth request created...");

        return await sessionManager.AuthenticateDirect(authRequest).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UnregisterUser(User user, string provider)
    {
        user.AuthenticationProviderId = provider;
        await userManager.UpdateUserAsync(user).ConfigureAwait(false);

        linkManager.DeleteLinksToUser(user.Id);
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

    private async Task UpdateUser(
        Config config,
        TimedAuthorizeState timedState,
        User user)
    {
        user.SetPermission(PermissionKind.IsAdministrator, timedState.Admin);
        user.SetPermission(PermissionKind.EnableAllFolders, config.EnableAllFolders);

        // Parse folder IDs to GUIDs
        var folderGuids = new List<Guid>(timedState.DefaultAllowedFolders.Count + timedState.RbacFolders.Count);

        // Add folders that are allowed by default
        foreach (string folderId in timedState.DefaultAllowedFolders)
        {
            if (Guid.TryParse(folderId, out Guid folderGuid))
            {
                folderGuids.Add(folderGuid);
            }
        }

        // Add folders that are allowed by RBAC
        foreach (string folderId in timedState.RbacFolders)
        {
            if (Guid.TryParse(folderId, out Guid folderGuid))
            {
                folderGuids.Add(folderGuid);
            }
        }

        user.SetPreference(PreferenceKind.EnabledFolders, folderGuids.ToArray());

        user.SetPermission(PermissionKind.EnableLiveTvAccess,
            config.EnableLiveTvRoles ? timedState.EnableLiveTv : config.EnableLiveTv);
        user.SetPermission(PermissionKind.EnableLiveTvManagement,
            config.EnableLiveTvRoles ? timedState.EnableLiveTvManagement : config.EnableLiveTvManagement);

        if (timedState.AvatarURL is not null)
        {
            await SetUserAvatar(timedState.AvatarURL, user).ConfigureAwait(false);
        }

        await userManager.UpdateUserAsync(user).ConfigureAwait(false);
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
}