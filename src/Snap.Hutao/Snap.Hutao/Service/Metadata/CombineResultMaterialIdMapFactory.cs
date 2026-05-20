// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Intrinsic;
using Snap.Hutao.Model.Metadata;
using Snap.Hutao.Model.Primitive;
using System.Collections.Immutable;

namespace Snap.Hutao.Service.Metadata;

/// <summary>
/// Combine 元数据中多条配方可共享同一产物 Id（例如元素断片既有 3×碎屑合成，也有用其他元素石与尘的转化）。
/// 构建 MaterialId→Combine 映射时必须优先保留「单材料×3→产物×1」的合成台主链，否则后项覆盖前项会导致养成统计无法向下展开原料。
/// </summary>
internal static class CombineResultMaterialIdMapFactory
{
    public static ImmutableDictionary<MaterialId, Combine> ToImmutableDictionary(ImmutableArray<Combine> combines)
    {
        Dictionary<MaterialId, Combine> map = [];

        foreach (Combine current in combines)
        {
            MaterialId key = current.Result.Id;
            if (!map.TryGetValue(key, out Combine? existing))
            {
                map[key] = current;
                continue;
            }

            if (ComparePreference(current, existing) > 0)
            {
                map[key] = current;
            }
        }

        return map.ToImmutableDictionary();
    }

    /// <summary>正数表示 <paramref name="incoming"/> 应取代已有项。</summary>
    private static int ComparePreference(Combine incoming, Combine existing)
    {
        return Score(incoming).CompareTo(Score(existing));
    }

    private static int Score(Combine c)
    {
        if (c.Materials.Length is 1 && c.Materials[0].Count is 3 && c.Result.Count is 1)
        {
            return c.RecipeType switch
            {
                RecipeType.RECIPE_TYPE_COMBINE => 300,
                RecipeType.RECIPE_TYPE_CONVERT => 200,
                _ => 150,
            };
        }

        if (c.RecipeType is RecipeType.RECIPE_TYPE_COMBINE)
        {
            return 100;
        }

        return 0;
    }
}
