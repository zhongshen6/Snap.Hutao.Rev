// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Core.Database;
using Snap.Hutao.Core.Text.Json;
using Snap.Hutao.Model;
using Snap.Hutao.Model.Cultivation;
using Snap.Hutao.Model.Entity;
using Snap.Hutao.Model.Entity.Primitive;
using Snap.Hutao.Model.Intrinsic;
using Snap.Hutao.Model.Metadata;
using Snap.Hutao.Model.Metadata.Item;
using Snap.Hutao.Model.Primitive;
using Snap.Hutao.Service.Abstraction;
using Snap.Hutao.Service.Cultivation.Consumption;
using Snap.Hutao.Service.Inventory;
using Snap.Hutao.Service.Metadata.ContextAbstraction;
using Snap.Hutao.ViewModel.Cultivation;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using ModelItem = Snap.Hutao.Model.Item;

namespace Snap.Hutao.Service.Cultivation;

[Service(ServiceLifetime.Singleton, typeof(ICultivationService))]
internal sealed partial class CultivationService : ICultivationService
{
    private readonly ConcurrentDictionary<Guid, ObservableCollection<CultivateEntryView>> entryCollectionCache = [];
    private readonly AsyncLock entryCollectionLock = new();
    private readonly AsyncLock projectsLock = new();

    private readonly ICultivationResinStatisticsService cultivationResinStatisticsService;
    private readonly ICultivationRepository cultivationRepository;
    private readonly IInventoryRepository inventoryRepository;
    private readonly IServiceProvider serviceProvider;
    private readonly ITaskContext taskContext;

    [GeneratedConstructor]
    public partial CultivationService(IServiceProvider serviceProvider);

    private AdvancedDbCollectionView<CultivateProject>? projects;

    public async ValueTask<IAdvancedDbCollectionView<CultivateProject>> GetProjectCollectionAsync()
    {
        using (await projectsLock.LockAsync().ConfigureAwait(false))
        {
            return projects ??= new(cultivationRepository.GetCultivateProjectCollection(), serviceProvider);
        }
    }

    public async ValueTask<ObservableCollection<CultivateEntryView>> GetCultivateEntryCollectionAsync(CultivateProject cultivateProject, ICultivationMetadataContext context)
    {
        using (await entryCollectionLock.LockAsync().ConfigureAwait(false))
        {
            if (entryCollectionCache.TryGetValue(cultivateProject.InnerId, out ObservableCollection<CultivateEntryView>? collection))
            {
                return collection;
            }

            await taskContext.SwitchToBackgroundAsync();
            return SynchronizedGetCultivateEntryCollection(cultivateProject, context);
        }

        ObservableCollection<CultivateEntryView> SynchronizedGetCultivateEntryCollection(CultivateProject cultivateProject, ICultivationMetadataContext context)
        {
            ImmutableArray<CultivateEntry> entries = cultivationRepository.GetCultivateEntryImmutableArrayIncludingLevelInformationByProjectId(cultivateProject.InnerId);

            Dictionary<Guid, CultivateEntry> entryByInnerId = new(entries.Length);
            foreach (ref readonly CultivateEntry entry in entries.AsSpan())
            {
                entryByInnerId[entry.InnerId] = entry;
            }

            List<CultivateEntryView> resultEntries = new(entries.Length);
            foreach (ref readonly CultivateEntry entry in entries.AsSpan())
            {
                ImmutableArray<CultivateItem> items = cultivationRepository.GetCultivateItemImmutableArrayByEntryId(entry.InnerId);
                if (IsHiddenAssociationOnlyAvatarEntry(entry, items.Length))
                {
                    continue;
                }

                ImmutableArray<CultivateItemView>.Builder entryItems = ImmutableArray.CreateBuilder<CultivateItemView>(items.Length);

                foreach (ref readonly CultivateItem cultivateItem in items.AsSpan())
                {
                    entryItems.Add(CultivateItemView.Create(cultivateItem, context.GetMaterial(cultivateItem.ItemId), cultivateProject.ServerTimeZoneOffset));
                }

                ModelItem item = entry.Type switch
                {
                    CultivateType.AvatarAndSkill => context.GetAvatarItem(entry.Id),
                    CultivateType.Weapon => context.GetWeaponItem(entry.Id),

                    // TODO: support furniture calc
                    _ => default!,
                };

                string? relatedAvatarName = null;
                if (entry.Type is CultivateType.Weapon && entry.RelatedEntryId is Guid relatedId && entryByInnerId.TryGetValue(relatedId, out CultivateEntry? relatedEntry) && relatedEntry.Type is CultivateType.AvatarAndSkill)
                {
                    relatedAvatarName = context.GetAvatarItem(relatedEntry.Id).Name;
                }

                resultEntries.Add(CultivateEntryView.Create(entry, item, entryItems.ToImmutable(), relatedAvatarName));
            }

            ObservableCollection<CultivateEntryView> result = resultEntries.SortByDescending(e => e.IsToday).ToObservableCollection();
            entryCollectionCache.TryAdd(cultivateProject.InnerId, result);
            return result;
        }
    }

