// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Core;
using Snap.Hutao.Core.ExceptionService;
using Snap.Hutao.Core.IO.Http;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;

namespace Snap.Hutao.Service.Game.Island;

[Service(ServiceLifetime.Singleton)]
internal sealed partial class HoYoShadeShaderService
{
    private const string RepositoryUrl = "https://github.com/crosire/reshade-shaders";
    private const string RepositoryArchiveUrl = $"{RepositoryUrl}/archive/HEAD.zip";
    private const string ShaderDirectoryName = "Shaders";
    private const string TextureDirectoryName = "Textures";
    private const int MaxAttemptCount = 3;

    private readonly IHttpClientFactory httpClientFactory;

    [GeneratedConstructor]
    public partial HoYoShadeShaderService(IServiceProvider serviceProvider);

    public bool HasLibrary()
    {
        string libraryDirectory = GetLibraryDirectory();
        return ContainsLibraryFiles(libraryDirectory);
    }

    public async ValueTask<bool> InstallOrReinstallAsync(IProgress<HoYoShadeShaderInstallProgress> progress, CancellationToken token)
    {
        string libraryDirectory = GetLibraryDirectory();
        bool isReinstall = HasLibrary();

        string installationDirectory = Path.Combine(HoYoShadeRuntime.GetRuntimeDirectory(), ".installing", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installationDirectory);
        try
        {
            string archivePath = Path.Combine(installationDirectory, "reshade-shaders.zip");
            await DownloadArchiveAsync(RepositoryArchiveUrl, archivePath, progress, token).ConfigureAwait(false);

            string stagedLibraryDirectory = Path.Combine(installationDirectory, "reshade-shaders");
            await ExtractArchiveAsync(archivePath, stagedLibraryDirectory, progress, token).ConfigureAwait(false);

            CommitLibrary(stagedLibraryDirectory, libraryDirectory);
            return isReinstall;
        }
        finally
        {
            if (Directory.Exists(installationDirectory))
            {
                Directory.Delete(installationDirectory, true);
            }
        }
    }

