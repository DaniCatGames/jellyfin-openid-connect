using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Duende.IdentityModel.OidcClient;

namespace Jellyfin.Plugin.OpenIDConnect.Services;

/// <summary>
///     Interface for the state manager.
/// </summary>
public interface IStateManager
{
    /// <summary>
    ///     Try to get a state by key.
    /// </summary>
    /// <param name="key">The id of the state to get</param>
    /// <param name="state">The state</param>
    /// <returns></returns>
    bool TryGetValue(string key, out TimedAuthorizeState state);

    /// <summary>
    ///     Try to add a state to the manager
    /// </summary>
    /// <param name="key">The id of the state to add</param>
    /// <param name="state">The state to add</param>
    /// <returns></returns>
    bool TryAdd(string key, TimedAuthorizeState state);

    /// <summary>
    ///     Try to remove a state from the manager
    /// </summary>
    /// <param name="key">The id of the state to remove</param>
    /// <param name="state">The state that was removed.</param>
    /// <returns></returns>
    bool TryRemove(string key, out TimedAuthorizeState state);

    /// <summary>
    ///     Check if a state is still valid.
    /// </summary>
    /// <param name="state">The state to check.</param>
    /// <returns></returns>
    bool IsValid(TimedAuthorizeState state);

    /// <summary>
    ///     Check if a state is expired.
    /// </summary>
    /// <param name="state">The state to check.</param>
    /// <returns></returns>
    bool IsExpired(TimedAuthorizeState state);

    /// <summary>
    ///     Get all currently running states and states that haven't been removed by Invalidate() yet.
    /// </summary>
    /// <returns>the states</returns>
    ConcurrentDictionary<string, TimedAuthorizeState> GetStates();

    /// <summary>
    ///     Remove all states too old.
    /// </summary>
    void Invalidate();
}

/// <summary>
///     A manager for OpenID to manage the state of the clients.
/// </summary>
public class TimedAuthorizeState
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TimedAuthorizeState" /> class.
    /// </summary>
    /// <param name="state">The AuthorizeState to time.</param>
    /// <param name="created">When this state was created.</param>
    public TimedAuthorizeState(AuthorizeState state, DateTime created)
    {
        State = state;
        Created = created;
        Valid = false;
        Admin = false;
        IsLinking = false;
        EnableLiveTv = false;
        EnableLiveTvManagement = false;
        AvatarURL = null;
    }

    /// <summary>
    ///     Gets or sets the Authorization State of the client.
    /// </summary>
    public AuthorizeState State { get; set; }

    /// <summary>
    ///     Gets or sets when this object was created to time it out.
    /// </summary>
    public DateTime Created { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the user is valid.
    /// </summary>
    public bool Valid { get; set; }

    /// <summary>
    ///     Gets or sets the user tied to the state.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the user is an administrator.
    /// </summary>
    public bool Admin { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the state is
    ///     tied to a linking flow (instead of a login flow).
    /// </summary>
    public bool IsLinking { get; set; }

    /// <summary>
    ///     Gets or sets the folders the user is allowed access to.
    /// </summary>
    public List<string> Folders { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the user is allowed to view live TV.
    /// </summary>
    public bool EnableLiveTv { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the user is allowed to manage live TV.
    /// </summary>
    public bool EnableLiveTvManagement { get; set; }

    /// <summary>
    ///     Gets or sets the user avatar url.
    /// </summary>
    public string AvatarURL { get; set; }
}