﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MonoTorrent;
using Newtonsoft.Json;
using RdtClient.Data.Data;
using RdtClient.Data.Enums;
using RdtClient.Service.Helpers;
using RdtClient.Service.Models;
using RdtClient.Service.Models.TorrentClient;
using RdtClient.Service.Services.TorrentClients;
using Torrent = RdtClient.Data.Models.Data.Torrent;

namespace RdtClient.Service.Services
{
    public class Torrents
    {
        private static readonly SemaphoreSlim RealDebridUpdateLock = new(1, 1);

        private readonly ILogger<Torrents> _logger;
        private readonly TorrentData _torrentData;
        private readonly Downloads _downloads;
        
        private readonly ITorrentClient _torrentClient;

        public Torrents(ILogger<Torrents> logger,
                        TorrentData torrentData, 
                        Downloads downloads,
                        RealDebridTorrentClient realDebridTorrentClient)
        {
            _logger = logger;
            _torrentData = torrentData;
            _downloads = downloads;
            
            _torrentClient = realDebridTorrentClient;
        }

        public async Task<IList<Torrent>> Get()
        {
            var torrents = await _torrentData.Get();

            foreach (var torrent in torrents)
            {
                foreach (var download in torrent.Downloads)
                {
                    if (TorrentRunner.ActiveDownloadClients.TryGetValue(download.DownloadId, out var downloadClient))
                    {
                        download.Speed = downloadClient.Speed;
                        download.BytesTotal = downloadClient.BytesTotal;
                        download.BytesDone = downloadClient.BytesDone;
                    }

                    if (TorrentRunner.ActiveUnpackClients.TryGetValue(download.DownloadId, out var unpackClient))
                    {
                        download.BytesTotal = unpackClient.BytesTotal;
                        download.BytesDone = unpackClient.BytesDone;
                    }
                }
            }

            return torrents;
        }

        public async Task<Torrent> GetByHash(String hash)
        {
            var torrent = await _torrentData.GetByHash(hash);

            if (torrent != null)
            {
                await UpdateRdData(torrent);
            }

            return torrent;
        }

        public async Task UpdateCategory(String hash, String category)
        {
            var torrent = await _torrentData.GetByHash(hash);

            if (torrent == null)
            {
                return;
            }

            Log($"Update category to {category}", torrent);

            await _torrentData.UpdateCategory(torrent.TorrentId, category);
        }

        public async Task<Torrent> UploadMagnet(String magnetLink,
                                                String category,
                                                TorrentDownloadAction downloadAction,
                                                TorrentFinishedAction finishedAction,
                                                Int32 downloadMinSize,
                                                String downloadManualFiles,
                                                Int32? priority)
        {
            MagnetLink magnet;

            try
            {
                magnet = MagnetLink.Parse(magnetLink);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{ex.Message}, trying to parse {magnetLink}");
                throw new Exception($"{ex.Message}, trying to parse {magnetLink}");
            }

            var id = await _torrentClient.AddMagnet(magnetLink);

            var hash = magnet.InfoHash.ToHex();

            var newTorrent = await Add(id, hash, category, downloadAction, finishedAction, downloadMinSize, downloadManualFiles, magnetLink, false, priority);

            Log($"Adding {hash} magnet link {magnetLink}", newTorrent);

            return newTorrent;
        }

        public async Task<Torrent> UploadFile(Byte[] bytes,
                                              String category,
                                              TorrentDownloadAction downloadAction,
                                              TorrentFinishedAction finishedAction,
                                              Int32 downloadMinSize,
                                              String downloadManualFiles,
                                              Int32? priority)
        {
            MonoTorrent.Torrent monoTorrent;

            var fileAsBase64 = Convert.ToBase64String(bytes);

            try
            {
                monoTorrent = await MonoTorrent.Torrent.LoadAsync(bytes);
            }
            catch (Exception ex)
            {
                throw new Exception($"{ex.Message}, trying to parse {fileAsBase64}");
            }

            var id = await _torrentClient.AddFile(bytes);

            var hash = monoTorrent.InfoHash.ToHex();

            var newTorrent = await Add(id, hash, category, downloadAction, finishedAction, downloadMinSize, downloadManualFiles, fileAsBase64, true, priority);

            Log($"Adding {hash} torrent file {fileAsBase64}", newTorrent);

            return newTorrent;
        }

        public async Task<List<TorrentClientAvailableFile>> GetAvailableFiles(String hash)
        {
            var result = await _torrentClient.GetAvailableFiles(hash);

            return result;
        }

