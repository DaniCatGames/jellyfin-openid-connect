using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.OpenIDConnect.Handlers;
using Jellyfin.Plugin.OpenIDConnect.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.OpenIDConnect;

/// <inheritdoc />
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IStateManager, StateManager>();
        serviceCollection.AddSingleton<ILinkManager, LinkManager>();
        serviceCollection.AddSingleton<IOidcUserManager, OidcUserManager>();
        serviceCollection.AddSingleton<IEventConsumer<UserDeletedEventArgs>, UserDeletedHandler>();
    }
}