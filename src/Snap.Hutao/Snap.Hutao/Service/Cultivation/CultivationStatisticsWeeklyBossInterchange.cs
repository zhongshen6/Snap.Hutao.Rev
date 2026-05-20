// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Primitive;
using Snap.Hutao.ViewModel.Cultivation;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Snap.Hutao.Service.Cultivation;

/// <summary>
/// 材料统计：同一周本 Boss 材料池内，将超出需求的虚拟持有量 1:1 调配给池内缺口（不计异梦溶媒消耗）。
/// </summary>
internal static class CultivationStatisticsWeeklyBossInterchange
{
    public static void Apply(
        Dictionary<uint, StatisticsCultivateItem> items,
        ImmutableArray<ImmutableArray<MaterialId>> interchangeGroups,
        bool enabled)
    {
        if (!enabled || interchangeGroups.IsDefault || interchangeGroups.IsEmpty)
        {
            return;
        }

        foreach (ImmutableArray<MaterialId> group in interchangeGroups)
        {
            ApplySingleGroup(items, group);
        }
    }

    private static void ApplySingleGroup(Dictionary<uint, StatisticsCultivateItem> items, ImmutableArray<MaterialId> group)
    {
        List<uint> poolIds = [];
        foreach (MaterialId mid in group)
        {
            uint id = mid;
            if (items.ContainsKey(id))
            {
                poolIds.Add(id);
            }
        }

        if (poolIds.Count < 2)
        {
            return;
        }

        Dictionary<uint, uint> virt = new(poolIds.Count);
        foreach (uint id in poolIds)
        {
            StatisticsCultivateItem it = items[id];
            virt[id] = it.MergeAdjustedCurrent ?? it.Current;
        }

        while (true)
        {
            uint? donor = null;
            uint maxSurplus = 0U;
            foreach (uint id in poolIds)
            {
                uint v = virt[id];
                uint need = items[id].Count;
                if (v > need && v - need > maxSurplus)
                {
                    maxSurplus = v - need;
                    donor = id;
                }
            }

            uint? receiver = null;
            uint maxDeficit = 0U;
            foreach (uint id in poolIds)
            {
                if (id == donor)
                {
                    continue;
                }

                uint v = virt[id];
                uint need = items[id].Count;
                if (v < need && need - v > maxDeficit)
                {
                    maxDeficit = need - v;
                    receiver = id;
                }
            }

            if (donor is null || receiver is null || maxSurplus is 0U || maxDeficit is 0U)
            {
                break;
            }

            virt[donor.Value]--;
            virt[receiver.Value]++;
        }

        foreach (uint id in poolIds)
        {
            StatisticsCultivateItem it = items[id];
            uint baseline = it.MergeAdjustedCurrent ?? it.Current;
            uint finalV = virt[id];
            if (finalV != baseline)
            {
                it.WeeklyBossInterchangeAdjustedCurrent = finalV;
            }
        }
    }
}
