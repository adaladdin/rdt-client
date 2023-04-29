using Microsoft.Extensions.Logging;
using SharpCompress;

namespace RdtClient.Service.Services;

public class PlexService
{
    private readonly ILogger<PlexService> _logger;
    private readonly Settings _settings;

    public PlexService(ILogger<PlexService> logger, Settings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public async Task<Boolean> TestPlex(String host, String token)
    {
        _logger.LogDebug($"Testing plex host {host} and token {token}");
        
        if (!Uri.TryCreate(host, UriKind.Absolute, out var uriResult)
            && uriResult.Scheme == Uri.UriSchemeHttp)
        {
            _logger.LogDebug($"Plex test failed. Host not a valid URI");
            return false;
        }

        var cleanedHost = host.EndsWith(@"/") ? host : host + "/";
        
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $@"{cleanedHost}?X-Plex-Token={token}");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        _logger.LogDebug($"Plex test successful. {content}");

        return true;
    }

    public async Task RefreshLibraries()
    {
        _logger.LogInformation("Refreshing plex libraries.");

        try
        {


            var host = Settings.Get.Plex.Host;
            var token = Settings.Get.Plex.Token;
            var libraries = Settings.Get.Plex.LibrariesToRefresh;

            if (host == null || token == null || libraries == null)
            {
                _logger.LogInformation("Refresh failed. host/token/libraries empty.");

                return;
            }

            if (!Uri.TryCreate(host, UriKind.Absolute, out var uriResult)
                && uriResult.Scheme == Uri.UriSchemeHttp)
            {
                _logger.LogInformation("Refresh failed. Host not a valid URI.");

                return;
            }

            var libraryIndices = new List<String>();

            libraries.Split(",")
                     .ForEach(s =>
                     {
                         var trimmed = s.Trim();

                         if (Int32.TryParse(s.Trim(), out _))
                         {
                             libraryIndices.Add(trimmed);
                         }
                     });

            if (!libraryIndices.Any())
            {
                return;
            }

            _logger.LogInformation($"{libraryIndices.Count} libraries to refresh.");

            var cleanedHost = host.EndsWith(@"/") ? host : host + "/";

            using var client = new HttpClient();

            foreach (var index in libraryIndices)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $@"{cleanedHost}library/sections/{index}/refresh?X-Plex-Token={token}");
                var response = await client.SendAsync(request);
                response.EnsureSuccessStatusCode();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error while refreshing library: {ex.Message} | {ex.StackTrace}");
        }
        finally
        {
            _logger.LogInformation($"Finished refreshing libraries.");    
        }
    }
}
