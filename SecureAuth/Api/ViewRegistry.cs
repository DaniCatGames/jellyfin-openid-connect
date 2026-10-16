#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SecureAuth.Api;

/// <summary>
///     Asset loader
/// </summary>
public sealed class AuthViewRegistry
{
    private readonly Assembly _assembly;
    private readonly Lazy<IReadOnlyDictionary<string, PluginPageInfo>> _index;
    private readonly Func<IEnumerable<PluginPageInfo>?> _source;

    private readonly ConcurrentDictionary<string, string> _textCache =
        new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuthViewRegistry" /> class.
    /// </summary>
    /// <param name="source">Delegate returning the plugin's registered views.</param>
    /// <param name="assembly">The assembly containing the embedded resources.</param>
    private AuthViewRegistry(Func<IEnumerable<PluginPageInfo>?> source, Assembly assembly)
    {
        _source = source;
        _assembly = assembly;
        _index = new Lazy<IReadOnlyDictionary<string, PluginPageInfo>>(BuildIndex);
    }

    /// <summary>The process registry</summary>
    public static AuthViewRegistry Instance { get; } = new AuthViewRegistry(
        () => SecureAuth.Instance?.GetViews(),
        typeof(SecureAuth).Assembly);

    /// <summary>
    ///     Gets a value indicating whether a view with the given name is registered.
    /// </summary>
    /// <param name="viewName">The registered view name.</param>
    /// <param name="view">The matching view registration.</param>
    public bool TryGet(string viewName, out PluginPageInfo view)
    {
        return _index.Value.TryGetValue(viewName, out view!);
    }

    /// <summary>
    ///     Opens the embedded resource stream for a registered view.
    /// </summary>
    /// <param name="viewName">The registered view name.</param>
    public Stream? OpenStream(string viewName)
    {
        return TryGet(viewName, out PluginPageInfo view)
            ? _assembly.GetManifestResourceStream(view.EmbeddedResourcePath)
            : null;
    }

    /// <summary>
    ///     Reads a text view
    /// </summary>
    /// <param name="viewName">The registered view name.</param>
    public string ReadAllText(string viewName)
    {
        return _textCache.GetOrAdd(
            viewName,
            static (name, self) =>
            {
                using Stream stream = self.OpenStream(name)
                                      ?? throw new InvalidOperationException(
                                          $"View '{name}' has no embedded resource.");
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            },
            this);
    }

    private Dictionary<string, PluginPageInfo> BuildIndex()
    {
        var index = new Dictionary<string, PluginPageInfo>(StringComparer.Ordinal);
        foreach (PluginPageInfo page in _source.Invoke() ?? Enumerable.Empty<PluginPageInfo>())
        {
            index.TryAdd(page.Name, page);
        }

        return index;
    }
}