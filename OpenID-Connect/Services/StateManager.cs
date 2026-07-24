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
    public ConcurrentDictionary<string, TimedAuthorizeState> GetStates()
    {
        return _states;
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        foreach (KeyValuePair<string, TimedAuthorizeState> kvp in _states.Where(kvp => kvp.Value.IsExpired()))
        {
            _states.TryRemove(kvp);
        }
    }
}