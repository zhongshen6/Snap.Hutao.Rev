// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.UI.Xaml.Controls;
using Snap.Hutao.Core;
using Snap.Hutao.Core.LifeCycle;
using Snap.Hutao.Core.Setting;
using Snap.Hutao.Factory.ContentDialog;
using Snap.Hutao.Service.Notification;
using Snap.Hutao.Web.Hutao;
using System.Net.Http;

namespace Snap.Hutao.Service.Update;

[Service(ServiceLifetime.Singleton, typeof(IUpdateService))]
internal sealed partial class UpdateService : IUpdateService
{
    private const string LatestReleasePage = "https://github.com/zhongshen6/Snap.Hutao.Rev/releases/latest";

    // Avoid injecting services directly
    private readonly IServiceProvider serviceProvider;

    [GeneratedConstructor]
    public partial UpdateService(IServiceProvider serviceProvider);

    public string? UpdateInfo { get; set; }

    public async ValueTask<CheckUpdateResult> CheckUpdateAsync(CancellationToken token = default)
    {
        using (IServiceScope scope = serviceProvider.CreateScope())
        {
            CheckUpdateResult checkUpdateResult = new();
            try
            {
                ITaskContext taskContext = scope.ServiceProvider.GetRequiredService<ITaskContext>();
                await taskContext.SwitchToBackgroundAsync();

                GitHubLatestRelease? latestRelease = await GetLatestReleaseFromRedirectAsync(scope.ServiceProvider, token).ConfigureAwait(false);
                if (latestRelease is null || !TryCreatePackageInformation(latestRelease, out HutaoPackageInformation packageInformation))
                {
                    checkUpdateResult.Kind = CheckUpdateResultKind.VersionApiInvalidResponse;
                    return checkUpdateResult;
                }

                checkUpdateResult.Kind = CheckUpdateResultKind.UpdateAvailable;
                checkUpdateResult.PackageInformation = packageInformation;

                if (!LocalSetting.Get(SettingKeys.OverrideUpdateVersionComparison, false))
                {
                    // Launched in an updated version
                    if (HutaoRuntime.Version >= checkUpdateResult.PackageInformation.Version)
                    {
                        checkUpdateResult.Kind = CheckUpdateResultKind.AlreadyUpdated;
                        return checkUpdateResult;
                    }
                }

                if (checkUpdateResult.PackageInformation.Validation is not { Length: > 0 })
                {
                    checkUpdateResult.Kind = CheckUpdateResultKind.VersionApiInvalidSha256;
                }
                return checkUpdateResult;
            }
            finally
            {
                UpdateInfo = checkUpdateResult.Kind switch
                {
                    CheckUpdateResultKind.UpdateAvailable => SH.FormatViewModelSettingUpdateAvailable(checkUpdateResult.PackageInformation?.Version.ToString()),
                    CheckUpdateResultKind.AlreadyUpdated => SH.ViewModelSettingAlreadyUpdated,
                    CheckUpdateResultKind.VersionApiInvalidResponse or CheckUpdateResultKind.VersionApiInvalidSha256 => SH.ViewModelSettingCheckUpdateFailed,
                    _ => default,
                };
            }
        }
    }

    public async ValueTask TriggerUpdateAsync(CheckUpdateResult result, CancellationToken token = default)
    {
        if (result.Kind is not CheckUpdateResultKind.UpdateAvailable)
        {
            return;
        }

        using (IServiceScope scope = serviceProvider.CreateScope())
        {
            ICurrentXamlWindowReference currentXamlWindowReference = scope.ServiceProvider.GetRequiredService<ICurrentXamlWindowReference>();
            IContentDialogFactory contentDialogFactory = scope.ServiceProvider.GetRequiredService<IContentDialogFactory>();
            IMessenger messenger = scope.ServiceProvider.GetRequiredService<IMessenger>();

            if (currentXamlWindowReference.Window is null)
            {
                return;
            }

            try
            {
                ContentDialogResult installUpdateUserConsentResult = await contentDialogFactory
                    .CreateForConfirmCancelAsync(
                        SH.FormatViewTitleUpdatePackageAvailableTitle(result.PackageInformation?.Version),
                        SH.ViewTitileUpdatePackageAvailableContent,
                        ContentDialogButton.Primary)
                    .ConfigureAwait(false);

                if (installUpdateUserConsentResult is not ContentDialogResult.Primary)
                {
                    return;
                }

                if (result.PackageInformation?.Mirrors.SingleOrDefault(static mirror => mirror.MirrorType is Web.Hutao.HutaoPackageMirrorType.Browser) is { } mirror)
                {
                    await Windows.System.Launcher.LaunchUriAsync(mirror.Url.ToUri());
                }
            }
            catch (Exception ex)
            {
                // Access to the path '?' is denied.
                // 0x80070002 无法启动服务，原因可能是已被禁用或与其相关联的设备没有启动
                // The process cannot access the file '?' because it is being used by another process.
                // 0x80070005 Attempted to perform an unauthorized operation.
                messenger.Send(InfoBarMessage.Error(ex));
            }
        }
    }

    private static bool TryCreatePackageInformation(GitHubLatestRelease release, out HutaoPackageInformation packageInformation)
    {
        packageInformation = default!;
        if (!TryParseVersion(release.TagName, out Version version) && !TryParseVersion(release.Name, out version))
        {
            return false;
        }

        packageInformation = new()
        {
            Version = version,
            Validation = release.TagName ?? release.HtmlUrl ?? "github",
            Mirrors =
            [
                new HutaoPackageMirror
                {
                    Url = string.IsNullOrWhiteSpace(release.HtmlUrl) ? LatestReleasePage : release.HtmlUrl!,
                    MirrorName = "GitHub Releases",
                    MirrorType = HutaoPackageMirrorType.Browser,
                },
            ],
        };
        return true;
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = default!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim().TrimStart('v', 'V');
        int end = 0;
        while (end < trimmed.Length && (char.IsDigit(trimmed[end]) || trimmed[end] is '.'))
        {
            end++;
        }

        return end > 0 && Version.TryParse(trimmed[..end], out version);
    }

    private static async ValueTask<GitHubLatestRelease?> GetLatestReleaseFromRedirectAsync(IServiceProvider serviceProvider, CancellationToken token)
    {
        IHttpClientFactory httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        using HttpClient httpClient = httpClientFactory.CreateClient();
        using CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(token);
        source.CancelAfter(TimeSpan.FromSeconds(5));

        using HttpRequestMessage request = new(HttpMethod.Get, LatestReleasePage);
        request.Headers.UserAgent.ParseAdd("Snap.Hutao.Rev");

        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, source.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string? releasePage = response.RequestMessage?.RequestUri?.AbsoluteUri;
        string? tagName = response.RequestMessage?.RequestUri?.Segments.LastOrDefault()?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(releasePage) || string.IsNullOrWhiteSpace(tagName)
            ? null
            : new()
            {
                TagName = tagName,
                Name = tagName,
                HtmlUrl = releasePage,
            };
    }

    private sealed class GitHubLatestRelease
    {
        public string? TagName { get; set; }

        public string? Name { get; set; }

        public string? HtmlUrl { get; set; }
    }
}