        public async Task SelectFiles(Guid torrentId, IList<String> fileIds)
        {
            var torrent = await GetById(torrentId);

            if (torrent == null)
            {
                return;
            }

            await _torrentClient.SelectFiles(torrent.RdId, fileIds);
        }

        public async Task CheckForLinks(Guid torrentId)
        {
            var torrent = await GetById(torrentId);

            if (torrent == null)
            {
                return;
            }

            var rdTorrent = await _torrentClient.GetInfo(torrent.RdId);

            var torrentLinks = rdTorrent.Links.Where(m => !String.IsNullOrWhiteSpace(m)).ToList();

            Log($"Found {torrentLinks} links", torrent);

            // Sometimes RD will give you 1 rar with all files, sometimes it will give you 1 link per file.
            if (torrent.Files.Count(m => m.Selected) != torrentLinks.Count && 
                torrent.ManualFiles.Count != torrentLinks.Count &&
                torrentLinks.Count != 1)
            {
                return;
            }

            foreach (var file in torrentLinks)
            {
                // Make sure downloads don't get added multiple times
                var downloadExists = await _downloads.Get(torrent.TorrentId, file);

                if (downloadExists == null && !String.IsNullOrWhiteSpace(file))
                {
                    await _downloads.Add(torrent.TorrentId, file);
                }
            }
        }

        public async Task Delete(Guid torrentId, Boolean deleteData, Boolean deleteRdTorrent, Boolean deleteLocalFiles)
        {
            var torrent = await GetById(torrentId);

            if (torrent == null)
            {
                return;
            }

            Log($"Deleting", torrent);

            foreach (var download in torrent.Downloads)
            {
                while (TorrentRunner.ActiveDownloadClients.TryGetValue(download.DownloadId, out var downloadClient))
                {
                    Log($"Cancelling download", download, torrent);

                    await downloadClient.Cancel();

                    await Task.Delay(100);
                }

                while (TorrentRunner.ActiveUnpackClients.TryGetValue(download.DownloadId, out var unpackClient))
                {
                    Log($"Cancelling unpack", download, torrent);

                    unpackClient.Cancel();

                    await Task.Delay(100);
                }
            }

            if (deleteData)
            {
                Log($"Deleting RdtClient data", torrent);

                await _torrentData.UpdateComplete(torrent.TorrentId, DateTimeOffset.UtcNow);
                await _downloads.DeleteForTorrent(torrent.TorrentId);
                await _torrentData.Delete(torrentId);
            }

            if (deleteRdTorrent)
            {
                Log($"Deleting RealDebrid Torrent", torrent);

                try
                {
                    await _torrentClient.Delete(torrent.RdId);
                }
                catch
                {
                    // ignored
                }
            }

            if (deleteLocalFiles)
            {
                var downloadPath = DownloadPath(torrent);
                downloadPath = Path.Combine(downloadPath, torrent.RdName);

                Log($"Deleting local files in {downloadPath}", torrent);

                if (Directory.Exists(downloadPath))
                {
                    var retry = 0;

                    while (true)
                    {
                        try
                        {
                            Directory.Delete(downloadPath, true);

                            break;
                        }
                        catch
                        {
                            retry++;

                            if (retry >= 3)
                            {
                                throw;
                            }

                            await Task.Delay(1000);
                        }
                    }
                }
            }
        }

        public async Task<String> UnrestrictLink(Guid downloadId)
        {
            var download = await _downloads.GetById(downloadId);

            if (download == null)
            {
                throw new Exception($"Download with ID {downloadId} not found");
            }

            Log($"Unrestricting link", download, download.Torrent);

            var unrestrictedLink = await _torrentClient.Unrestrict(download.Path);

            await _downloads.UpdateUnrestrictedLink(downloadId, unrestrictedLink);

            return unrestrictedLink;
        }

        public async Task<Profile> GetProfile()
        {
            var user = await _torrentClient.GetUser();

            var profile = new Profile
            {
                UserName = user.Username,
                Expiration = user.Expiration
            };

            return profile;
        }

        public async Task UpdateRdData()
        {
            await RealDebridUpdateLock.WaitAsync();

            var torrents = await Get();

            try
            {
                var rdTorrents = await _torrentClient.GetTorrents();

                foreach (var rdTorrent in rdTorrents)
                {
                    var torrent = torrents.FirstOrDefault(m => m.RdId == rdTorrent.Id);

                    if (torrent == null)
                    {
                        continue;
                    }

                    await UpdateRdData(torrent);
                }
            }
            finally
            {
                RealDebridUpdateLock.Release();
            }
        }