    public async ValueTask<StatisticsCultivateItemCollection> GetStatisticsCultivateItemCollectionAsync(CultivateProject cultivateProject, ICultivationMetadataContext context, CultivationStatisticsMergeOptions mergeOptions, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await taskContext.SwitchToBackgroundAsync();
        token.ThrowIfCancellationRequested();
        return SynchronizedGetStatisticsCultivateItemCollection(cultivateProject, context, mergeOptions, token);

        StatisticsCultivateItemCollection SynchronizedGetStatisticsCultivateItemCollection(CultivateProject cultivateProject, ICultivationMetadataContext context, CultivationStatisticsMergeOptions mergeOptions, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Dictionary</* ItemId */ uint, StatisticsCultivateItem> resultItems = [];
            Guid projectId = cultivateProject.InnerId;
            Dictionary<uint, uint> inventoryCounts = [];

            foreach (ref readonly CultivateEntry entry in cultivationRepository.GetCultivateEntryImmutableArrayByProjectId(projectId).AsSpan())
            {
                token.ThrowIfCancellationRequested();
                foreach (ref readonly CultivateItem item in cultivationRepository.GetCultivateItemImmutableArrayByEntryId(entry.InnerId).AsSpan())
                {
                    token.ThrowIfCancellationRequested();
                    ref StatisticsCultivateItem? existedItem = ref CollectionsMarshal.GetValueRefOrAddDefault(resultItems, item.ItemId, out _);
                    if (existedItem is null || existedItem.ExcludedFromPresentation)
                    {
                        existedItem = StatisticsCultivateItem.Create(context.GetMaterial(item.ItemId), item, cultivateProject.ServerTimeZoneOffset);
                    }
                    else
                    {
                        existedItem.Count += item.Count;
                    }

                    RecursiveAddMaterialIngredientsByMaterialId(cultivateProject, context, resultItems, item.ItemId, token);
                }
            }

            foreach (ref readonly InventoryItem inventoryItem in inventoryRepository.GetInventoryItemImmutableArrayByProjectId(projectId).AsSpan())
            {
                token.ThrowIfCancellationRequested();
                inventoryCounts[inventoryItem.ItemId] = inventoryItem.Count;
                ref StatisticsCultivateItem existedItem = ref CollectionsMarshal.GetValueRefOrNullRef(resultItems, inventoryItem.ItemId);
                if (!Unsafe.IsNullRef(in existedItem))
                {
                    existedItem.Current = inventoryItem.Count;
                }
            }

            AddWeeklyBossGroupInventoryDonors(
                resultItems,
                inventoryCounts,
                context,
                cultivateProject.ServerTimeZoneOffset,
                mergeOptions.WeeklyBossMaterialInterchange);

            CultivationStatisticsSurplusMerge.Apply(resultItems, context, mergeOptions);
            CultivationStatisticsWeeklyBossInterchange.Apply(
                resultItems,
                context.WeeklyBossMaterialInterchangeGroups,
                mergeOptions.WeeklyBossMaterialInterchange);
            ApplyStatisticsConsumerMenuLines(resultItems, projectId, context, cultivationRepository, token);

            return new(resultItems);
        }
    }

