// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using CommunityToolkit.Mvvm.Input;
using Snap.Hutao.Core.Setting;
using Snap.Hutao.Web.Hutao.HutaoAsAService;
using Snap.Hutao.Web.Response;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Windows.Storage;
using HutaoAnnouncement = Snap.Hutao.Web.Hutao.HutaoAsAService.Announcement;

namespace Snap.Hutao.Service.Hutao;

[Service(ServiceLifetime.Scoped, typeof(IHutaoAsAService))]
internal sealed partial class HutaoAsAService : IHutaoAsAService
{
    private const int AnnouncementDuration = 30;
    private const string CustomAnnouncementEndpoint = "https://raw.githubusercontent.com/zhongshen6/Snap.Hutao.Rev/main/announcements.json";

    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly IHttpClientFactory httpClientFactory;

    private ObservableCollection<HutaoAnnouncement>? announcements;
    private ICommand? dismissCommand;

    [GeneratedConstructor]
    public partial HutaoAsAService(IServiceProvider serviceProvider);

    public async ValueTask<ObservableCollection<HutaoAnnouncement>> GetHutaoAnnouncementCollectionAsync(CancellationToken token = default)
    {
        if (announcements is null)
        {
            dismissCommand = new RelayCommand<HutaoAnnouncement>(DismissAnnouncement);

            ApplicationDataCompositeValue excludedIds = LocalSetting.Get<ApplicationDataCompositeValue>(SettingKeys.ExcludedAnnouncementIds, []);
            ImmutableArray<long> data = [.. excludedIds.Select(static kvp => long.Parse(kvp.Key, CultureInfo.InvariantCulture))];

            Task<ImmutableArray<HutaoAnnouncement>> upstreamTask = GetUpstreamAnnouncementArrayAsync(data, token);
            Task<ImmutableArray<HutaoAnnouncement>> customTask = GetCustomAnnouncementArrayAsync(token);
            await Task.WhenAll(upstreamTask, customTask).ConfigureAwait(false);

            ImmutableArray<HutaoAnnouncement> upstream = await upstreamTask.ConfigureAwait(false);
            ImmutableArray<HutaoAnnouncement> custom = await customTask.ConfigureAwait(false);
            ImmutableArray<HutaoAnnouncement> merged = MergeAnnouncements(custom, upstream);

            foreach (HutaoAnnouncement item in merged)
            {
                item.DismissCommand = dismissCommand;
            }

            announcements = merged.ToObservableCollection();
        }

        return announcements;
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(?:\.\d+)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingSemanticVersionRegex();

    private static ImmutableArray<HutaoAnnouncement> MergeAnnouncements(ImmutableArray<HutaoAnnouncement> custom, ImmutableArray<HutaoAnnouncement> upstream)
    {
        Dictionary<long, HutaoAnnouncement> byId = [];

        foreach (HutaoAnnouncement item in custom)
        {
            byId[item.Id] = item;
        }

        foreach (HutaoAnnouncement item in upstream)
        {
            byId.TryAdd(item.Id, item);
        }

        return [.. byId.Values.OrderByDescending(static a => a.LastUpdateTime)];
    }

    private static bool IsUpstreamReleaseNotice(HutaoAnnouncement announcement)
    {
        if (string.IsNullOrEmpty(announcement.Title))
        {
            return false;
        }

        if (!LeadingSemanticVersionRegex().IsMatch(announcement.Title))
        {
            return false;
        }

        return !string.IsNullOrEmpty(announcement.Link) && announcement.Link.Contains("/download", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ImmutableArray<HutaoAnnouncement>> GetUpstreamAnnouncementArrayAsync(ImmutableArray<long> excludedIds, CancellationToken token)
    {
        using (IServiceScope scope = serviceScopeFactory.CreateScope())
        {
            HutaoAsAServiceClient hutaoAsAServiceClient = scope.ServiceProvider.GetRequiredService<HutaoAsAServiceClient>();
            Response<ImmutableArray<HutaoAnnouncement>> response = await hutaoAsAServiceClient.GetAnnouncementListAsync(excludedIds, token).ConfigureAwait(false);

            if (!ResponseValidator.TryValidate(response, scope.ServiceProvider, out ImmutableArray<HutaoAnnouncement> array))
            {
                return [];
            }

            return [.. array.Where(static a => (string.IsNullOrEmpty(a.Distribution) || a.Distribution == "Snap Hutao") && !IsUpstreamReleaseNotice(a))];
        }
    }

    private async Task<ImmutableArray<HutaoAnnouncement>> GetCustomAnnouncementArrayAsync(CancellationToken token)
    {
        try
        {
            using HttpClient httpClient = httpClientFactory.CreateClient(nameof(HutaoAsAService));
            using CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(token);
            source.CancelAfter(TimeSpan.FromSeconds(5));

            using HttpResponseMessage response = await httpClient.GetAsync(CustomAnnouncementEndpoint, source.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(source.Token).ConfigureAwait(false);
            CustomAnnouncementResponse? customResponse = await JsonSerializer.DeserializeAsync<CustomAnnouncementResponse>(stream, cancellationToken: source.Token).ConfigureAwait(false);
            if (customResponse is not { ReturnCode: 0, Data: { Count: > 0 } data })
            {
                return [];
            }

            return [.. data];
        }
        catch (OperationCanceledException)
        {
            return [];
        }
        catch
        {
            return [];
        }
    }

    private sealed class CustomAnnouncementResponse
    {
        [JsonPropertyName("retcode")]
        public int ReturnCode { get; set; }

        [JsonPropertyName("data")]
        public List<HutaoAnnouncement>? Data { get; set; }
    }

    private void DismissAnnouncement(HutaoAnnouncement? announcement)
    {
        if (announcement is not null && announcements is not null)
        {
            ApplicationDataCompositeValue excludedIds = LocalSetting.Get<ApplicationDataCompositeValue>(SettingKeys.ExcludedAnnouncementIds, []);
            DateTimeOffset minTime = DateTimeOffset.UtcNow - TimeSpan.FromDays(AnnouncementDuration);

            foreach ((string key, object value) in excludedIds)
            {
                if (value is DateTimeOffset time && time < minTime)
                {
                    excludedIds.Remove(key);
                }
            }

            excludedIds.TryAdd($"{announcement.Id}", DateTimeOffset.UtcNow + TimeSpan.FromDays(AnnouncementDuration));
            LocalSetting.Set(SettingKeys.ExcludedAnnouncementIds, excludedIds);

            announcements.Remove(announcement);
        }
    }
}
