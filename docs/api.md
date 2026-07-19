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

These all require authorization. Append an API key to the end of the request: `curl "http://myjellyfin.example.com/sso/OID/Get?api_key=9c6e5fae4ae145669e6b7a3942f813b7"`

- POST `Add/PROVIDER_NAME`: This adds or overwrites a configuration for OpenID with a given provider name. It accepts JSON with the following keys and format:
  - `Endpoint`: string. The OpenID endpoint. Must have a `.well-known` path available.
  - `ClientId`: string. The OpenID client ID.
  - `Secret`: string. The OpenID secret.
  - `Enabled`: boolean. Determines if the provider is enabled or not.
  - `EnableAuthorization`: boolean: Determines if the plugin sets permissions for the user. If false, the user will start with no permissions and an administrator will add permissions. If disabled, then the permissions of users will not be modified and the Jellyfin defaults will be used instead.
  - `EnableAllFolders`: boolean. Determines if the client logging in is allowed access to all folders.
  - `EnabledFolders`: array of strings. If `EnableAllFolders` is set to false, then this will be used to determine what folders the users who log in through this provider are allowed to use.
  - `Roles`: array of strings. This validates the OpenID response against the claim set in `RoleClaim`. If a user has any of these roles, then the user is authenticated. Leave blank to disable role checking.
  - `AdminRoles`: array of strings. This uses the OpenID response against the claim set in `RoleClaim`. If a user has any of these roles, then the user is an admin. Leave blank to disable (default is to not enable admin permissions).
  - `EnableFolderRoles`: boolean. Determines if role-based folder access should be used.
  - `FolderRoleMapping`: object in the format "role": string and "folders": array of strings. The user with this role will have access to the following folders if `EnableFolderRoles` is enabled. To get the IDs of the folders, GET the `/Library/MediaFolders` URL with an API key. Look for the `Id` attribute.
  - `EnableLiveTvRoles`: boolean. Determines if role-based Live TV access should be used.
  - `LiveTvRoles`: array of strings. If `EnableLiveTvRoles` is enabled, then the user's roles will be checked against these. If the user is granted permission, then the user will be able to view Live TV.
  - `LiveTvManagementRoles`: array of strings. If `EnableLiveTvRoles` is enabled, then the user's roles will be checked against these. If the user is granted permission, then the user will be able to manage Live TV.
  - `EnableLiveTv`: boolean. Whether to allow Live TV by default. This applies even if `EnableLiveTvRoles` is enabled.
  - `EnableLiveTvManagement`: boolean. Whether to allow Live TV management by default. This applies even if `EnableLiveTvRoles` is enabled.
  - `RoleClaim`: string. This is the value in the OpenID response to check for roles. For Keycloak, it is `realm_access.roles` by default. The first element is the claim type, the subsequent values are to parse the JSON of the claim value. Use a "\\." to denote a literal ".". This expects a list of strings from the OIDC server.
  - `Scopes` : array of strings. Each contains an additional scope name to include in the OIDC request.
    - For some OIDC providers (For example, [authelia](https://github.com/9p4/jellyfin-plugin-sso/issues/23#issuecomment-1112237616)), additional scopes may be required in order to validate group membership in role claim.
    - Leave empty to only request the default scopes.
  - `DefaultUsernameClaim`: string. The provider will use the claim to create the users' usernames. If not set, it fallbacks to `preferred_username`.
  - `AvatarUrlFormat`: string. The URL format for the users avatars. OIDC claims can be used by using the `@{claim_type}` syntax. If not set, the avatars won't change.
  - `DisableHttps`: boolean. Determines whether the OpenID discovery endpoint requires HTTPS.
  - `DoNotValidateEndpoints`: boolean. Determines whether the OpenID discovery process will validate endpoints. This may be required for Google.
  - `DoNotValidateIssuerName`: boolean. Determines whether the OpenID discovery process will validate the OpenID issuer name.
  - `UseHTTP`: boolean. Force the plugin to use HTTP, can be useful when your provider in on an internal network and uses http.
- GET `Del/PROVIDER_NAME`: This removes a configuration for OpenID for a given provider name.
- GET `Get`: Lists the configurations currently available.
- GET `States`: Lists currently active OpenID flows in progress.

## Misc

- POST `Unregister/username`: This "unregisters" a user from SSO. A JSON-formatted string must be posted with the new authentication provider. To reset to the default provider, use `Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider` like so: `curl -X POST -H "Content-Type: application/json" -d '"Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider"' "https://myjellyfin.example.com/sso/Unregister/username?api_key=API_KEY`
