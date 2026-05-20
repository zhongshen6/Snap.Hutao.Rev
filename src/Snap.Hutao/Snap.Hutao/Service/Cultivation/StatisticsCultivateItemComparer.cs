// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Primitive;
using Snap.Hutao.ViewModel.Cultivation;

namespace Snap.Hutao.Service.Cultivation;

/// <summary>
/// 材料统计默认顺序：与原先一致按 <see cref="MaterialIdComparer"/>，但当两项均为「角色培养素材」时先按 <see cref="Snap.Hutao.Model.Metadata.Item.Material.RankLevel"/> 再按物品 Id。
/// </summary>
internal sealed class StatisticsCultivateItemComparer : IComparer<StatisticsCultivateItem>
{
    private static readonly LazySlim<StatisticsCultivateItemComparer> LazyShared = new(() => new());

    private StatisticsCultivateItemComparer()
    {
    }

    public static StatisticsCultivateItemComparer Shared { get => LazyShared.Value; }

    public int Compare(StatisticsCultivateItem? x, StatisticsCultivateItem? y)
    {
        return (x, y) switch
        {
            (null, not null) => -1,
            (not null, null) => 1,
            (null, null) => 0,
            (not null, not null) => CompareCore(x, y),
        };
    }

    internal static int CompareCore(StatisticsCultivateItem x, StatisticsCultivateItem y)
    {
        string? tx = x.Inner.TypeDescription;
        string? ty = y.Inner.TypeDescription;
        if (IsCharacterLevelUpMaterial(tx) && IsCharacterLevelUpMaterial(ty))
        {
            int rank = x.Inner.RankLevel.CompareTo(y.Inner.RankLevel);
            if (rank is not 0)
            {
                return rank;
            }
        }

        return MaterialIdComparer.Shared.Compare(x.Inner.Id, y.Inner.Id);
    }

    private static bool IsCharacterLevelUpMaterial(string? typeDescription)
    {
        return typeDescription == SH.ModelMetadataMaterialCharacterLevelUpMaterial;
    }
}
