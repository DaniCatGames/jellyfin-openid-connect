using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.OpenIDConnect.Services;

/// <summary>
///     Interface for the link manager.
/// </summary>
public interface ILinkManager
{
    /// <summary>
    ///     Get all links for a provider
    /// </summary>
    /// <param name="provider">The name of the provider</param>
    /// <param name="links">The links connected to the provider</param>
    /// <returns>Whether the provider exists</returns>
    bool TryGetLinks(string provider, out SerializableDictionary<string, Guid> links);

    /// <summary>
    ///     Tries to get a link for a provider and sub.
    /// </summary>
    /// <param name="provider">The name of the provider</param>
    /// <param name="sub">The sub of the user from the IdP</param>
    /// <param name="userId">The jellyfin user ID</param>
    /// <returns>Whether a link exists</returns>
    bool TryGetLink(string provider, string sub, out Guid userId);

    /// <summary>
    ///     Creates a link for a provider and sub.
    /// </summary>
    /// <param name="provider">The name of the provider</param>
    /// <param name="sub">the sub of the user from the IdP</param>
    /// <param name="userId">The jellyfin user ID</param>
    /// <returns>Whether it succeeded</returns>
    bool TryCreateLink(string provider, string sub, Guid userId);

    /// <summary>
    ///     Replaces the links for a provider with the new links.
    /// </summary>
    /// <param name="provider">The name of the provider to update</param>
    /// <param name="links">The new links</param>
    /// <returns>Whether it succeeded</returns>
    bool TryUpdateLinkConfig(string provider, SerializableDictionary<string, Guid> links);


    /// <summary>
    ///     Deletes a link for a provider.
    /// </summary>
    /// <param name="provider">The name of the provider</param>
    /// <param name="sub">The sub which has a link to delete</param>
    /// <returns>Whether it succeeded</returns>
    bool TryDeleteLink(string provider, string sub);

    /// <summary>
    ///     Get all the links that point to a jellyfin user
    /// </summary>
    /// <param name="userId">The jellyfin user ID</param>
    /// <returns>The map containing all links for the user ID</returns>
    SerializableDictionary<string, IEnumerable<string>> GetLinksByUser(Guid userId);
}