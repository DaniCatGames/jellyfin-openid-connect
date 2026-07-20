using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.OpenIDConnect;

/// <summary>
///     The SSO plugin class.
/// </summary>
public class OpenIDConnect : BasePlugin<PluginConfiguration>, IPlugin, IHasWebPages
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="OpenIDConnect" /> class.
    /// </summary>
    /// <param name="applicationPaths">Internal Jellyfin interface for the ApplicationPath.</param>
    /// <param name="xmlSerializer">Internal Jellyfin interface for the XML information.</param>
    public OpenIDConnect(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    ///     Gets the instance of the SSO plugin.
    /// </summary>
    public static OpenIDConnect Instance { get; private set; }

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
    ///     Gets the name of the SSO plugin.
    /// </summary>
    public override string Name => "OpenID Connect";

    /// <summary>
    ///     Gets the GUID of the SSO plugin.
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
                Name = "link",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.linking.html",
            },
            new PluginPageInfo
            {
                Name = "linking.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Config.linking.js",
            },
            new PluginPageInfo
            {
                Name = "ApiClient.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.apiClient.js",
            },
            new PluginPageInfo
            {
                Name = "emby-restyle.css",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.emby-restyle.css",
            },
            new PluginPageInfo
            {
                Name = "jellyfin-apiClient.esm.min.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.jellyfin-apiClient.esm.min.js",
            },
            new PluginPageInfo
            {
                Name = "shared.js",
                EmbeddedResourcePath = $"{GetType().Namespace}.Views.shared.js",
            },
        ];
    }
}