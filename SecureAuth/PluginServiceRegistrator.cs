using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.SecureAuth.Auth;
using Jellyfin.Plugin.SecureAuth.Migrations;
using Jellyfin.Plugin.SecureAuth.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using UserDeletedHandler = Jellyfin.Plugin.SecureAuth.Handlers.UserDeletedHandler;

namespace Jellyfin.Plugin.SecureAuth;

/// <inheritdoc />
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton(typeof(IStateManager<>), typeof(StateManager<>));
        serviceCollection.AddSingleton<ILinkManager, LinkManager>();
        serviceCollection.AddSingleton<IOidcUserManager, OidcUserManager>();
        serviceCollection.AddSingleton<IEventConsumer<UserDeletedEventArgs>, UserDeletedHandler>();
        serviceCollection.AddSingleton<IAuthenticationProvider, OIDCProvider>();
        serviceCollection.AddSingleton<IConfigMigration, ConfigMigration>();
    }
}