// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Core;
using Snap.Hutao.Core.ExceptionService;
using Snap.Hutao.Core.IO.Http;
using Snap.Hutao.Core.IO.Ini;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace Snap.Hutao.Service.Game.Island;

[Service(ServiceLifetime.Singleton)]
internal sealed partial class HoYoShadeShaderService
{
    private const string EffectPackagesUrl = "https://raw.githubusercontent.com/crosire/reshade-shaders/list/EffectPackages.ini";
    private const string AddonsUrl = "https://raw.githubusercontent.com/crosire/reshade-shaders/list/Addons.ini";
    private const string PresetsArchiveUrl = "https://github.com/HoYoShade-Dev/HoYoShade.Presets/archive/HEAD.zip";
    private const string ShaderDirectoryName = "Shaders";
    private const string TextureDirectoryName = "Textures";
    private const string PresetDirectoryName = "Presets";
    private const string AddonDirectoryName = "Addons";
    private const int MaxAttemptCount = 3;
    private static readonly TimeSpan NetworkIdleTimeout = TimeSpan.FromSeconds(30);

    private static readonly IReadOnlySet<string> EmptyDeniedEffectFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<string> AddonResourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".fx",
        ".addonfx",
        ".fxh",
    };

    private readonly IHttpClientFactory httpClientFactory;

    [GeneratedConstructor]
    public partial HoYoShadeShaderService(IServiceProvider serviceProvider);

    public ValueTask<IReadOnlyList<HoYoShadeEffectPackage>> GetEffectPackagesAsync(CancellationToken token)
        => GetCatalogAsync(EffectPackagesUrl, ParseEffectPackages, token);

    public ValueTask<IReadOnlyList<HoYoShadeAddon>> GetAddonsAsync(CancellationToken token)
        => GetCatalogAsync(AddonsUrl, ParseAddons, token);

    private async ValueTask<T> GetCatalogAsync<T>(string url, Func<Stream, T> parser, CancellationToken token)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            using HttpClient client = httpClientFactory.CreateClient();
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, url);
            using HttpResponseMessage response = await SendAsync(client, request, token).ConfigureAwait(false);
            byte[] content = await ReadResponseContentAsync(response, token).ConfigureAwait(false);
            using MemoryStream stream = new(content, writable: false);
            return parser(stream);
        }, default, token).ConfigureAwait(false);
    }

    public async ValueTask InstallAsync(
        IReadOnlyList<HoYoShadeEffectPackage> packages,
        IReadOnlyList<HoYoShadeAddon> addons,
        bool includePresets,
        IProgress<HoYoShadeShaderInstallProgress> progress,
        Func<ValueTask> beforeCommitAsync,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(addons);
        if (packages.Count is 0 && addons.Count is 0)
        {
            throw HutaoException.InvalidOperation("No HoYoShade resources were selected");
        }

        string runtimeDirectory = HoYoShadeRuntime.GetRuntimeDirectory();
        string installationDirectory = Path.Combine(runtimeDirectory, ".installing", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installationDirectory);
        bool retainInstallationDirectory = false;

        try
        {
            Dictionary<string, string> stagedFiles = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < packages.Count; index++)
            {
                HoYoShadeEffectPackage package = packages[index];
                string packageDirectory = Path.Combine(installationDirectory, $"package-{index:D3}");
                string archivePath = Path.Combine(packageDirectory, "package.zip");
                string extractedDirectory = Path.Combine(packageDirectory, "extracted");

                await ExecuteInstallStepAsync(async () =>
                {
                    await DownloadAndExtractArchiveAsync(
                        package.DownloadUrl,
                        archivePath,
                        extractedDirectory,
                        percentage => FormatPackageProgress(SH.ViewDialogHoYoShadeConfigurationDownloadingEffectPackage, package.PackageName, index + 1, packages.Count, percentage),
                        percentage => FormatPackageProgress(SH.ViewDialogHoYoShadeConfigurationExtractingEffectPackage, package.PackageName, index + 1, packages.Count, percentage),
                        progress,
                        token).ConfigureAwait(false);

                    AddPackageFiles(package, extractedDirectory, runtimeDirectory, stagedFiles);
                }, $"Failed to install HoYoShade effect package '{package.PackageName}'.", token).ConfigureAwait(false);
            }

            for (int index = 0; index < addons.Count; index++)
            {
                HoYoShadeAddon addon = addons[index];
                string addonDirectory = Path.Combine(installationDirectory, $"addon-{index:D3}");
                string archivePath = Path.Combine(addonDirectory, "addon.bin");
                string extractedDirectory = Path.Combine(addonDirectory, "extracted");
                Directory.CreateDirectory(addonDirectory);

                await ExecuteInstallStepAsync(async () =>
                {
                    if (IsAddonArchive(addon.DownloadUrl))
                    {
                        await DownloadAndExtractArchiveAsync(
                            addon.DownloadUrl,
                            archivePath,
                            extractedDirectory,
                            percentage => FormatPackageProgress(SH.ViewDialogHoYoShadeConfigurationDownloadingAddon, addon.PackageName, index + 1, addons.Count, percentage),
                            percentage => FormatPackageProgress(SH.ViewDialogHoYoShadeConfigurationExtractingAddon, addon.PackageName, index + 1, addons.Count, percentage),
                            progress,
                            token).ConfigureAwait(false);

                        AddAddonFiles(addon, archivePath, extractedDirectory, runtimeDirectory, stagedFiles);
                    }
                    else
                    {
                        progress.Report(new(FormatPackageProgress(SH.ViewDialogHoYoShadeConfigurationDownloadingAddon, addon.PackageName, index + 1, addons.Count, default), default));
                        await DownloadArchiveAsync(
                            addon.DownloadUrl,
                            archivePath,
                            percentage => FormatPackageProgress(SH.ViewDialogHoYoShadeConfigurationDownloadingAddon, addon.PackageName, index + 1, addons.Count, percentage),
                            progress,
                            token).ConfigureAwait(false);
                        AddAddonFiles(addon, archivePath, default, runtimeDirectory, stagedFiles);
                    }
                }, $"Failed to install HoYoShade addon '{addon.PackageName}'.", token).ConfigureAwait(false);
            }

            if (includePresets)
            {
                string presetDirectory = Path.Combine(installationDirectory, "presets");
                string archivePath = Path.Combine(presetDirectory, "presets.zip");
                string extractedDirectory = Path.Combine(presetDirectory, "extracted");

                await ExecuteInstallStepAsync(async () =>
                {
                    await DownloadAndExtractArchiveAsync(
                        PresetsArchiveUrl,
                        archivePath,
                        extractedDirectory,
                        percentage => FormatPresetProgress(SH.ViewDialogHoYoShadeConfigurationDownloadingPresets, percentage),
                        percentage => FormatPresetProgress(SH.ViewDialogHoYoShadeConfigurationExtractingPresets, percentage),
                        progress,
                        token).ConfigureAwait(false);

                    AddPresetFiles(extractedDirectory, runtimeDirectory, stagedFiles);
                }, "Failed to install the HoYoShade presets.", token).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            await beforeCommitAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            progress.Report(new(SH.ViewDialogHoYoShadeConfigurationCommittingResources, default));
            try
            {
                HoYoShadeResourceFileOperations.CommitStagedFiles(stagedFiles, installationDirectory);
            }
            catch (HoYoShadeResourceTransactionException)
            {
                retainInstallationDirectory = true;
                throw;
            }
        }
        finally
        {
            if (!retainInstallationDirectory)
            {
                TryDeleteDirectory(installationDirectory);
            }
        }
    }

    private static IReadOnlyList<HoYoShadeEffectPackage> ParseEffectPackages(Stream stream)
    {
        HashSet<string> packageIds = new(StringComparer.OrdinalIgnoreCase);
        List<HoYoShadeEffectPackage> packages = [];

        foreach (IniSection section in IniSerializer.Deserialize(stream).OfType<IniSection>())
        {
            Dictionary<string, string> values = section.Children
                .OfType<IniParameter>()
                .Where(static parameter => !parameter.Key.StartsWith('#'))
                .ToDictionary(static parameter => parameter.Key, static parameter => parameter.Value, StringComparer.OrdinalIgnoreCase);

            string packageName = GetRequiredValue(values, "PackageName");
            string packageDescription = GetRequiredValue(values, "PackageDescription");
            string installPath = GetRequiredValue(values, "InstallPath");
            string textureInstallPath = GetRequiredValue(values, "TextureInstallPath");
            string downloadUrl = GetRequiredValue(values, "DownloadUrl");
            string repositoryUrl = GetRequiredValue(values, "RepositoryUrl");
            IReadOnlySet<string> effectFiles = GetCommaSeparatedValues(values, "EffectFiles");

            if (!packageIds.Add(section.Name)
                || effectFiles.Count is 0
                || !TryGetHttpsUri(downloadUrl, out _)
                || !TryGetHttpsUri(repositoryUrl, out _))
            {
                throw HutaoException.InvalidOperation("Invalid HoYoShade effect package catalog");
            }

            HoYoShadeResourceFileOperations.GetPackageRelativePath(installPath, ShaderDirectoryName);
            HoYoShadeResourceFileOperations.GetPackageRelativePath(textureInstallPath, TextureDirectoryName);

            packages.Add(new(
                packageName,
                packageDescription,
                installPath,
                textureInstallPath,
                downloadUrl,
                GetBooleanValue(values, "Required"),
                GetBooleanValue(values, "Enabled"),
                GetCommaSeparatedValues(values, "DenyEffectFiles")));
        }

        if (packages.Count is 0)
        {
            throw HutaoException.InvalidOperation("HoYoShade effect package catalog is empty");
        }

        return packages;
    }

    private static IReadOnlyList<HoYoShadeAddon> ParseAddons(Stream stream)
    {
        HashSet<string> addonIds = new(StringComparer.OrdinalIgnoreCase);
        List<HoYoShadeAddon> addons = [];

        foreach (IniSection section in IniSerializer.Deserialize(stream).OfType<IniSection>())
        {
            Dictionary<string, string> values = section.Children
                .OfType<IniParameter>()
                .Where(static parameter => !parameter.Key.StartsWith('#'))
                .ToDictionary(static parameter => parameter.Key, static parameter => parameter.Value, StringComparer.OrdinalIgnoreCase);

            string downloadUrl = GetAddonDownloadUrl(values);
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                continue;
            }

            string packageName = GetRequiredValue(values, "PackageName");
            string packageDescription = GetRequiredValue(values, "PackageDescription");
            string repositoryUrl = GetRequiredValue(values, "RepositoryUrl");
            string effectInstallPath = values.TryGetValue("EffectInstallPath", out string? configuredPath) ? configuredPath : string.Empty;

            if (!addonIds.Add(section.Name)
                || !TryGetHttpsUri(downloadUrl, out _)
                || !TryGetHttpsUri(repositoryUrl, out _)
                || (!string.IsNullOrWhiteSpace(effectInstallPath)
                    && !IsValidPackagePath(effectInstallPath, ShaderDirectoryName)))
            {
                throw HutaoException.InvalidOperation("Invalid HoYoShade addon catalog");
            }

            addons.Add(new(section.Name, packageName, packageDescription, effectInstallPath, downloadUrl));
        }

        return addons;
    }

    private static async ValueTask ExecuteInstallStepAsync(Func<ValueTask> action, string errorMessage, CancellationToken token)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(errorMessage, ex);
        }
    }

    private static void AddPackageFiles(HoYoShadeEffectPackage package, string extractedDirectory, string runtimeDirectory, Dictionary<string, string> stagedFiles)
    {
        string shaderDestinationDirectory = Path.Combine(runtimeDirectory, HoYoShadeResourceFileOperations.GetPackageRelativePath(package.InstallPath, ShaderDirectoryName));
        string textureDestinationDirectory = Path.Combine(runtimeDirectory, HoYoShadeResourceFileOperations.GetPackageRelativePath(package.TextureInstallPath, TextureDirectoryName));
        string shaderSourceDirectory = HoYoShadeResourceFileOperations.FindEffectDirectory(extractedDirectory)
            ?? throw HutaoException.InvalidOperation($"HoYoShade effect package does not contain effects: {package.PackageName}");
        string? textureSourceDirectory = HoYoShadeResourceFileOperations.FindTextureDirectory(extractedDirectory);

        HoYoShadeResourceFileOperations.AddDirectoryFiles(shaderSourceDirectory, shaderDestinationDirectory, package.DenyEffectFiles, stagedFiles);
        if (textureSourceDirectory is not null)
        {
            HoYoShadeResourceFileOperations.AddDirectoryFiles(textureSourceDirectory, textureDestinationDirectory, EmptyDeniedEffectFiles, stagedFiles);
        }
    }

    private static void AddAddonFiles(
        HoYoShadeAddon addon,
        string downloadedFile,
        string? extractedDirectory,
        string runtimeDirectory,
        Dictionary<string, string> stagedFiles)
    {
        string addonSourceFile = extractedDirectory is null
            ? downloadedFile
            : FindAddonFile(extractedDirectory)
                ?? throw HutaoException.InvalidOperation($"HoYoShade addon archive does not contain a 64-bit addon: {addon.PackageName}");

        string addonDirectory = Path.Combine(
            runtimeDirectory,
            HoYoShadeResourceFileOperations.GetPackageRelativePath($"reshade-shaders/{AddonDirectoryName}", AddonDirectoryName));
        string addonFileName = Path.GetFileNameWithoutExtension(
            extractedDirectory is null
                ? new Uri(addon.DownloadUrl).AbsolutePath
                : addonSourceFile);
        if (string.IsNullOrWhiteSpace(addonFileName))
        {
            addonFileName = addon.Id;
        }

        addonFileName += ".addon64";
        stagedFiles[Path.Combine(addonDirectory, addonFileName)] = addonSourceFile;

        if (extractedDirectory is null)
        {
            return;
        }

        string? effectSourceDirectory = HoYoShadeResourceFileOperations.FindEffectDirectory(extractedDirectory);
        if (effectSourceDirectory is null)
        {
            return;
        }

        string effectsDirectory = string.IsNullOrWhiteSpace(addon.EffectInstallPath)
            ? Path.Combine(runtimeDirectory, "reshade-shaders", ShaderDirectoryName)
            : Path.Combine(runtimeDirectory, HoYoShadeResourceFileOperations.GetPackageRelativePath(addon.EffectInstallPath, ShaderDirectoryName));
        HoYoShadeResourceFileOperations.AddDirectoryFiles(effectSourceDirectory, effectsDirectory, EmptyDeniedEffectFiles, stagedFiles, AddonResourceExtensions);
    }

    private static string? FindAddonFile(string extractedDirectory)
    {
        string? addon64 = Directory.EnumerateFiles(extractedDirectory, "*.addon64", SearchOption.AllDirectories).FirstOrDefault();
        if (addon64 is not null)
        {
            return addon64;
        }

        string[] genericAddons = Directory.EnumerateFiles(extractedDirectory, "*.addon", SearchOption.AllDirectories).ToArray();
        return genericAddons.Length is 1
            ? genericAddons[0]
            : genericAddons.FirstOrDefault(static path =>
                Path.GetFileNameWithoutExtension(path).EndsWith("64", StringComparison.OrdinalIgnoreCase)
                || path.Contains("x64", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetAddonDownloadUrl(IReadOnlyDictionary<string, string> values)
    {
        if (values.TryGetValue("DownloadUrl64", out string? downloadUrl64)
            && !string.IsNullOrWhiteSpace(downloadUrl64))
        {
            return downloadUrl64;
        }

        if (values.TryGetValue("DownloadUrl", out string? downloadUrl)
            && !string.IsNullOrWhiteSpace(downloadUrl)
            && (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? uri)
                || !string.Equals(Path.GetExtension(uri.AbsolutePath), ".addon32", StringComparison.OrdinalIgnoreCase)))
        {
            return downloadUrl;
        }

        return string.Empty;
    }

    private static bool IsValidPackagePath(string configuredPath, string expectedDirectory)
    {
        try
        {
            HoYoShadeResourceFileOperations.GetPackageRelativePath(configuredPath, expectedDirectory);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool IsAddonArchive(string url)
    {
        string extension = Path.GetExtension(new Uri(url).AbsolutePath);
        return !string.Equals(extension, ".addon", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".addon32", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".addon64", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddPresetFiles(string extractedDirectory, string runtimeDirectory, Dictionary<string, string> stagedFiles)
    {
        string[] topLevelFiles = Directory.EnumerateFiles(extractedDirectory).ToArray();
        string[] topLevelDirectories = Directory.EnumerateDirectories(extractedDirectory).ToArray();
        if (topLevelFiles.Length is not 0 || topLevelDirectories.Length is not 1)
        {
            throw HutaoException.InvalidOperation("Invalid HoYoShade preset archive layout");
        }

        string presetDestinationDirectory = Path.Combine(runtimeDirectory, PresetDirectoryName);
        HoYoShadeResourceFileOperations.AddDirectoryFiles(topLevelDirectories[0], presetDestinationDirectory, EmptyDeniedEffectFiles, stagedFiles);
    }

    private async ValueTask DownloadAndExtractArchiveAsync(
        string url,
        string archivePath,
        string extractedDirectory,
        Func<double?, string> downloadProgressText,
        Func<double?, string> extractProgressText,
        IProgress<HoYoShadeShaderInstallProgress> progress,
        CancellationToken token)
    {
        string? archiveDirectory = Path.GetDirectoryName(archivePath);
        ArgumentException.ThrowIfNullOrEmpty(archiveDirectory);
        Directory.CreateDirectory(archiveDirectory);
        progress.Report(new(downloadProgressText(default), default));
        await DownloadArchiveAsync(url, archivePath, downloadProgressText, progress, token).ConfigureAwait(false);
        progress.Report(new(extractProgressText(default), default));
        await ExtractArchiveAsync(archivePath, extractedDirectory, extractProgressText, progress, token).ConfigureAwait(false);
    }

    private static async ValueTask ExtractArchiveAsync(string archivePath, string extractedDirectory, Func<double?, string> getProgressText, IProgress<HoYoShadeShaderInstallProgress> progress, CancellationToken token)
    {
        await using FileStream source = File.OpenRead(archivePath);
        await ZipFile.ExtractToDirectoryAsync(source, extractedDirectory, overwriteFiles: false, token).ConfigureAwait(false);
        progress.Report(new(getProgressText(100), 100));
    }

    private async ValueTask DownloadArchiveAsync(string url, string archivePath, Func<double?, string> getProgressText, IProgress<HoYoShadeShaderInstallProgress> progress, CancellationToken token)
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
            Stopwatch progressInterval = Stopwatch.StartNew();
            int read;
            while ((read = await ReadWithIdleTimeoutAsync(source, buffer, token).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                downloadedBytes += read;
                if (progressInterval.ElapsedMilliseconds >= 100)
                {
                    double? percentage = totalBytes is > 0 ? downloadedBytes * 100D / totalBytes.Value : default;
                    progress.Report(new(getProgressText(percentage), percentage));
                    progressInterval.Restart();
                }
            }

            progress.Report(new(getProgressText(100), 100));
            return true;
        }, progress, token).ConfigureAwait(false);
    }

    private static string GetRequiredValue(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
        {
            throw HutaoException.InvalidOperation("Invalid HoYoShade effect package catalog");
        }

        return value;
    }

    private static bool GetBooleanValue(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out string? value) && string.Equals(value, "1", StringComparison.Ordinal);
    }

    private static IReadOnlySet<string> GetCommaSeparatedValues(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out string? value)
            ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : EmptyDeniedEffectFiles;
    }

    private static bool TryGetHttpsUri(string value, [NotNullWhen(true)] out Uri? uri)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out uri) && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPackageProgress(string format, string packageName, int packageIndex, int packageCount, double? percentage)
    {
        return string.Format(CultureInfo.CurrentCulture, format, packageName, packageIndex, packageCount, FormatPercentage(percentage));
    }

    private static string FormatPresetProgress(string format, double? percentage)
    {
        return string.Format(CultureInfo.CurrentCulture, format, 1, 1, FormatPercentage(percentage));
    }

    private static string FormatPercentage(double? percentage)
    {
        return percentage is { } value
            ? string.Format(CultureInfo.CurrentCulture, "{0:0}%", value)
            : SH.ViewDialogHoYoShadeConfigurationProgressUnknown;
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
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(NetworkIdleTimeout);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("HoYoShade network request timed out while waiting for response headers");
        }

        try
        {
            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)
            {
                throw new HoYoShadeRateLimitException(SH.ViewDialogHoYoShadeConfigurationGitHubRateLimited);
            }

            response.EnsureSuccessStatusCode();
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static async ValueTask<byte[]> ReadResponseContentAsync(HttpResponseMessage response, CancellationToken token)
    {
        await using Stream source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using MemoryStream target = new();
        byte[] buffer = new byte[131072];
        int read;
        while ((read = await ReadWithIdleTimeoutAsync(source, buffer, token).ConfigureAwait(false)) > 0)
        {
            target.Write(buffer, 0, read);
        }

        return target.ToArray();
    }

    private static async ValueTask<int> ReadWithIdleTimeoutAsync(Stream source, Memory<byte> buffer, CancellationToken token)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(NetworkIdleTimeout);
        try
        {
            return await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("HoYoShade download stalled while reading response data");
        }
    }

    private static async ValueTask<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, IProgress<HoYoShadeShaderInstallProgress>? progress, CancellationToken token)
    {
        for (int attempt = 1; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HoYoShadeRateLimitException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxAttemptCount && ex is HttpRequestException or TimeoutException)
            {
                progress?.Report(new(SH.ViewDialogHoYoShadeConfigurationRetryingShaderLibrary, default));
                await Task.Delay(TimeSpan.FromSeconds(attempt), token).ConfigureAwait(false);
            }
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class HoYoShadeRateLimitException(string message) : Exception(message);
}

