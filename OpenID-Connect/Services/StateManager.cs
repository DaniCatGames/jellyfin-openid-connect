using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.OpenIDConnect.Services;

/// <inheritdoc />
public class StateManager : IStateManager
{
    private readonly ConcurrentDictionary<string, TimedAuthorizeState> _states =
        new ConcurrentDictionary<string, TimedAuthorizeState>();

    /// <inheritdoc />
    public bool TryGetValue(string key, out TimedAuthorizeState state)
    {
        return _states.TryGetValue(key, out state);
    }

    /// <inheritdoc />
    public bool TryAdd(string key, TimedAuthorizeState state)
    {
        return _states.TryAdd(key, state);
    }

    /// <inheritdoc />
    public bool TryRemove(string key, out TimedAuthorizeState state)
    {
        return _states.TryRemove(key, out state);
    }

    /// <inheritdoc />
    public bool IsValid(TimedAuthorizeState state)
    {
        return state.Valid && !IsExpired(state);
    }

    /// <inheritdoc />
    public bool IsExpired(TimedAuthorizeState state)
    {
        return state.Created < DateTime.UtcNow.AddMinutes(-1);
    }

    /// <inheritdoc />
    public ConcurrentDictionary<string, TimedAuthorizeState> GetStates()
    {
        return _states;
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        DateTime cutoff = DateTime.UtcNow.AddMinutes(-1);

        foreach (KeyValuePair<string, TimedAuthorizeState> kvp in _states.Where(kvp => kvp.Value.Created < cutoff))
        {
            _states.TryRemove(kvp);
        }
    }
}