    private static void AddWeeklyBossGroupInventoryDonors(
        Dictionary<uint, StatisticsCultivateItem> items,
        Dictionary<uint, uint> inventoryCounts,
        ICultivationMetadataContext context,
        TimeSpan offset,
        bool enabled)
    {
        if (!enabled || context.WeeklyBossMaterialInterchangeGroups.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (ImmutableArray<MaterialId> group in context.WeeklyBossMaterialInterchangeGroups)
        {
            bool anyPlannedInGroup = false;
            foreach (MaterialId mid in group)
            {
                if (items.ContainsKey(mid))
                {
                    anyPlannedInGroup = true;
                    break;
                }
            }

            if (!anyPlannedInGroup)
            {
                continue;
            }

            foreach (MaterialId mid in group)
            {
                uint id = mid;
                if (items.ContainsKey(id))
                {
                    continue;
                }

                if (!inventoryCounts.TryGetValue(id, out uint inv) || inv is 0U)
                {
                    continue;
                }

                StatisticsCultivateItem donor = StatisticsCultivateItem.Create(context.GetMaterial(id), offset);
                donor.Current = inv;
                items[id] = donor;
            }
        }
    }

    public ValueTask<ResinStatistics> GetResinStatisticsAsync(StatisticsCultivateItemCollection statisticsCultivateItems, CancellationToken token)
    {
        return cultivationResinStatisticsService.GetResinStatisticsAsync(statisticsCultivateItems, token);
    }

    public async ValueTask RemoveCultivateEntryAsync(Guid entryId)
    {
        await taskContext.SwitchToBackgroundAsync();
        cultivationRepository.RemoveCultivateEntryById(entryId);
    }

    public async ValueTask RemoveAvatarAndWeaponEntriesForCurrentProjectAsync()
    {
        IAdvancedDbCollectionView<CultivateProject> projects = await GetProjectCollectionAsync().ConfigureAwait(false);
        if (!await EnsureCurrentProjectAsync(projects).ConfigureAwait(false))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(projects.CurrentItem);
        Guid projectId = projects.CurrentItem.InnerId;

        await taskContext.SwitchToBackgroundAsync();

        ImmutableArray<CultivateEntry> entries = cultivationRepository.GetCultivateEntryImmutableArrayByProjectId(projectId);
        List<Guid> weaponEntryIds = new(entries.Length);
        List<Guid> avatarEntryIds = new(entries.Length);
        foreach (ref readonly CultivateEntry entry in entries.AsSpan())
        {
            switch (entry.Type)
            {
                case CultivateType.Weapon:
                    weaponEntryIds.Add(entry.InnerId);
                    break;
                case CultivateType.AvatarAndSkill:
                    avatarEntryIds.Add(entry.InnerId);
                    break;
            }
        }

        foreach (Guid id in weaponEntryIds)
        {
            cultivationRepository.RemoveCultivateEntryById(id);
        }

        foreach (Guid id in avatarEntryIds)
        {
            cultivationRepository.RemoveCultivateEntryById(id);
        }

        entryCollectionCache.TryRemove(projectId, out _);
    }

    public void SaveCultivateItem(CultivateItemView item)
    {
        cultivationRepository.UpdateCultivateItem(item.Entity);
    }

    public async ValueTask<ConsumptionSaveResult> SaveConsumptionAsync(InputConsumption inputConsumption)
    {
        // No selected project
        IAdvancedDbCollectionView<CultivateProject> projects = await GetProjectCollectionAsync().ConfigureAwait(false);
        if (!await EnsureCurrentProjectAsync(projects).ConfigureAwait(false))
        {
            return new(ConsumptionSaveResultKind.NoProject);
        }

        ArgumentNullException.ThrowIfNull(projects.CurrentItem);
        Guid projectId = projects.CurrentItem.InnerId;

        await taskContext.SwitchToBackgroundAsync();

        // PreserveExisting or CreateNewEntry, but no item
        if (inputConsumption is { Strategy: not ConsumptionSaveStrategyKind.OverwriteExisting, Items: [] })
        {
            return new(ConsumptionSaveResultKind.NoItem);
        }

        // PreserveExisting or OverwriteExisting
        if (inputConsumption.Strategy is not ConsumptionSaveStrategyKind.CreateNewEntry)
        {
            // Check for existing entries
            ImmutableArray<CultivateEntry> entries = cultivationRepository.GetCultivateEntryImmutableArrayByProjectIdAndItemId(projectId, inputConsumption.ItemId);

            if (entries.Length > 0)
            {
                if (inputConsumption.Strategy is ConsumptionSaveStrategyKind.PreserveExisting)
                {
                    return new(ConsumptionSaveResultKind.Skipped);
                }

                if (inputConsumption.Strategy is ConsumptionSaveStrategyKind.OverwriteExisting)
                {
                    foreach (CultivateEntry entry in entries)
                    {
                        cultivationRepository.RemoveLevelInformationByEntryId(entry.InnerId);
                        cultivationRepository.RemoveCultivateItemRangeByEntryId(entry.InnerId);
                        cultivationRepository.RemoveCultivateEntryById(entry.InnerId);
                    }

                    if (inputConsumption.Items is [])
                    {
                        entryCollectionCache.TryRemove(projectId, out _);
                        return new(ConsumptionSaveResultKind.Removed);
                    }
                }
            }
            else
            {
                if (inputConsumption.Items is [])
                {
                    return new(ConsumptionSaveResultKind.NoItem);
                }
            }
        }

        {
            CultivateEntry entry = CultivateEntry.From(projectId, inputConsumption.Type, inputConsumption.ItemId);
            entry.RelatedEntryId = inputConsumption.RelatedEntryId;
            cultivationRepository.AddCultivateEntry(entry);

            CultivateEntryLevelInformation entryLevelInformation = CultivateEntryLevelInformation.From(entry.InnerId, inputConsumption.Type, inputConsumption.LevelInformation);
            cultivationRepository.AddLevelInformation(entryLevelInformation);

            IEnumerable<CultivateItem> toAdd = inputConsumption.Items.Select(item => CultivateItem.From(entry.InnerId, item));
            cultivationRepository.AddCultivateItemRange(toAdd);

            // The consumption save operation is always performed outside cultivation page
            // and without touching the cache. So we have to invalidate the cache manually.
            entryCollectionCache.TryRemove(projectId, out _);

            return new(ConsumptionSaveResultKind.Added, entry.InnerId);
        }
    }

    public async ValueTask<Guid?> TryGetAvatarCultivateEntryInnerIdAsync(uint avatarId)
    {
        IAdvancedDbCollectionView<CultivateProject> projects = await GetProjectCollectionAsync().ConfigureAwait(false);
        if (!await EnsureCurrentProjectAsync(projects).ConfigureAwait(false))
        {
            return null;
        }

        ArgumentNullException.ThrowIfNull(projects.CurrentItem);

        await taskContext.SwitchToBackgroundAsync();
        return cultivationRepository.TryGetAvatarCultivateEntryInnerId(projects.CurrentItem.InnerId, avatarId);
    }

    public async ValueTask<Guid?> EnsureAvatarAssociationStubAsync(uint avatarId, LevelInformation levelInformation)
    {
        IAdvancedDbCollectionView<CultivateProject> projects = await GetProjectCollectionAsync().ConfigureAwait(false);
        if (!await EnsureCurrentProjectAsync(projects).ConfigureAwait(false))
        {
            return null;
        }

        ArgumentNullException.ThrowIfNull(projects.CurrentItem);

        await taskContext.SwitchToBackgroundAsync();

        Guid projectId = projects.CurrentItem.InnerId;
        if (cultivationRepository.TryGetAvatarCultivateEntryInnerId(projectId, avatarId) is Guid existing)
        {
            return existing;
        }

        CultivateEntry entry = CultivateEntry.From(projectId, CultivateType.AvatarAndSkill, avatarId);
        cultivationRepository.AddCultivateEntry(entry);

        CultivateEntryLevelInformation level = CultivateEntryLevelInformation.From(entry.InnerId, CultivateType.AvatarAndSkill, levelInformation);
        cultivationRepository.AddLevelInformation(level);

        entryCollectionCache.TryRemove(projectId, out _);
        return entry.InnerId;
    }

    public async ValueTask<ProjectAddResultKind> TryAddProjectAsync(CultivateProject project)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
        {
            return ProjectAddResultKind.InvalidName;
        }

        ArgumentNullException.ThrowIfNull(projects);

        if (projects.Source.Any(a => a.Name == project.Name))
        {
            return ProjectAddResultKind.AlreadyExists;
        }

        await taskContext.SwitchToMainThreadAsync();
        projects.Add(project);
        projects.MoveCurrentTo(project);

        return ProjectAddResultKind.Added;
    }

