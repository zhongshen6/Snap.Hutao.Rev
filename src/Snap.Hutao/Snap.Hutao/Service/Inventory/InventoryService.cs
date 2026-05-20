// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Entity;
using Snap.Hutao.Model.InterChange.Inventory;
using Snap.Hutao.Model.Metadata.Item;
using Snap.Hutao.Service.Cultivation;
using Snap.Hutao.Service.Metadata.ContextAbstraction;
using Snap.Hutao.Service.Notification;
using Snap.Hutao.Service.User;
using Snap.Hutao.Service.Yae;
using Snap.Hutao.ViewModel.Cultivation;
using Snap.Hutao.ViewModel.Game;
using Snap.Hutao.Web.Hoyolab.Takumi.Event.Calculate;
using Snap.Hutao.Web.Response;
using System.Collections.Immutable;

namespace Snap.Hutao.Service.Inventory;

[Service(ServiceLifetime.Singleton, typeof(IInventoryService))]
internal sealed partial class InventoryService : IInventoryService
{
    private readonly PromotionDeltaFactory promotionDeltaFactory;
    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly IInventoryRepository inventoryRepository;
    private readonly ICultivationRepository cultivationRepository;
    private readonly IUserService userService;
    private readonly IMessenger messenger;

    [GeneratedConstructor]
    public partial InventoryService(IServiceProvider serviceProvider);

    public ImmutableArray<InventoryItemView> GetInventoryItemViews(ICultivationMetadataContext context, CultivateProject cultivateProject, ICommand saveCommand)
    {
        Guid projectId = cultivateProject.InnerId;
        ImmutableDictionary<uint, InventoryItem> entities = inventoryRepository.GetInventoryItemImmutableDictionaryByProjectId(projectId);

        ImmutableArray<InventoryItemView>.Builder results = ImmutableArray.CreateBuilder<InventoryItemView>();
        foreach (Material meta in context.EnumerateInventoryMaterial())
        {
            InventoryItem entity = entities.GetValueOrDefault(meta.Id) ?? InventoryItem.From(projectId, meta.Id);
            results.Add(new(entity, meta, saveCommand));
        }

        return results.ToImmutable();
    }

    public void SaveInventoryItem(InventoryItemView item)
    {
        inventoryRepository.UpdateInventoryItem(item.Entity);
    }

    public ValueTask RefreshInventoryAsync(RefreshOptions refreshOptions)
    {
        switch (refreshOptions.Kind)
        {
            case RefreshOptionKind.WebCalculator:
                ArgumentNullException.ThrowIfNull(refreshOptions.MetadataContext);
                return RefreshInventoryByCalculatorAsync(refreshOptions);
            case RefreshOptionKind.EmbeddedYae:
                ArgumentNullException.ThrowIfNull(refreshOptions.YaeService);
                ArgumentNullException.ThrowIfNull(refreshOptions.ViewModelSupportLaunchExecution);
                return RefreshInventoryByEmbeddedYaeAsync(refreshOptions.YaeService, refreshOptions.ViewModelSupportLaunchExecution, refreshOptions.Project);
        }

        return ValueTask.CompletedTask;
    }

    public void RemoveInventoryItems(CultivateProject cultivateProject)
    {
        Guid projectId = cultivateProject.InnerId;
        inventoryRepository.RemoveInventoryItemRangeByProjectId(projectId);
    }

    private async ValueTask RefreshInventoryByCalculatorAsync(RefreshOptions options)
    {
        ICultivationMetadataContext context = options.MetadataContext!;
        CultivateProject project = options.Project;
        bool syncToAllProjects = options.SyncCalculatorInventoryToAllProjects;

        if (await userService.GetCurrentUserAndUidAsync().ConfigureAwait(false) is not { } userAndUid)
        {
            messenger.Send(InfoBarMessage.Warning(SH.MustSelectUserAndUid));
            return;
        }

        ImmutableArray<AvatarPromotionDelta> deltas = await promotionDeltaFactory.GetAsync(context, userAndUid).ConfigureAwait(false);

        BatchConsumption? batchConsumption;
        using (IServiceScope scope = serviceScopeFactory.CreateScope())
        {
            CalculateClient calculateClient = scope.ServiceProvider.GetRequiredService<CalculateClient>();

            Response<BatchConsumption> resp = await calculateClient
                .BatchComputeAsync(userAndUid, deltas, true)
                .ConfigureAwait(false);

            if (!ResponseValidator.TryValidate(resp, scope.ServiceProvider, out batchConsumption))
            {
                return;
            }
        }

        if (batchConsumption is { OverallConsume: { IsDefault: false } items })
        {
            static IEnumerable<InventoryItem> ToInventoryItems(ImmutableArray<Item> consumeItems, Guid projectId)
            {
                static uint ToSafeCount(Item item)
                {
                    long delta = (long)item.Num - item.LackNum;
                    if (delta <= 0)
                    {
                        return 0U;
                    }

                    return delta >= uint.MaxValue ? uint.MaxValue : (uint)delta;
                }

                return consumeItems.SelectAsArray(static (item, pid) => InventoryItem.From(pid, item.Id, ToSafeCount(item)), projectId);
            }

            if (syncToAllProjects)
            {
                ImmutableArray<Guid> projectIds = cultivationRepository.GetCultivateProjectInnerIds();
                foreach (Guid projectId in projectIds.AsSpan())
                {
                    inventoryRepository.RemoveInventoryItemRangeByProjectId(projectId);
                    inventoryRepository.AddInventoryItemRangeByProjectId(ToInventoryItems(items, projectId));
                }
            }
            else
            {
                inventoryRepository.RemoveInventoryItemRangeByProjectId(project.InnerId);
                inventoryRepository.AddInventoryItemRangeByProjectId(ToInventoryItems(items, project.InnerId));
            }
        }
    }

    private async ValueTask RefreshInventoryByEmbeddedYaeAsync(IYaeService yaeService, IViewModelSupportLaunchExecution viewModel, CultivateProject project)
    {
        if (await yaeService.GetInventoryAsync(viewModel).ConfigureAwait(false) is not { } uiif)
        {
            messenger.Send(InfoBarMessage.Warning(SH.ServiceYaeEmbeddedYaeErrorTitle, SH.ServiceInventoryRefreshByEmbeddedYaeErrorMessage));
            return;
        }

        inventoryRepository.RemoveInventoryItemRangeByProjectId(project.InnerId);
        inventoryRepository.AddInventoryItemRangeByProjectId(UIIFItemToInventoryItem(project.InnerId, uiif.List));

        static IEnumerable<InventoryItem> UIIFItemToInventoryItem(Guid projectId, ImmutableArray<UIIFItem> uiif)
        {
            foreach (UIIFItem item in uiif)
            {
                if (item.Material is not null)
                {
                    yield return InventoryItem.From(projectId, item.ItemId, item.Material.Count);
                }
            }
        }
    }
}