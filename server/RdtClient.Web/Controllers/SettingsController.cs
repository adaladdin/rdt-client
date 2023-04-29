﻿using System.Diagnostics;
using Aria2NET;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RdtClient.Data.Data;
using RdtClient.Data.Models.Data;
using RdtClient.Data.Models.Internal;
using RdtClient.Service.Helpers;
using RdtClient.Service.Services;
using RdtClient.Service.Services.Downloaders;

namespace RdtClient.Web.Controllers;

[Authorize(Policy = "AuthSetting")]
[Route("Api/Settings")]
public class SettingsController : Controller
{
    private readonly Settings _settings;
    private readonly Torrents _torrents;
    private readonly PlexService _plexService;

    public SettingsController(Settings settings, Torrents torrents, PlexService plexService)
    {
        _settings = settings;
        _torrents = torrents;
        _plexService = plexService;
    }

    [HttpGet]
    [Route("")]
    public ActionResult Get()
    {
        var result = SettingData.GetAll();
        return Ok(result);
    }

    [HttpPut]
    [Route("")]
    public async Task<ActionResult> Update([FromBody] IList<SettingProperty>? settings)
    {
        if (settings == null)
        {
            return BadRequest();
        }

        await _settings.Update(settings);
        
        return Ok();
    }

    [HttpGet]
    [Route("Profile")]
    public async Task<ActionResult<Profile>> Profile()
    {
        var profile = await _torrents.GetProfile();
        return Ok(profile);
    }
        
    [HttpPost]
    [Route("TestPath")]
    public async Task<ActionResult> TestPath([FromBody] SettingsControllerTestPathRequest? request)
    {
        if (request == null)
        {
            return BadRequest();
        }

        if (String.IsNullOrEmpty(request.Path))
        {
            return BadRequest("Invalid path");
        }

        var path = request.Path.TrimEnd('/').TrimEnd('\\');

        if (!Directory.Exists(path))
        {
            throw new Exception($"Path {path} does not exist");
        }

        var testFile = $"{path}/test.txt";

        await System.IO.File.WriteAllTextAsync(testFile, "RealDebridClient Test File, you can remove this file.");
            
        await FileHelper.Delete(testFile);

        return Ok();
    }
    
    [HttpPost]
    [Route("TestPlex")]
    public async Task<ActionResult> TestPlex([FromBody] SettingsControllerTestPlexRequest? request)
    {
        if (request == null)
        {
            return BadRequest();
        }

        if (String.IsNullOrEmpty(request.Token))
        {
            return BadRequest("Invalid token");
        }

        var libraries = await _plexService.TestToken(request.Token);

        if (libraries == null)
        {
            return NotFound("No libraries found");
        }

        var combinedLibraries = String.Join(", ", libraries.Select(l => l.Title));
        
        return Ok(combinedLibraries);
    }
        
    [HttpGet]
    [Route("TestDownloadSpeed")]
    public async Task<ActionResult> TestDownloadSpeed(CancellationToken cancellationToken)
    {
        var downloadPath = Settings.Get.DownloadClient.DownloadPath;

        var testFilePath = Path.Combine(downloadPath, "testDefault.rar");

        await FileHelper.Delete(testFilePath);

        var download = new Download
        {
            Link = "https://34.download.real-debrid.com/speedtest/testDefault.rar",
            Torrent = new Torrent
            {
                RdName = ""
            }
        };

        var downloadClient = new DownloadClient(download, download.Torrent, downloadPath);

        await downloadClient.Start();

        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        while (!downloadClient.Finished)
        {
            await Task.Delay(1000, CancellationToken.None);

            if (cancellationToken.IsCancellationRequested)
            {
                await downloadClient.Cancel();
            }

            if (downloadClient.Downloader is Aria2cDownloader aria2Downloader)
            {
                var aria2NetClient = new Aria2NetClient(Settings.Get.DownloadClient.Aria2cUrl, Settings.Get.DownloadClient.Aria2cSecret, httpClient, 1);

                var allDownloads = await aria2NetClient.TellAllAsync(cancellationToken);

                await aria2Downloader.Update(allDownloads);
            }
            
            if (downloadClient.BytesDone > 1024 * 1024 * 50)
            {
                await downloadClient.Cancel();

                break;
            }
        }

        await FileHelper.Delete(testFilePath);

        return Ok(downloadClient.Speed);
    }

    [HttpGet]
    [Route("TestWriteSpeed")]
    public async Task<ActionResult> TestWriteSpeed()
    {
        var downloadPath = Settings.Get.DownloadClient.DownloadPath;

        var testFilePath = Path.Combine(downloadPath, "test.tmp");

        await FileHelper.Delete(testFilePath);

        const Int32 testFileSize = 64 * 1024 * 1024;

        var watch = new Stopwatch();

        watch.Start();

        var rnd = new Random();

        await using var fileStream = new FileStream(testFilePath, FileMode.Create, FileAccess.Write, FileShare.Write);

        var buffer = new Byte[64 * 1024];

        while (fileStream.Length < testFileSize)
        {
            rnd.NextBytes(buffer);

            await fileStream.WriteAsync(buffer.AsMemory(0, buffer.Length));
        }
            
        watch.Stop();

        var writeSpeed = fileStream.Length / watch.Elapsed.TotalSeconds;
            
        fileStream.Close();

        await FileHelper.Delete(testFilePath);
        
        return Ok(writeSpeed);
    }

    [HttpPost]
    [Route("TestAria2cConnection")]
    public async Task<ActionResult<String>> TestAria2cConnection([FromBody] SettingsControllerTestAria2cConnectionRequest? request)
    {
        if (request == null)
        {
            return BadRequest();
        }

        if (String.IsNullOrEmpty(request.Url))
        {
            return BadRequest("Invalid Url");
        }

        var client = new Aria2NetClient(request.Url, request.Secret);

        var version = await client.GetVersionAsync();

        return Ok(version);
    }
}

public class SettingsControllerTestPathRequest
{
    public String? Path { get; set; }
}

public class SettingsControllerTestPlexRequest
{
    public String? Token { get; set; }
}

public class SettingsControllerTestAria2cConnectionRequest
{
    public String? Url { get; set; }
    public String? Secret { get; set; }
}