# API Endpoints

The API is all done from a base URL of `https://jellyfin.example.com/OpenIDConnect/`

## OpenID

### Flow

- GET `redirect/PROVIDER_NAME`: This is the OpenID callback path. This will return HTML and JavaScript for the client to login with a given provider name.
- GET `start/PROVIDER_NAME`: This is the OpenID initiator: it will begin the authorization flow for OpenID with a given provider name.
- POST `Auth/PROVIDER_NAME`: This is the OpenID client-side API: the HTML and JavaScript client will call this endpoint to receive Jellyfin credentials for a given provider name. Post format is in JSON with the following keys:
  - `DeviceId`: string. Device ID.
  - `DeviceName`: string. Device name.
  - `AppName`: string. App name.
  - `AppVersion`: string. App version.
  - `Data`: string. The OpenID state. Used to verify a request.

### Configuration

- GET `Providers/Names`: This lists all providers that have a configuration.

The requests below all require authorization. Append an `ApiKey` to the end of the url, or send an `Authorization` header along with the request. See [here](https://gist.github.com/nielsvanvelzen/ea047d9028f676185832e51ffaf12a6f) for more information.

- PUT `Providers/PROVIDER_NAME`: This adds or overwrites a configuration for OpenID with a given provider name. It accepts JSON with the following keys and format:
  - `Endpoint`: string. The OpenID endpoint. Must have a `.well-known` path available.
  - `ClientId`: string. The OpenID client ID.
  - `Secret`: string. The OpenID secret.
  - `Enabled`: boolean. Determines if the provider is enabled or not.
  - `RoleClaim`: string. This is the value in the OpenID response to check for roles. For Keycloak, it is `realm_access.roles` by default. The first element is the claim type, the subsequent values are to parse the JSON of the claim value. Use a "\\." to denote a literal ".". This expects a list of strings from the OIDC server.
  - `DefaultUsernameClaim`: string. The provider will use the claim to create the users' usernames. If not set, it fallbacks to `preferred_username`.
  - `AvatarClaim`: string. The claim to use for looking up the user's avatar. Must be a URL.
  - `EnableAuthorization`: boolean: Determines if the plugin sets permissions for the user. If false, the user will start with no permissions and an administrator will add permissions. If disabled, then the permissions of users will not be modified and the Jellyfin defaults will be used instead.
  - `Roles`: array of strings. This validates the OpenID response against the claim set in `RoleClaim`. If a user has any of these roles, then the user is authenticated. Leave blank to disable role checking.
  - `AdminRoles`: array of strings. This uses the OpenID response against the claim set in `RoleClaim`. If a user has any of these roles, then the user is an admin. Leave blank to disable (default is to not enable admin permissions).
  - `EnableUserProvisioning`: When enabled, users will be automatically created upon first login if they have no matching link in the allowlist.
  - `AutoLinkingAllowList`: array of strings. Determines which IdP users will be automatically linked to Jellyfin accounts when they log in for the first time. Jellyfin users are removed from the list on their first link.
  - `EnableAllFolders`: boolean. Determines if the client logging in is allowed access to all folders.
  - `EnabledFolders`: array of strings. If `EnableAllFolders` is set to false, then this will be used to determine what folders the users who log in through this provider are allowed to use.
  - `EnableFolderRoles`: boolean. Determines if role-based folder access should be used.
  - `FolderRoleMapping`: object in the format "role": string and "folders": array of strings. The user with this role will have access to the following folders if `EnableFolderRoles` is enabled. To get the IDs of the folders, GET the `/Library/MediaFolders` URL with an API key. Look for the `Id` attribute.
  - `EnableLiveTv`: boolean. Whether to allow Live TV by default. This applies even if `EnableLiveTvRoles` is enabled.
  - `EnableLiveTvManagement`: boolean. Whether to allow Live TV management by default. This applies even if `EnableLiveTvRoles` is enabled.
  - `EnableLiveTvRoles`: boolean. Determines if role-based Live TV access should be used.
  - `LiveTvRoles`: array of strings. If `EnableLiveTvRoles` is enabled, then the user's roles will be checked against these. If the user is granted permission, then the user will be able to view Live TV.
  - `LiveTvManagementRoles`: array of strings. If `EnableLiveTvRoles` is enabled, then the user's roles will be checked against these. If the user is granted permission, then the user will be able to manage Live TV.
  - `Scopes` : array of strings. Each contains an additional scope name to include in the OIDC request. `openid` and `profile` are always included.
    - For some OIDC providers (For example, [authelia](https://github.com/9p4/jellyfin-plugin-sso/issues/23#issuecomment-1112237616)), additional scopes may be required in order to validate group membership in role claim.
    - Leave empty to only request the default scopes.
  - `DisableHttps`: boolean. Determines whether the OpenID discovery endpoint requires HTTPS.
  - `DisablePushedAuthorization`: boolean. Determines whether to disable pushed authorization. May be needed for authelia.
  - `DoNotValidateEndpoints`: boolean. Determines whether the OpenID discovery process will validate endpoints. This may be required for Google.
  - `DoNotValidateIssuerName`: boolean. Determines whether the OpenID discovery process will validate the OpenID issuer name.
  - `DoNotLoadProfile`: boolean. Determines whether the OpenID discovery process will skip loading the profile. May be needed for Cloudflare.
  - `UseHTTP`: boolean. Force the plugin use HTTP for building redirect URLs, can be useful when your provider in on an internal network and uses http.
  - `PortOverride`: integer. Override the port used for the redirect URL.
- DELETE `Providers/PROVIDER_NAME`: This removes a configuration for OpenID for a given provider name.
- GET `Providers`: Lists the configurations currently available.

## Misc

These requests also require authorization.

- POST `Unregister/username`: This "unregisters" a user from SSO. A JSON-formatted string must be posted with the new authentication provider. To reset to the default provider, use `Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider` like so: `curl -X POST -H "Content-Type: application/json" -d '"Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider"' "https://myjellyfin.example.com/sso/Unregister/username?api_key=API_KEY`
- GET `States`: Lists currently active OpenID flows in progress.