    public async ValueTask<CultivateProjectAvatarPropertyBatchPreferences?> GetAvatarPropertyBatchCultivatePreferencesForCurrentProjectAsync()
    {
        IAdvancedDbCollectionView<CultivateProject> projects = await GetProjectCollectionAsync().ConfigureAwait(false);
        if (!await EnsureCurrentProjectAsync(projects).ConfigureAwait(false))
        {
            return null;
        }

        string? json = projects.CurrentItem?.AvatarPropertyBatchCultivatePreferencesJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CultivateProjectAvatarPropertyBatchPreferences>(json, JsonOptions.Default);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async ValueTask SaveAvatarPropertyBatchCultivatePreferencesForCurrentProjectAsync(CultivateProjectAvatarPropertyBatchPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        IAdvancedDbCollectionView<CultivateProject> projects = await GetProjectCollectionAsync().ConfigureAwait(false);
        if (!await EnsureCurrentProjectAsync(projects).ConfigureAwait(false))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(projects.CurrentItem);

        CultivateProject project = projects.CurrentItem;

        await taskContext.SwitchToBackgroundAsync();

        project.AvatarPropertyBatchCultivatePreferencesJson = JsonSerializer.Serialize(preferences, JsonOptions.Default);
        cultivationRepository.Update(project);
    }

    public async ValueTask RemoveProjectAsync(CultivateProject project)
    {
        ArgumentNullException.ThrowIfNull(projects);

        // Keep this on main thread.
        await taskContext.SwitchToMainThreadAsync();
        projects.Remove(project);

        await taskContext.SwitchToBackgroundAsync();
        entryCollectionCache.TryRemove(project.InnerId, out _);
        cultivationRepository.RemoveCultivateProjectById(project.InnerId);
    }

    public async ValueTask<bool> EnsureCurrentProjectAsync(IAdvancedDbCollectionView<CultivateProject> projects)
    {
        if (projects.CurrentItem is null)
        {
            try
            {
                await taskContext.SwitchToMainThreadAsync();
                projects.MoveCurrentTo(projects.Source.SelectedOrFirstOrDefault());
            }
            catch (InvalidOperationException)
            {
                // Sequence contains more than one matching element
            }

            if (projects.CurrentItem is null)
            {
                return false;
            }
        }

        return true;
    }

    private static void RecursiveAddMaterialIngredientsByMaterialId(CultivateProject cultivateProject, ICultivationMetadataContext context, Dictionary<uint, StatisticsCultivateItem> resultItems, MaterialId materialId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        if (materialId == 104003U)
        {
            foreach (ref readonly MaterialId xpBookId in (ReadOnlySpan<MaterialId>)[104001U, 104002U])
            {
                ref StatisticsCultivateItem? bookItem = ref CollectionsMarshal.GetValueRefOrAddDefault(resultItems, xpBookId, out _);
                bookItem ??= StatisticsCultivateItem.Create(context.GetMaterial(xpBookId), cultivateProject.ServerTimeZoneOffset);
            }

            return;
        }

        if (context.ResultMaterialIdCombineMap.TryGetValue(materialId, out Combine? combine) && combine.RecipeType is RecipeType.RECIPE_TYPE_COMBINE)
        {
            foreach (ref readonly IdCount ingredient in combine.Materials.AsSpan())
            {
                token.ThrowIfCancellationRequested();
                ref StatisticsCultivateItem? ingredientItem = ref CollectionsMarshal.GetValueRefOrAddDefault(resultItems, ingredient.Id, out _);
                ingredientItem ??= StatisticsCultivateItem.Create(context.GetMaterial(ingredient.Id), cultivateProject.ServerTimeZoneOffset);
                RecursiveAddMaterialIngredientsByMaterialId(cultivateProject, context, resultItems, ingredient.Id, token);
            }
        }
    }

    private static void ApplyStatisticsConsumerMenuLines(
        Dictionary<uint, StatisticsCultivateItem> resultItems,
        Guid projectId,
        ICultivationMetadataContext context,
        ICultivationRepository cultivationRepository,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ImmutableArray<(CultivateEntry Entry, CultivateItem Item)> pairs = cultivationRepository.GetCultivateEntryItemPairsByProjectId(projectId);
        ImmutableArray<CultivateEntry> projectEntries = cultivationRepository.GetCultivateEntryImmutableArrayByProjectId(projectId);
        Dictionary<Guid, CultivateEntry> entryByInnerId = new(projectEntries.Length);
        foreach (ref readonly CultivateEntry e in projectEntries.AsSpan())
        {
            entryByInnerId[e.InnerId] = e;
        }

        Dictionary<uint, List<(string SortKey, StatisticsConsumerMenuLine Line)>> unfinishedRowsByMaterial = [];

        foreach ((CultivateEntry entry, CultivateItem item) in pairs.AsSpan())
        {
            token.ThrowIfCancellationRequested();
            if (item.IsFinished)
            {
                continue;
            }

            string sortKey = $"{FormatStatisticsConsumerEntryName(entry, context, entryByInnerId)}×{item.Count}";
            StatisticsConsumerMenuLine line = CreateStatisticsConsumerMenuLine(entry, item, context, entryByInnerId);
            ref List<(string SortKey, StatisticsConsumerMenuLine Line)>? list = ref CollectionsMarshal.GetValueRefOrAddDefault(unfinishedRowsByMaterial, item.ItemId, out _);
            list ??= [];
            list.Add((sortKey, line));
        }

        foreach ((uint materialId, StatisticsCultivateItem stat) in resultItems)
        {
            token.ThrowIfCancellationRequested();
            if (MaterialIds.IsExcludedFromStatisticsConsumerMenu(materialId))
            {
                stat.StatisticsConsumerMenuLines = ImmutableArray.Create(StatisticsConsumerMenuLine.Plain(SH.ViewPageCultivationStatisticsConsumerMenuExcluded));
                continue;
            }

            if (unfinishedRowsByMaterial.TryGetValue(materialId, out List<(string SortKey, StatisticsConsumerMenuLine Line)>? rows))
            {
                rows.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.SortKey, b.SortKey));
                stat.StatisticsConsumerMenuLines = ImmutableArray.CreateRange(rows.ConvertAll(static r => r.Line));
            }
            else
            {
                stat.StatisticsConsumerMenuLines = ImmutableArray.Create(StatisticsConsumerMenuLine.Plain(SH.ViewPageCultivationStatisticsUnfinishedConsumersEmptyList));
            }
        }
    }

    private static StatisticsConsumerMenuLine CreateStatisticsConsumerMenuLine(
        CultivateEntry entry,
        CultivateItem item,
        ICultivationMetadataContext context,
        Dictionary<Guid, CultivateEntry> entryByInnerId)
    {
        // 展示用量：全角括号比「×数量」更利落，且与中文混排更协调。
        string countSuffix = $"\uFF08{item.Count}\uFF09";

        switch (entry.Type)
        {
            case CultivateType.AvatarAndSkill:
            {
                ModelItem avatarItem = context.GetAvatarItem(entry.Id);
                return StatisticsConsumerMenuLine.SingleIcon(avatarItem.Icon, avatarItem.Quality, avatarItem.Name, countSuffix);
            }

            case CultivateType.Weapon:
            {
                ModelItem weaponItem = context.GetWeaponItem(entry.Id);
                if (entry.RelatedEntryId is Guid relatedId
                    && entryByInnerId.TryGetValue(relatedId, out CultivateEntry? related)
                    && related.Type is CultivateType.AvatarAndSkill)
                {
                    ModelItem avatarItem = context.GetAvatarItem(related.Id);
                    return StatisticsConsumerMenuLine.AvatarAndWeapon(
                        avatarItem.Icon,
                        avatarItem.Quality,
                        avatarItem.Name,
                        weaponItem.Icon,
                        weaponItem.Quality,
                        weaponItem.Name,
                        countSuffix);
                }

                return StatisticsConsumerMenuLine.SingleIcon(weaponItem.Icon, weaponItem.Quality, weaponItem.Name, countSuffix);
            }

            default:
                return StatisticsConsumerMenuLine.Plain($"{Material.Default.Name}{countSuffix}");
        }
    }

    private static string FormatStatisticsConsumerEntryName(
        CultivateEntry entry,
        ICultivationMetadataContext context,
        Dictionary<Guid, CultivateEntry> entryByInnerId)
    {
        return entry.Type switch
        {
            CultivateType.AvatarAndSkill => context.GetAvatarItem(entry.Id).Name,
            CultivateType.Weapon => FormatStatisticsConsumerWeaponEntryName(entry, context, entryByInnerId),
            _ => Material.Default.Name,
        };
    }

    /// <summary>
    /// 材料统计右键「未完成」：武器条目在有关联角色条目时展示「角色名·武器名」，否则（含历史无 RelatedEntryId）仅武器名。
    /// </summary>
    private static string FormatStatisticsConsumerWeaponEntryName(
        CultivateEntry entry,
        ICultivationMetadataContext context,
        Dictionary<Guid, CultivateEntry> entryByInnerId)
    {
        string weaponName = context.GetWeaponItem(entry.Id).Name;
        if (entry.RelatedEntryId is not Guid relatedId)
        {
            return weaponName;
        }

        if (!entryByInnerId.TryGetValue(relatedId, out CultivateEntry? related) || related.Type is not CultivateType.AvatarAndSkill)
        {
            return weaponName;
        }

        string avatarName = context.GetAvatarItem(related.Id).Name;
        return $"{avatarName}·{weaponName}";
    }

    /// <summary>
    /// 无材料的「已满配」角色占位行不在养成列表展示（仍为武器的 RelatedEntryId 解析目标）。
    /// </summary>
    private static bool IsHiddenAssociationOnlyAvatarEntry(CultivateEntry entry, int cultivateItemCount)
    {
        if (entry.Type is not CultivateType.AvatarAndSkill || cultivateItemCount != 0)
        {
            return false;
        }

        if (entry.LevelInformation is not CultivateEntryLevelInformation li)
        {
            return false;
        }

        return li.AvatarLevelFrom == li.AvatarLevelTo
            && li.SkillALevelFrom == li.SkillALevelTo
            && li.SkillELevelFrom == li.SkillELevelTo
            && li.SkillQLevelFrom == li.SkillQLevelTo;
    }
}
