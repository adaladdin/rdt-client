using Microsoft.Extensions.Logging;
using Plex.Api.Factories;
using Plex.Library.ApiModels.Libraries;

namespace RdtClient.Service.Services;

public class PlexService
{
    private readonly ILogger<PlexService> _logger;
    private readonly IPlexFactory _plexFactory;
    private readonly Settings _settings;

    public PlexService(ILogger<PlexService> logger, Settings settings, IPlexFactory plexFactory)
    {
        _logger = logger;
        _plexFactory = plexFactory;
        _settings = settings;
    }

    public async Task RefreshLibraries()
    {
        var token = Settings.Get.Plex.Token;

        if (token == null)
        {
            _logger.LogDebug($"Plex Token {token} invalid.");
            return;
        }
        
        var account = _plexFactory.GetPlexAccount(token);
        
        var servers = await account.Servers();
        _logger.LogDebug($"Found {servers.Count} servers for token {token}");
        
        servers.ForEach(async s =>
        {
            (await s.Libraries()).ForEach(l =>
            {
                _logger.LogDebug($"Found {l.Title} library");                
            });
        });
    }

    public async Task<List<LibraryBase>?> TestToken(String token)
    {
        return await GetAllLibraries(token);
    }

    private async Task<List<LibraryBase>?> GetAllLibraries(String token)
    {
        var account = _plexFactory.GetPlexAccount(token);
        var servers = await account.Servers();

        if (servers.Any())
        {
            return null;
        }

        var libraries = new List<LibraryBase>();
        
        foreach (var server in servers)
        {
            libraries.AddRange(await server.Libraries());
        }

        return libraries;
    }
}