internal sealed class HoYoShadeEffectPackage : INotifyPropertyChanged
{
    private bool isSelected;

    public HoYoShadeEffectPackage(
        string packageName,
        string packageDescription,
        string installPath,
        string textureInstallPath,
        string downloadUrl,
        bool isRequired,
        bool isDefaultEnabled,
        IReadOnlySet<string> denyEffectFiles)
    {
        PackageName = packageName;
        PackageDescription = packageDescription;
        InstallPath = installPath;
        TextureInstallPath = textureInstallPath;
        DownloadUrl = downloadUrl;
        IsRequired = isRequired;
        IsDefaultEnabled = isDefaultEnabled;
        DenyEffectFiles = denyEffectFiles;
        isSelected = isRequired || isDefaultEnabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PackageName { get; }

    public string PackageDescription { get; }

    public string InstallPath { get; }

    public string TextureInstallPath { get; }

    public string DownloadUrl { get; }

    public bool IsRequired { get; }

    public bool IsDefaultEnabled { get; }

    public IReadOnlySet<string> DenyEffectFiles { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (IsRequired || isSelected == value)
            {
                return;
            }

            isSelected = value;
            PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
        }
    }
}

internal sealed class HoYoShadeAddon : INotifyPropertyChanged
{
    private bool isSelected;

    public HoYoShadeAddon(
        string id,
        string packageName,
        string packageDescription,
        string effectInstallPath,
        string downloadUrl)
    {
        Id = id;
        PackageName = packageName;
        PackageDescription = packageDescription;
        EffectInstallPath = effectInstallPath;
        DownloadUrl = downloadUrl;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string PackageName { get; }

    public string PackageDescription { get; }

    public string EffectInstallPath { get; }

    public string DownloadUrl { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
        }
    }
}

internal readonly record struct HoYoShadeShaderInstallProgress(string Text, double? Percentage);
