using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.OpenIDConnect.Services;

/// <inheritdoc />
public class LinkManager : ILinkManager
{
    /// <inheritdoc />
    public bool TryGetLinks(string provider, out SerializableDictionary<string, Guid> links)
    {
        if (!OpenIDConnect.Instance.Configuration.Configs.TryGetValue(provider, out Config config))
        {
            links = new SerializableDictionary<string, Guid>();
            return false;
        }

        links = config.Links;
        return true;
    }

    /// <inheritdoc />
    public bool TryGetLink(string provider, string sub, out Guid userId)
    {
        if (!TryGetLinks(provider, out SerializableDictionary<string, Guid> links)
            || !links.TryGetValue(sub, out userId))
        {
            userId = Guid.Empty;
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryCreateLink(string provider, string sub, Guid userId)
    {
        if (!TryGetLinks(provider, out SerializableDictionary<string, Guid> links))
        {
            return false;
        }

        links[sub] = userId;

        return TryUpdateLinkConfig(provider, links);
    }

    /// <inheritdoc />
    public bool TryUpdateLinkConfig(string provider, SerializableDictionary<string, Guid> links)
    {
        PluginConfiguration config = OpenIDConnect.Instance.Configuration;
        if (!config.Configs.TryGetValue(provider, out _))
        {
            return false;
        }

        config.Configs[provider].Links = links;
        OpenIDConnect.Instance.UpdateConfiguration(config);
        return true;
    }

    /// <inheritdoc />
    public bool TryDeleteLink(string provider, string sub)
    {
        if (!TryGetLinks(provider, out SerializableDictionary<string, Guid> links))
        {
            return false;
        }

        links.Remove(sub);

        return TryUpdateLinkConfig(provider, links);
    }

    /// <inheritdoc />
    public SerializableDictionary<string, IEnumerable<string>> GetLinksByUser(Guid userId)
    {
        var mappings = new SerializableDictionary<string, IEnumerable<string>>();
        SerializableDictionary<string, Config> providerList = OpenIDConnect.Instance.Configuration.Configs;

        foreach (string providerName in providerList.Keys)
        {
            SerializableDictionary<string, Guid> links = providerList[providerName].Links;
            IEnumerable<string> keys = links
                .Where(link => link.Value == userId)
                .Select(link => link.Key);
            mappings[providerName] = keys;
        }

        return mappings;
    }

    /// <inheritdoc />
    public void DeleteLinksToUser(Guid userId)
    {
        SerializableDictionary<string, IEnumerable<string>> links = GetLinksByUser(userId);

        foreach (KeyValuePair<string, IEnumerable<string>> providerMap in links)
        {
            foreach (string sub in providerMap.Value)
            {
                TryDeleteLink(providerMap.Key, sub);
            }
        }
    }
}