        public async Task RetryTorrent(Guid torrentId, Int32 retryCount)
        {
            var torrent = await _torrentData.GetById(torrentId);

            if (torrent == null)
            {
                return;
            }

            Log($"Retrying Torrent", torrent);

            foreach (var download in torrent.Downloads)
            {
                while (TorrentRunner.ActiveDownloadClients.TryGetValue(download.DownloadId, out var downloadClient))
                {
                    await downloadClient.Cancel();

                    await Task.Delay(100);
                }

                while (TorrentRunner.ActiveUnpackClients.TryGetValue(download.DownloadId, out var unpackClient))
                {
                    unpackClient.Cancel();

                    await Task.Delay(100);
                }
            }

            await Delete(torrentId, true, true, true);

            if (String.IsNullOrWhiteSpace(torrent.FileOrMagnet))
            {
                throw new Exception($"Cannot re-add this torrent, original magnet or file not found");
            }

            Torrent newTorrent;

            if (torrent.IsFile)
            {
                var bytes = Convert.FromBase64String(torrent.FileOrMagnet);

                newTorrent = await UploadFile(bytes, torrent.Category, torrent.DownloadAction, torrent.FinishedAction, torrent.DownloadMinSize, torrent.DownloadManualFiles, torrent.Priority);
            }
            else
            {
                newTorrent = await UploadMagnet(torrent.FileOrMagnet,
                                                torrent.Category,
                                                torrent.DownloadAction,
                                                torrent.FinishedAction,
                                                torrent.DownloadMinSize,
                                                torrent.DownloadManualFiles,
                                                torrent.Priority);
            }

            await _torrentData.UpdateRetryCount(newTorrent.TorrentId, retryCount);
        }

        public async Task RetryDownload(Guid downloadId)
        {
            var download = await _downloads.GetById(downloadId);

            if (download == null)
            {
                return;
            }

            Log($"Retrying Download", download, download.Torrent);

            while (TorrentRunner.ActiveDownloadClients.TryGetValue(download.DownloadId, out var downloadClient))
            {
                await downloadClient.Cancel();

                await Task.Delay(100);
            }

            while (TorrentRunner.ActiveUnpackClients.TryGetValue(download.DownloadId, out var unpackClient))
            {
                unpackClient.Cancel();

                await Task.Delay(100);
            }

            var downloadPath = DownloadPath(download.Torrent);
            
            var filePath = DownloadHelper.GetDownloadPath(downloadPath, download.Torrent, download);

            Log($"Deleting {filePath}", download, download.Torrent);
            
            await FileHelper.Delete(filePath);
            
            await _torrentData.UpdateComplete(download.TorrentId, null);

            Log($"Resetting", download, download.Torrent);

            await _downloads.Reset(downloadId);
        }
        
        public async Task UpdateComplete(Guid torrentId, DateTimeOffset datetime)
        {
            await _torrentData.UpdateComplete(torrentId, datetime);
        }

        public async Task UpdateFilesSelected(Guid torrentId, DateTimeOffset datetime)
        {
            await _torrentData.UpdateFilesSelected(torrentId, datetime);
        }

        public async Task UpdatePriority(String hash, Int32 priority)
        {
            var torrent = await _torrentData.GetByHash(hash);

            if (torrent == null)
            {
                return;
            }

            await _torrentData.UpdatePriority(torrent.TorrentId, priority);
        }

        public async Task<Torrent> GetById(Guid torrentId)
        {
            var torrent = await _torrentData.GetById(torrentId);

            if (torrent == null)
            {
                return null;
            }

            await UpdateRdData(torrent);

            foreach (var download in torrent.Downloads)
            {
                if (TorrentRunner.ActiveDownloadClients.TryGetValue(download.DownloadId, out var downloadClient))
                {
                    download.Speed = downloadClient.Speed;
                    download.BytesTotal = downloadClient.BytesTotal;
                    download.BytesDone = downloadClient.BytesDone;
                }

                if (TorrentRunner.ActiveUnpackClients.TryGetValue(download.DownloadId, out var unpackClient))
                {
                    download.BytesTotal = unpackClient.BytesTotal;
                    download.BytesDone = unpackClient.BytesDone;
                }
            }

            return torrent;
        }

        private static String DownloadPath(Torrent torrent)
        {
            var settingDownloadPath = Settings.Get.DownloadPath;

            if (!String.IsNullOrWhiteSpace(torrent.Category))
            {
                settingDownloadPath = Path.Combine(settingDownloadPath, torrent.Category);
            }

            return settingDownloadPath;
        }

