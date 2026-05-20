// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Intrinsic;
using Snap.Hutao.Model.Metadata;
using Snap.Hutao.Service.Metadata.ContextAbstraction;
using Snap.Hutao.ViewModel.Cultivation;

namespace Snap.Hutao.Service.Cultivation;

internal static class CultivationStatisticsSurplusMerge
{
    /// <summary>
    /// 元数据 <c>Combine.Type</c>：1 角色与武器培养素材（野怪等）、2 武器突破、3 角色天赋；与此三类对应的合成均支持 10% 暴击期望。
    /// </summary>
    private static bool CombineTypeSupportsSynthCritTenPercent(uint combineType)
    {
        return combineType is 1U or 2U or 3U;
    }

    public static void Apply(Dictionary<uint, StatisticsCultivateItem> items, ICultivationMetadataContext context, CultivationStatisticsMergeOptions options)
    {
        if (!options.MergeUpgradeMaterials || items.Count is 0)
        {
            return;
        }

        bool talentCrit = options.TalentSynthCritTenPercent;

        List<Combine> eligibleCombines = [];
        foreach (Combine combine in context.ResultMaterialIdCombineMap.Values)
        {
            if (combine.RecipeType is not RecipeType.RECIPE_TYPE_COMBINE)
            {
                continue;
            }

            if (combine.Materials.Length is not 1 || combine.Materials[0].Count is not 3 || combine.Result.Count is not 1)
            {
                continue;
            }

            eligibleCombines.Add(combine);
        }

        Dictionary<uint, double> virtualAmount = new(items.Count);
        foreach ((uint id, StatisticsCultivateItem item) in items)
        {
            virtualAmount[id] = item.Current;
        }

        foreach (IGrouping<uint, Combine> group in eligibleCombines.GroupBy(static c => c.SubType))
        {
            List<Combine> groupCombines = [.. group];
            HashSet<uint> ids = [];
            foreach (Combine combine in groupCombines)
            {
                _ = ids.Add(combine.Result.Id);
                _ = ids.Add(combine.Materials[0].Id);
            }

            uint[] sortedIds = [.. ids.OrderBy(id => GetRankLevel(context, id)).ThenBy(id => id)];

            foreach (uint id in sortedIds)
            {
                Combine? upward = FindUpwardCombine(groupCombines, id);
                if (upward is null)
                {
                    continue;
                }

                if (!virtualAmount.TryGetValue(id, out double virt))
                {
                    continue;
                }

                uint need = items.TryGetValue(id, out StatisticsCultivateItem? row) ? row.Count : 0U;
                double surplus = Math.Max(0D, virt - need);
                long crafts = (long)(surplus / 3D);
                if (crafts <= 0L)
                {
                    continue;
                }

                double multiplier = talentCrit && CombineTypeSupportsSynthCritTenPercent(upward.Type) ? 1.1D : 1D;
                double produced = crafts * multiplier;
                uint resultId = upward.Result.Id;

                virtualAmount[id] = virt - (crafts * 3L);

                if (!virtualAmount.TryGetValue(resultId, out double atResult))
                {
                    atResult = items.TryGetValue(resultId, out StatisticsCultivateItem? r) ? r.Current : 0D;
                }

                virtualAmount[resultId] = atResult + produced;
            }
        }

        foreach ((uint id, StatisticsCultivateItem item) in items)
        {
            if (virtualAmount.TryGetValue(id, out double v))
            {
                item.MergeAdjustedCurrent = (uint)Math.Clamp(Math.Floor(v + 1e-6), 0D, uint.MaxValue);
            }
        }
    }

    private static Combine? FindUpwardCombine(List<Combine> groupCombines, uint ingredientId)
    {
        foreach (Combine combine in groupCombines)
        {
            if (combine.Materials is [{ Id: var mid, Count: 3 }] && mid == ingredientId)
            {
                return combine;
            }
        }

        return null;
    }

    private static QualityType GetRankLevel(ICultivationMetadataContext context, uint materialId)
    {
        return context.GetMaterial(materialId).RankLevel;
    }
}
