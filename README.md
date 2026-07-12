<h1 align="center">Jellyfin OpenIDConnect</h1>

<p align="center">

<img alt="Logo" src="https://raw.githubusercontent.com/DaniCatGames/jellyfin-openid-connect/main/img/logo.png"/>
<br/>
<br/>
<a href="https://github.com/DaniCatGames/jellyfin-openid-connect">
    <img alt="GPL 3.0 License" src="https://img.shields.io/github/license/DaniCatGames/jellyfin-openid-connect.svg"/>
</a>
<a href="https://github.com/DaniCatGames/jellyfin-openid-connect/actions/workflows/publish-unstable.yml">
    <img alt="Unstable Build Status" src="https://github.com/DaniCatGames/jellyfin-openid-connect/actions/workflows/publish-unstable.yml/badge.svg"/>
</a>
<a href="https://github.com/DaniCatGames/jellyfin-openid-connect/releases">
    <img alt="Current Release" src="https://img.shields.io/github/release/DaniCatGames/jellyfin-openid-connect.svg"/>
</a>

This plugin allows users to sign in through an OpenIDConnect provider (such as Google, Microsoft, or your own provider). This enables one-click signin.

https://user-images.githubusercontent.com/17993169/149681516-f93b43f5-fa5c-4c1f-a909-e5414878a864.mp4

Existing users may link new SSO accounts, or remove existing links using self-service at `/OpenIDConnectViews/link`.

## Current State

This is 100% alpha software! PRs are welcome to improve the code.

**This is for Jellyfin >=12.0 and only on the Web UI or clients supporting [Quick Connect](https://jellyfin.org/docs/general/server/quick-connect)**

## Tested Providers

[Find provider specific documentation in docs/providers.md](docs/providers.md)

- Authelia
- authentik
- Keycloak
  - OIDC
- Pocket ID
- Kanidm
- Google OpenID: Works, but usernames are all numeric

While the above providers are apprently working, I personally only use and test with Authentik

## Installing

### Stable (recommended)

Add the stable package repository to your Jellyfin plugin repositories (**Dashboard → Plugins → Repositories → +**):

```
https://raw.githubusercontent.com/DaniCatGames/jellyfin-openid-connect/manifest-stable/manifest.json
```

Then install **OpenID Connect** from the plugin catalog.

### Unstable builds

If you're impatient/brave/feel like helping us test things out, you can opt into unstable builds, which are built automatically from every change on the `main` branch and versioned `N.YYMM.run.0`.

Add the unstable repository instead of (or alongside) the stable one:

```
https://raw.githubusercontent.com/DaniCatGames/jellyfin-openid-connect/manifest-unstable/manifest.json
```

Unstable builds may have new features unavailable in stable, but **be warned**: things change frequently, may break, and you could lose data. They are not intended for production use.

### Branch builds

Builds for individual feature branches are uploaded as artifacts on each branch's GitHub Actions run (named `openid-connect-<branch>-<sha>.zip`) and must be installed manually. They are not published to any repository.

See [Building & Releasing](docs/building.md) for instructions on how to build from source.

## Limitations

Logging in with an OIDC account that has the same username as an existing Jellyfin account will override the permissions for the user. Use caution when overriding the administrator account!

~~There is no GUI to sign in. You have to make it yourself! The buttons should redirect to something like this: [https://myjellyfin.example.com/sso/OID/start/clientid](https://myjellyfin.example.com/sso/OID/start/clientid) replacing `clientid` with the provider client ID.~~

There is also no logout callback. Logging out of Jellyfin will log you out of Jellyfin only, instead of the SSO provider as well.

# Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

For building from source and releasing, see [docs/building.md](docs/building.md).

# Credits and Thanks

Credit to [9p4's jellyfin-plugin-sso](https://github.com/9p4/jellyfin-plugin-sso) and [eddymoulton's fork](https://github.com/eddymoulton/jellyfin-plugin-oidc) for forming a solid base to build this off.

I've taken the fork to continue maintaining for my own use, along with removing excess functionality that I did not want to maintain.

This is now intended to be a minimal implementation for OIDC only.

## Transitive thanks

Credit to those who helped make jellyfin-plugin-sso possible too.

> Much thanks to the [Jellyfin LDAP plugin](https://github.com/jellyfin/jellyfin-plugin-ldapauth) for offering a base for me to start on my plugin.
>
> I use the [Duende IdentityModel OIDC Client](https://github.com/DuendeSoftware/foss) library for the OpenID side of things.
>
> Thanks to these projects, without which I would have been pulling my hair out implementing these protocols from scratch.