        private async Task<Torrent> Add(String rdTorrentId,
                                        String infoHash,
                                        String category,
                                        TorrentDownloadAction downloadAction,
                                        TorrentFinishedAction finishedAction,
                                        Int32 downloadMinSize,
                                        String downloadManualFiles,
                                        String fileOrMagnetContents,
                                        Boolean isFile,
                                        Int32? priority)
        {
            await RealDebridUpdateLock.WaitAsync();
            
            try
            {
                var torrent = await _torrentData.GetByHash(infoHash);

                if (torrent != null)
                {
                    return torrent;
                }

                var newTorrent = await _torrentData.Add(rdTorrentId,
                                                        infoHash,
                                                        category,
                                                        downloadAction,
                                                        finishedAction,
                                                        downloadMinSize,
                                                        downloadManualFiles,
                                                        fileOrMagnetContents,
                                                        isFile,
                                                        priority);

                await UpdateRdData(newTorrent);

                return newTorrent;
            }
            finally
            {
                RealDebridUpdateLock.Release();
            }
        }
        
        public async Task Update(Torrent torrent)
        {
            await _torrentData.Update(torrent);
        }

        private async Task UpdateRdData(Torrent torrent)
        {
            var originalTorrent = JsonConvert.SerializeObject(torrent,
                                                              new JsonSerializerSettings
                                                              {
                                                                  ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                                                              });

            try
            {
                var rdTorrent = await _torrentClient.GetInfo(torrent.RdId);

                if (!String.IsNullOrWhiteSpace(rdTorrent.Filename))
                {
                    torrent.RdName = rdTorrent.Filename;
                }

                if (!String.IsNullOrWhiteSpace(rdTorrent.OriginalFilename))
                {
                    torrent.RdName = rdTorrent.OriginalFilename;
                }

                if (rdTorrent.Bytes > 0)
                {
                    torrent.RdSize = rdTorrent.Bytes;
                }
                else if (rdTorrent.OriginalBytes > 0)
                {
                    torrent.RdSize = rdTorrent.OriginalBytes;
                }

                if (rdTorrent.Files != null)
                {
                    torrent.RdFiles = JsonConvert.SerializeObject(rdTorrent.Files);
                }

                torrent.RdHost = rdTorrent.Host;
                torrent.RdSplit = rdTorrent.Split;
                torrent.RdProgress = rdTorrent.Progress;
                torrent.RdAdded = rdTorrent.Added;
                torrent.RdEnded = rdTorrent.Ended;
                torrent.RdSpeed = rdTorrent.Speed;
                torrent.RdSeeders = rdTorrent.Seeders;
                torrent.RdStatusRaw = rdTorrent.Status;

                torrent.RdStatus = rdTorrent.Status switch
                {
                    "magnet_error" => RealDebridStatus.Error,
                    "magnet_conversion" => RealDebridStatus.Processing,
                    "waiting_files_selection" => RealDebridStatus.WaitingForFileSelection,
                    "queued" => RealDebridStatus.Downloading,
                    "downloading" => RealDebridStatus.Downloading,
                    "downloaded" => RealDebridStatus.Finished,
                    "error" => RealDebridStatus.Error,
                    "virus" => RealDebridStatus.Error,
                    "compressing" => RealDebridStatus.Downloading,
                    "uploading" => RealDebridStatus.Downloading,
                    "dead" => RealDebridStatus.Error,
                    _ => RealDebridStatus.Error
                };
            }
            catch (Exception ex)
            {
                if (ex.Message == "Resource not found")
                {
                    torrent.RdStatusRaw = "deleted";
                }
                else
                {
                    throw;
                }
            }
            
            var newTorrent = JsonConvert.SerializeObject(torrent,
                                                         new JsonSerializerSettings
                                                         {
                                                             ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                                                         });

            if (originalTorrent != newTorrent)
            {
                await _torrentData.UpdateRdData(torrent);
            }
        }

        private void Log(String message, Data.Models.Data.Download download, Torrent torrent)
        {
            if (download != null)
            {
                message = $"{message} {download.ToLog()}";
            }

            if (torrent != null)
            {
                message = $"{message} {torrent.ToLog()}";
            }

            _logger.LogDebug(message);
        }
        
        private void Log(String message, Torrent torrent = null)
        {
            if (torrent != null)
            {
                message = $"{message} {torrent.ToLog()}";
            }

            _logger.LogDebug(message);
        }
    }
}
