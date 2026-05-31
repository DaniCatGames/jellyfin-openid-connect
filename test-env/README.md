# SSO Plugin Test Environment

A reproducible local environment for smoke-testing the Jellyfin SSO plugin
against a real OIDC provider (Dex).

## Prerequisites

- Docker (Docker Desktop or Colima 0.5+)
- `dotnet` SDK matching the plugin's target framework (`net9.0`)
- `bash`, `curl`, `jq`, `tar`, `zstd`

Scripts will check for each and exit cleanly if anything is missing. They
will not install tools for you.

## Quickstart

```bash
test-env/scripts/up.sh
```

This will:

1. Publish the plugin to `test-env/.publish/`.
2. If `test-env/.data/` is empty, restore the baseline snapshot for the
   pinned Jellyfin version.
3. Bring up Jellyfin + Dex via `docker compose`.
4. Register the Dex OIDC provider with the plugin.

Then open <http://localhost:8096/sso/OID/start/dex> in your browser.

## Seeded credentials

Dex's login form uses **email**, not username. The `username` field below
is what surfaces through OIDC as `preferred_username`.

| Email (login)         | Password   | Groups           | Expected Jellyfin role |
| --------------------- | ---------- | ---------------- | ---------------------- |
| `admin@test.local`    | `password` | `jellyfin-admin` | Admin                  |
| `user@test.local`     | `password` | `jellyfin-users` | Regular user           |
| `noaccess@test.local` | `password` | _none_           | Login denied           |

Jellyfin admin (out-of-band, for the admin dashboard):

- Username: `admin`
- Password: `admin`

## Scripts

The bash scripts below are thin wrappers around a C# CLI; see [Direct CLI usage](#direct-cli-usage) for details on calling the orchestrator directly.

| Script                | Purpose                                                 |
| --------------------- | ------------------------------------------------------- |
| `up.sh`               | Full bring-up. Idempotent.                              |
| `down.sh`             | Stop containers. Keeps state.                           |
| `down.sh --volumes`   | Stop containers and wipe `.data/` + `.publish/`.        |
| `reload.sh`           | Republish plugin + restart Jellyfin only.               |
| `provision.sh`        | Re-register the SSO provider (called by `up.sh`).       |
| `snapshot-create.sh`  | Rebuild the baseline from scratch via manual wizard.    |
| `snapshot-refresh.sh` | Migrate an existing snapshot to a new Jellyfin version. |

## Direct CLI usage

The orchestration logic lives in `test-env/SSO-Auth.TestEnv/`. The bash scripts above
are thin wrappers around its subcommands. You can also call the CLI directly:

```bash
dotnet run --project test-env/SSO-Auth.TestEnv -- up
dotnet run --project test-env/SSO-Auth.TestEnv -- down [--volumes]
dotnet run --project test-env/SSO-Auth.TestEnv -- reload
dotnet run --project test-env/SSO-Auth.TestEnv -- provision
```

The bash wrappers exist mainly for shell auto-complete and shorter command lines;
they invoke the same CLI under the hood.

## Version pins

Image pins live in `docker-compose.yml` with defaults. To override locally,
copy `.env.example` to `.env` and edit. **Do not commit `.env`.**

The pinned snapshot must match the pinned Jellyfin version. Bumping
`JELLYFIN_VERSION` without a matching snapshot makes `up.sh` exit with an
explicit error.

## Networking

OIDC requires the issuer URL to resolve to the same Dex instance from both
the user's browser and from inside the Jellyfin container. We do this with
`dex.localtest.me` — a public DNS name that resolves to `127.0.0.1` for
everyone — plus an `extra_hosts: dex.localtest.me:host-gateway` entry on
the Jellyfin service so the hostname resolves to the host from inside the
container too.

If your machine cannot resolve `localtest.me` (very rare; usually only a
DNS adblocker would block it), add the following to `/etc/hosts`:

```
127.0.0.1 dex.localtest.me
```

## Runtime compatibility

- **Docker Desktop on macOS** — primary supported configuration.
- **Colima 0.5+** — works without modification. `host-gateway` is supported.
- **Podman** — untested. The `host-gateway` magic name may need an explicit
  IP.

## Notes on the build

Two things to be aware of for future maintainers:

**1. `SSO-Auth.csproj` strips host-provided assemblies after publish.**

Jellyfin 10.10+ loads plugins inside an isolated `PluginLoadContext`. If
the publish output bundles its own copies of `MediaBrowser.*`, `Jellyfin.*`,
`Emby.*`, or `Microsoft.Extensions.*` assemblies, the runtime loads those
in the isolated context instead of using the host's versions — producing
two distinct type identities for the same types and breaking DI
(e.g., `ILogger<SSOController>` can't be resolved).

A post-publish target (`RemoveHostProvidedAssemblies` in
`SSO-Auth/SSO-Auth.csproj`) deletes these host-shipped assemblies from
`bin/.../publish/` after each build. The production release path (JPRM
via `build.yaml`) was unaffected because JPRM only packages the three DLLs
explicitly listed in `build.yaml`'s `artifacts:`. The csproj fix makes
contributors using the documented `dotnet publish` workflow work too.

**2. The plugin bind-mount is writable, not `:ro`.**

Jellyfin's `PluginManager` writes a `meta.json` into each plugin folder at
startup. The `.publish/` bind mount cannot be `:ro` or that write fails
with an `IOException` and Jellyfin crashes during plugin load. `.publish/`
is gitignored and rewritten on every `dotnet publish`, so the meta.json
write is harmless.

## Limitations

- **Dex is not in the plugin's "tested providers" list**
  (Authelia, Authentik, Keycloak, Pocket ID, Kanidm are). Bugs specific to
  one of those won't surface here. If/when broader coverage is wanted, the
  cleanest path is a second compose profile with Keycloak.
- **No HTTPS.** Plain HTTP with the plugin's `disableHttps: true` flag.
  Real-world deployments must use HTTPS.

## Troubleshooting

**Plugin doesn't appear in Jellyfin's plugin list.**
Check `docker logs jellyfin-sso-test 2>&1 | grep -i plugin`. The most common
cause is `dotnet publish` silently failing. Verify
`test-env/.publish/SSO-Auth.dll` exists.

**OIDC login fails with "invalid issuer".**
The issuer URL embedded in Dex's discovery doc must match the URL the
plugin sees. Run from inside Jellyfin:

```
docker exec jellyfin-sso-test curl -s http://dex.localtest.me:5556/dex/.well-known/openid-configuration | jq .issuer
```

Should print `"http://dex.localtest.me:5556/dex"`. If it prints something
else or fails, see the Networking section above.

**Provisioning fails with 401.**
Snapshot may not contain the admin user. Rebuild via
`scripts/snapshot-create.sh`.

**The Jellyfin web UI shows "Add Server" instead of the login page.**
Your browser has cached client-side server connection state from a
previous Jellyfin instance on the same port. Open the URL in a private/
incognito window, or clear site data for `localhost:8096`.
