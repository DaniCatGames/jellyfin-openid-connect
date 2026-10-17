using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SecureAuth.Migrations;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SecureAuth;

/// <summary>
///     The SSO plugin class.
/// </summary>
public class SecureAuth : BasePlugin<PluginConfiguration>, IPlugin, IHasWebPages
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SecureAuth" /> class.
    /// </summary>
    /// <param name="applicationPaths">Internal Jellyfin interface for the ApplicationPath.</param>
    /// <param name="xmlSerializer">Internal Jellyfin interface for the XML information.</param>
    /// <param name="configMigration">The legacy config migration manager</param>
    public SecureAuth(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        IConfigMigration configMigration)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        configMigration.Migrate();
    }

    /// <summary>
    ///     Gets the instance of the SSO plugin.
    /// </summary>
    public static SecureAuth Instance { get; private set; }

    /// <summary>
    ///     Returns the available internal web pages of this plugin (Admin Dashboard).
    /// </summary>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "openid-connect",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.config.html",
            },
            new PluginPageInfo
            {
                Name = "openid-connect.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.config.js",
            },
            new PluginPageInfo
            {
                Name = "openid-connect.css",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.style.css",
            },
        ];
    }

    /// <summary>
    ///     Gets the name of the plugin.
    /// </summary>
    public override string Name => "SecureAuth";

    /// <summary>
    ///     Gets the GUID of the plugin.
    /// </summary>
    public override Guid Id => Guid.Parse("3b621017-67a3-461e-a820-21622c591827");

    /// <summary>
    ///     Returns the available public user views for this plugin (Self-Service).
    /// </summary>
    public IEnumerable<PluginPageInfo> GetViews()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "OIDC/link",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.OIDC.link.html",
            },
            new PluginPageInfo
            {
                Name = "OIDC/link.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.OIDC.link.js",
            },
            new PluginPageInfo
            {
                Name = "OIDC/link.css",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.OIDC.link.css",
            },
            new PluginPageInfo
            {
                Name = "OIDC/flow.html",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.OIDC.flow.html",
            },
            new PluginPageInfo
            {
                Name = "apiClient.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.apiClient.js",
            },
            new PluginPageInfo
            {
                Name = "api.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.api.js",
            },
            new PluginPageInfo
            {
                Name = "shared.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.shared.js",
            },
            new PluginPageInfo
            {
                Name = "QR/Web",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.QR.web.html",
            },
            new PluginPageInfo
            {
                Name = "QR/Approve",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.QR.approve.html",
            },
        ];
    }
}