    private async ValueTask DownloadArchiveAsync(string url, string archivePath, IProgress<HoYoShadeShaderInstallProgress> progress, CancellationToken token)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            using HttpClient client = httpClientFactory.CreateClient();
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, url);
            using HttpResponseMessage response = await SendAsync(client, request, token).ConfigureAwait(false);
            long? totalBytes = response.Content.Headers.ContentLength;
            await using Stream source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using FileStream target = new(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true);

            byte[] buffer = new byte[131072];
            long downloadedBytes = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                downloadedBytes += read;
                progress.Report(new(SH.ViewDialogHoYoShadeConfigurationDownloadingShaderLibrary, totalBytes is > 0 ? downloadedBytes * 100D / totalBytes.Value : default));
            }
        }, progress, token).ConfigureAwait(false);
    }

    private static async ValueTask ExtractArchiveAsync(string archivePath, string stagedLibraryDirectory, IProgress<HoYoShadeShaderInstallProgress> progress, CancellationToken token)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        HashSet<string> destinations = new(StringComparer.OrdinalIgnoreCase);
        int totalEntries = archive.Entries.Count(static entry => IsLibraryFile(entry.FullName));
        int extractedEntries = 0;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!TryGetLibraryRelativePath(entry.FullName, out string? relativePath))
            {
                continue;
            }

            string destinationPath = Path.GetFullPath(Path.Combine(stagedLibraryDirectory, relativePath));
            string stagedRoot = Path.GetFullPath(stagedLibraryDirectory) + Path.DirectorySeparatorChar;
            if (!destinationPath.StartsWith(stagedRoot, StringComparison.OrdinalIgnoreCase) || !destinations.Add(destinationPath))
            {
                throw HutaoException.InvalidOperation("Invalid HoYoShade shader archive path");
            }

            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
            Directory.CreateDirectory(destinationDirectory);
            await using Stream source = entry.Open();
            await using FileStream target = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
            await source.CopyToAsync(target, token).ConfigureAwait(false);
            extractedEntries++;
            progress.Report(new(SH.ViewDialogHoYoShadeConfigurationExtractingShaderLibrary, totalEntries > 0 ? extractedEntries * 100D / totalEntries : default));
        }

        if (!Directory.Exists(Path.Combine(stagedLibraryDirectory, ShaderDirectoryName))
            || !Directory.Exists(Path.Combine(stagedLibraryDirectory, TextureDirectoryName))
            || !Directory.EnumerateFiles(Path.Combine(stagedLibraryDirectory, ShaderDirectoryName), "*.fx", SearchOption.AllDirectories).Any())
        {
            throw HutaoException.InvalidOperation("HoYoShade shader archive does not contain effects");
        }
    }

    private static bool IsLibraryFile(string entryName)
    {
        return TryGetLibraryRelativePath(entryName, out _);
    }

    private static bool TryGetLibraryRelativePath(string entryName, [NotNullWhen(true)] out string? relativePath)
    {
        relativePath = default;
        string[] segments = entryName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3 || segments[1] is not (ShaderDirectoryName or TextureDirectoryName))
        {
            return false;
        }

        relativePath = Path.Combine(segments[1..]);
        return true;
    }

    private static void CommitLibrary(string stagedLibraryDirectory, string libraryDirectory)
    {
        string transactionId = Guid.NewGuid().ToString("N");
        string[] directoryNames = [ShaderDirectoryName, TextureDirectoryName];
        List<(string Destination, string Backup)> backupDirectories = [];
        List<string> committedDirectories = [];
        bool committed = false;
        try
        {
            foreach (string directoryName in directoryNames)
            {
                string destinationDirectory = Path.Combine(libraryDirectory, directoryName);
                if (Directory.Exists(destinationDirectory))
                {
                    string backupDirectory = $"{destinationDirectory}.{transactionId}.backup";
                    Directory.Move(destinationDirectory, backupDirectory);
                    backupDirectories.Add((destinationDirectory, backupDirectory));
                }
            }

            foreach (string directoryName in directoryNames)
            {
                string sourceDirectory = Path.Combine(stagedLibraryDirectory, directoryName);
                string destinationDirectory = Path.Combine(libraryDirectory, directoryName);
                Directory.Move(sourceDirectory, destinationDirectory);
                committedDirectories.Add(destinationDirectory);
            }

            committed = true;
        }
        catch
        {
            foreach (string destinationDirectory in committedDirectories)
            {
                if (Directory.Exists(destinationDirectory))
                {
                    Directory.Delete(destinationDirectory, true);
                }
            }

            foreach ((string destinationDirectory, string backupDirectory) in backupDirectories)
            {
                if (!Directory.Exists(destinationDirectory) && Directory.Exists(backupDirectory))
                {
                    Directory.Move(backupDirectory, destinationDirectory);
                }
            }

            throw;
        }
        finally
        {
            if (committed)
            {
                foreach ((_, string backupDirectory) in backupDirectories)
                {
                    if (Directory.Exists(backupDirectory))
                    {
                        Directory.Delete(backupDirectory, true);
                    }
                }
            }
        }
    }

    private static bool ContainsLibraryFiles(string libraryDirectory)
    {
        return new[] { ShaderDirectoryName, TextureDirectoryName }
            .Any(directoryName => Directory.Exists(Path.Combine(libraryDirectory, directoryName)) && Directory.EnumerateFiles(Path.Combine(libraryDirectory, directoryName), "*", SearchOption.AllDirectories).Any());
    }

    private static string GetLibraryDirectory()
    {
        return Path.Combine(HoYoShadeRuntime.GetRuntimeDirectory(), "reshade-shaders");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        HttpRequestMessage request = new(method, url);
        request.Headers.UserAgent.ParseAdd(HutaoRuntime.UserAgent);
        request.Options.Set(RetryHttpHandler.DisableRetry, true);
        return request;
    }

    private static async ValueTask<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken token)
    {
        HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            response.Dispose();
            throw new HoYoShadeRateLimitException(SH.ViewDialogHoYoShadeConfigurationGitHubRateLimited);
        }

        response.EnsureSuccessStatusCode();
        return response;
    }

    private static async ValueTask ExecuteWithRetryAsync(Func<Task> action, IProgress<HoYoShadeShaderInstallProgress>? progress, CancellationToken token)
    {
        Exception? exception = default;
        for (int attempt = 1; attempt <= MaxAttemptCount; attempt++)
        {
            try
            {
                await action().ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HoYoShadeRateLimitException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxAttemptCount)
            {
                exception = ex;
                progress?.Report(new(SH.ViewDialogHoYoShadeConfigurationRetryingShaderLibrary, default));
            }
        }

        HutaoException.Throw(SH.ViewDialogHoYoShadeConfigurationInstallFailed, exception);
    }

    private sealed class HoYoShadeRateLimitException(string message) : Exception(message);
}

internal readonly record struct HoYoShadeShaderInstallProgress(string Text, double? Percentage);
