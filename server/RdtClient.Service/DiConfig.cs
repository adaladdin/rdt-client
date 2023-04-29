using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Plex.Api.Factories;
using Plex.Library.Factories;
using Plex.ServerApi;
using Plex.ServerApi.Api;
using Plex.ServerApi.Clients;
using Plex.ServerApi.Clients.Interfaces;
using RdtClient.Service.BackgroundServices;
using RdtClient.Service.Middleware;
using RdtClient.Service.Services;
using RdtClient.Service.Services.TorrentClients;

namespace RdtClient.Service;

public static class DiConfig
{
    public static void Config(IServiceCollection services)
    {
        services.AddScoped<AllDebridTorrentClient>();
        services.AddScoped<Authentication>();
        services.AddScoped<Downloads>();
        services.AddScoped<PremiumizeTorrentClient>();
        services.AddScoped<QBittorrent>();
        services.AddScoped<RemoteService>();
        services.AddScoped<RealDebridTorrentClient>();
        services.AddScoped<Settings>();
        services.AddScoped<Torrents>();
        services.AddScoped<TorrentRunner>();
        
        // Create Client Options
        var apiOptions = new ClientOptions
        {
            Product = "API_RDTClient",
            DeviceName = "API_RDTClient",
            ClientId = "rdtclientcustomid",
            Platform = "Web",
            Version = "v1"
        };

        // Setup Dependency Injection
        services.AddSingleton(apiOptions);
        services.AddTransient<IPlexServerClient, PlexServerClient>();
        services.AddTransient<IPlexAccountClient, PlexAccountClient>();
        services.AddTransient<IPlexLibraryClient, PlexLibraryClient>();
        services.AddTransient<IApiService, ApiService>();
        services.AddTransient<IPlexFactory, PlexFactory>();
        services.AddTransient<IPlexRequestsHttpClient, PlexRequestsHttpClient>();
        services.AddScoped<PlexService>();

        services.AddSingleton<IAuthorizationHandler, AuthSettingHandler>();
            
        services.AddHostedService<ProviderUpdater>();
        services.AddHostedService<Startup>();
        services.AddHostedService<TaskRunner>();
        services.AddHostedService<UpdateChecker>();
        services.AddHostedService<WatchFolderChecker>();
    }
}