using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.SecureAuth.Services;

/// <inheritdoc />
public class StateManager<TState> : IStateManager<TState> where TState : TimedFlowState
{
    private readonly ConcurrentDictionary<string, TState> _states =
        new ConcurrentDictionary<string, TState>();

    /// <inheritdoc />
    public bool TryGetValue(string key, out TState state)
    {
        return _states.TryGetValue(key, out state);
    }

    /// <inheritdoc />
    public bool TryAdd(string key, TState state)
    {
        return _states.TryAdd(key, state);
    }

    /// <inheritdoc />
    public bool TryRemove(string key, out TState state)
    {
        return _states.TryRemove(key, out state);
    }

    /// <inheritdoc />
    public ConcurrentDictionary<string, TState> GetStates()
    {
        return _states;
    }

    /// <inheritdoc />
    public void Invalidate()
    {
        foreach (KeyValuePair<string, TState> kvp in _states.Where(kvp => kvp.Value.IsExpired()))
        {
            _states.TryRemove(kvp);
        }
    }
}