using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using OrionBe.Infrastructure.Interfaces;
using OrionBe.Infrastructure.Services.Microsoft;
using OrionBe.Router;
using OrionBe.View;
using OrionBe.View.Mods;
using OrionBe.View.Profile;
using OrionBe.ViewModel;
using OrionBe.ViewModel.Game;
using OrionBe.ViewModel.Hub;
using OrionBe.ViewModel.Mods;
using OrionBe.ViewModel.Profile;
using OrionBe.ViewModel.Settings;
using OrionBe.ViewModel.Shared;
using Umbra.Router.Core.Extensions;
using HubViewModel = OrionBe.ViewModel.Hub.HubViewModel;

namespace OrionBe.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddRouterMainWindow();

        services.AddSingleton<IMinecraftNewService, MinecraftNewService>();

        return services;
    }

    public static IServiceCollection AddRouterMainWindow(this IServiceCollection services)
    {
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<HubViewModel>();
        services.AddTransient<GameViewModel>();
        services.AddTransient<ModsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProfileViewModel>();

        services.AddUmbraRouter<Control, MainWindowViewModelBase>(static x =>
        {
            x.Register<HubView, HubViewModel>("hub");
            x.Register<GameView, GameViewModel>("game");
            x.Register<ModsView, ModsViewModel>("mods");
            x.Register<SettingsView, SettingsViewModel>("settings");
            x.Register<ProfileView, ProfileViewModel>("profile");
        });

        services.AddRouterHistory<RouterHistory<MainWindowViewModelBase>, Control, MainWindowViewModelBase>();

        return services;
    }
}
