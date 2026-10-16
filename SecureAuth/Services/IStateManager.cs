using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Duende.IdentityModel.OidcClient;

namespace Jellyfin.Plugin.SecureAuth.Services;

/// <summary>
///     Interface for the state manager.
/// </summary>
public interface IStateManager<TState> where TState : TimedFlowState
{
    /// <summary>
    ///     Try to get a state by key.
    /// </summary>
    /// <param name="key">The id of the state to get</param>
    /// <param name="state">The state</param>
    bool TryGetValue(string key, out TState state);

    /// <summary>
    ///     Try to add a state to the manager
    /// </summary>
    /// <param name="key">The id of the state to add</param>
    /// <param name="state">The state to add</param>
    bool TryAdd(string key, TState state);

    /// <summary>
    ///     Try to remove a state from the manager
    /// </summary>
    /// <param name="key">The id of the state to remove</param>
    /// <param name="state">The state that was removed.</param>
    bool TryRemove(string key, out TState state);

    /// <summary>
    ///     Get all currently running states and states that haven't been removed by Invalidate() yet.
    /// </summary>
    ConcurrentDictionary<string, TState> GetStates();

    /// <summary>
    ///     Remove all states too old.
    /// </summary>
    void Invalidate();
}

/// <summary>
///     A state connected to an active flow
/// </summary>
/// <param name="created">When the flow started</param>
public abstract class TimedFlowState(DateTime created)
{
    /// <summary>
    ///     When the flow was initiated
    /// </summary>
    private DateTime Created { get; } = created;

    /// <summary>
    ///     Check if the flow is expired.
    /// </summary>
    /// <param name="lifetime">How long the flow should be valid for</param>
    public bool IsExpired(TimeSpan? lifetime = null)
    {
        return Created < DateTime.UtcNow - (lifetime ?? TimeSpan.FromMinutes(5));
    }

    /// <summary>
    ///     Check if the flow is still valid.
    /// </summary>
    public virtual bool IsValid()
    {
        return !IsExpired();
    }
}

/// <summary>
///     The state of a QR code flow.
/// </summary>
public class TimedQrState(DateTime created) : TimedFlowState(created)
{
    /// <summary>
    ///     The id of the device that scanned the QR code.
    /// </summary>
    public string DeviceId { get; set; }

    /// <summary>
    ///     The name of the device that scanned the QR code.
    /// </summary>
    public string DeviceName { get; set; }

    /// <summary>
    ///     The name of the app that scanned the QR code.
    /// </summary>
    public string AppName { get; set; }

    /// <summary>
    ///     The version of the app that scanned the QR code.
    /// </summary>
    public string AppVersion { get; set; }

    /// <summary>
    ///     6 digit code shown to both devices.
    /// </summary>
    public string Fingerprint { get; set; }

    /// <summary>
    ///     What the current state of the QR flow is.
    /// </summary>
    public QrCodeState State { get; set; } = QrCodeState.Pending;
}

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public enum QrCodeState
{
    Pending,
    Approved,
    Denied,
    Expired,
}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

/// <summary>
///     A manager for OpenID to manage the state of the clients.
/// </summary>
public class TimedOidcState(AuthorizeState state, DateTime created) : TimedFlowState(created)
{
    /// <summary>
    ///     Gets or sets the Authorization State of the client.
    /// </summary>
    public AuthorizeState State { get; } = state;

    /// <summary>
    ///     Gets or sets a value indicating whether the user is valid.
    /// </summary>
    public bool Valid { get; set; }

    /// <summary>
    ///     Gets or sets the user tied to the state.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    ///     Gets or sets the sub of the user. This is a unique identifier for the user from the IdP.
    /// </summary>
    public string Sub { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the user is an administrator.
    /// </summary>
    public bool Admin { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the state is
    ///     tied to a linking flow (instead of a login flow).
    /// </summary>
    public bool IsLinking { get; init; }

    /// <summary>
    ///     Gets or sets a value indicating whether the state is
    ///     tied to a testing flow (instead of a login flow).
    /// </summary>
    public bool IsTesting { get; init; }

    /// <summary>
    ///     Provider that started the flow.
    /// </summary>
    public string Provider { get; init; }

    /// <summary>
    ///     Cookie to bind to the browser.
    /// </summary>
    public string Cookie { get; set; }

    /// <summary>
    ///     Gets or sets the folders the user is allowed access to.
    /// </summary>
    public List<string> DefaultAllowedFolders { get; set; }

    /// <summary>
    ///     Gets or sets the folders the user is allowed access to.
    /// </summary>
    public List<string> RbacFolders { get; set; }

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

    /// <summary>
    ///     Check if a state is still valid.
    /// </summary>
    public bool IsValid(string provider, string flowCookie)
    {
        return Valid && !IsExpired() && Provider == provider && Cookie == flowCookie;
    }
}