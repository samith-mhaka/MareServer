﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MareSynchronos.API;
using MareSynchronosServer.Authentication;
using MareSynchronosServer.Metrics;
using MareSynchronosServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MareSynchronosServer.Hubs
{
    public partial class MareHub
    {
        private string BasePath => _configuration["CacheDirectory"];

        [Authorize(AuthenticationSchemes = SecretKeyAuthenticationHandler.AuthScheme)]
        [HubMethodName(Api.SendFileAbortUpload)]
        public async Task AbortUpload()
        {
            _logger.LogInformation("User {AuthenticatedUserId} aborted upload", AuthenticatedUserId);
            var userId = AuthenticatedUserId;
            var notUploadedFiles = _dbContext.Files.Where(f => !f.Uploaded && f.Uploader.UID == userId).ToList();
            _dbContext.RemoveRange(notUploadedFiles);
            await _dbContext.SaveChangesAsync().ConfigureAwait(false);
        }

        [Authorize(AuthenticationSchemes = SecretKeyAuthenticationHandler.AuthScheme)]
        [HubMethodName(Api.SendFileDeleteAllFiles)]
        public async Task DeleteAllFiles()
        {
            _logger.LogInformation("User {AuthenticatedUserId} deleted all their files", AuthenticatedUserId);

            var ownFiles = await _dbContext.Files.Where(f => f.Uploaded && f.Uploader.UID == AuthenticatedUserId).ToListAsync().ConfigureAwait(false);
            foreach (var file in ownFiles)
            {
                var fi = new FileInfo(Path.Combine(BasePath, file.Hash));
                if (fi.Exists)
                {
                    MareMetrics.FilesTotalSize.Dec(fi.Length);
                    MareMetrics.FilesTotal.Dec();
                    fi.Delete();
                }
            }
            _dbContext.Files.RemoveRange(ownFiles);
            await _dbContext.SaveChangesAsync().ConfigureAwait(false);
        }

        [Authorize(AuthenticationSchemes = SecretKeyAuthenticationHandler.AuthScheme)]
        [HubMethodName(Api.InvokeGetFilesSizes)]
        public async Task<List<DownloadFileDto>> GetFilesSizes(List<string> hashes)
        {
            var allFiles = await _dbContext.Files.Where(f => hashes.Contains(f.Hash)).ToListAsync().ConfigureAwait(false);
            var forbiddenFiles = await _dbContext.ForbiddenUploadEntries.
                Where(f => hashes.Contains(f.Hash)).ToListAsync().ConfigureAwait(false);
            List<DownloadFileDto> response = new();
            foreach (var hash in hashes)
            {
                var fileInfo = new FileInfo(Path.Combine(BasePath, hash));
                long fileSize = 0;
                try
                {
                    fileSize = fileInfo.Length;
                }
                catch
                {
                    // file doesn't exist anymore
                }

                var forbiddenFile = forbiddenFiles.SingleOrDefault(f => f.Hash == hash);
                var downloadFile = allFiles.SingleOrDefault(f => f.Hash == hash);

                response.Add(new DownloadFileDto
                {
                    FileExists = fileInfo.Exists,
                    ForbiddenBy = forbiddenFile?.ForbiddenBy ?? string.Empty,
                    IsForbidden = forbiddenFile != null,
                    Hash = hash,
                    Size = fileSize,
                    Url = _configuration["CdnFullUrl"] + hash.ToUpperInvariant()
                });

                if (!fileInfo.Exists && downloadFile != null)
                {
                    _dbContext.Files.Remove(downloadFile);
                    await _dbContext.SaveChangesAsync().ConfigureAwait(false);
                }
            }

            return response;
        }

        [Authorize(AuthenticationSchemes = SecretKeyAuthenticationHandler.AuthScheme)]
        [HubMethodName(Api.InvokeFileIsUploadFinished)]
        public async Task<bool> IsUploadFinished()
        {
            var userUid = AuthenticatedUserId;
            return await _dbContext.Files.AsNoTracking()
                .AnyAsync(f => f.Uploader.UID == userUid && !f.Uploaded).ConfigureAwait(false);
        }

        [Authorize(AuthenticationSchemes = SecretKeyAuthenticationHandler.AuthScheme)]
        [HubMethodName(Api.InvokeFileSendFiles)]
        public async Task<List<UploadFileDto>> SendFiles(List<string> fileListHashes)
        {
            var userSentHashes = new HashSet<string>(fileListHashes.Distinct());
            _logger.LogInformation("User {AuthenticatedUserId} sending files: {count}", AuthenticatedUserId, userSentHashes.Count);
            var notCoveredFiles = new Dictionary<string, UploadFileDto>();
            var forbiddenFiles = await _dbContext.ForbiddenUploadEntries.AsNoTracking().Where(f => userSentHashes.Contains(f.Hash)).ToDictionaryAsync(f => f.Hash, f => f).ConfigureAwait(false);
            var existingFiles = await _dbContext.Files.AsNoTracking().Where(f => userSentHashes.Contains(f.Hash)).ToDictionaryAsync(f => f.Hash, f => f).ConfigureAwait(false);
            var uploader = await _dbContext.Users.SingleAsync(u => u.UID == AuthenticatedUserId).ConfigureAwait(false);

            List<FileCache> fileCachesToUpload = new();
            foreach (var file in userSentHashes)
            {
                // Skip empty file hashes, duplicate file hashes, forbidden file hashes and existing file hashes
                if (string.IsNullOrEmpty(file)) { continue; }
                if (notCoveredFiles.ContainsKey(file)) { continue; }
                if (forbiddenFiles.ContainsKey(file))
                {
                    notCoveredFiles[file] = new UploadFileDto()
                    {
                        ForbiddenBy = forbiddenFiles[file].ForbiddenBy,
                        Hash = file,
                        IsForbidden = true
                    };

                    continue;
                }
                if (existingFiles.ContainsKey(file)) { continue; }

                _logger.LogInformation("User {AuthenticatedUserId}  needs upload: {file}", AuthenticatedUserId, file);
                var userId = AuthenticatedUserId;
                fileCachesToUpload.Add(new FileCache()
                {
                    Hash = file,
                    Uploaded = false,
                    Uploader = uploader
                });

                notCoveredFiles[file] = new UploadFileDto()
                {
                    Hash = file,
                };
            }
            //Save bulk
            await _dbContext.Files.AddRangeAsync(fileCachesToUpload).ConfigureAwait(false);
            await _dbContext.SaveChangesAsync().ConfigureAwait(false);
            return notCoveredFiles.Values.ToList();
        }

        [Authorize(AuthenticationSchemes = SecretKeyAuthenticationHandler.AuthScheme)]
        [HubMethodName(Api.SendFileUploadFileStreamAsync)]
        public async Task UploadFileStreamAsync(string hash, IAsyncEnumerable<byte[]> fileContent)
        {
            _logger.LogInformation("User {AuthenticatedUserId} uploading file: {hash}", AuthenticatedUserId, hash);

            var relatedFile = _dbContext.Files.SingleOrDefault(f => f.Hash == hash && f.Uploader.UID == AuthenticatedUserId && f.Uploaded == false);
            if (relatedFile == null) return;
            var forbiddenFile = _dbContext.ForbiddenUploadEntries.SingleOrDefault(f => f.Hash == hash);
            if (forbiddenFile != null) return;
            var finalFileName = Path.Combine(BasePath, hash);
            var tempFileName = finalFileName + ".tmp";
            using var fileStream = new FileStream(tempFileName, FileMode.OpenOrCreate);
            long length = 0;
            try
            {
                await foreach (var chunk in fileContent.ConfigureAwait(false))
                {
                    length += chunk.Length;
                    await fileStream.WriteAsync(chunk).ConfigureAwait(false);
                }

                await fileStream.FlushAsync().ConfigureAwait(false);
                await fileStream.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await fileStream.FlushAsync().ConfigureAwait(false);
                    await fileStream.DisposeAsync().ConfigureAwait(false);
                    _dbContext.Files.Remove(relatedFile);
                    await _dbContext.SaveChangesAsync().ConfigureAwait(false);
                }
                catch
                {
                    // already removed
                }
                finally
                {
                    File.Delete(tempFileName);
                }

                return;
            }

            _logger.LogInformation("User {AuthenticatedUserId} upload finished: {hash}, size: {length}", AuthenticatedUserId, hash, length);

            try
            {
                var decodedFile = LZ4.LZ4Codec.Unwrap(await File.ReadAllBytesAsync(tempFileName).ConfigureAwait(false));
                using var sha1 = SHA1.Create();
                using var ms = new MemoryStream(decodedFile);
                var computedHash = await sha1.ComputeHashAsync(ms).ConfigureAwait(false);
                var computedHashString = BitConverter.ToString(computedHash).Replace("-", "");
                if (hash != computedHashString)
                {
                    _logger.LogWarning("Computed file hash was not expected file hash. Computed: {computedHashString}, Expected {hash}", computedHashString, hash);
                    _dbContext.Remove(relatedFile);
                    await _dbContext.SaveChangesAsync().ConfigureAwait(false);

                    return;
                }

                File.Move(tempFileName, finalFileName, true);
                relatedFile = _dbContext.Files.Single(f => f.Hash == hash);
                relatedFile.Uploaded = true;

                MareMetrics.FilesTotal.Inc();
                MareMetrics.FilesTotalSize.Inc(length);

                await _dbContext.SaveChangesAsync().ConfigureAwait(false);
                _logger.LogInformation("File {hash} added to DB", hash);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Upload failed");
                _dbContext.Remove(relatedFile);
                await _dbContext.SaveChangesAsync().ConfigureAwait(false);
            }
        }
    }
}
