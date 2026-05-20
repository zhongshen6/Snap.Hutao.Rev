// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Intrinsic;
using Snap.Hutao.Model.Metadata;
using Snap.Hutao.Model.Primitive;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Snap.Hutao.Service.Cultivation;

/// <summary>
/// 自 Combine 列表解析周本材料异梦转化互通组：配方为 Type=9、CONVERT、产物×1、材料为「异梦溶媒」+ 另一周本材料各×1。
/// </summary>
internal static class WeeklyBossMaterialInterchangeGroupsBuilder
{
    /// <summary>异梦溶媒 Id（转化消耗，统计虚拟调配时不扣溶媒，仅利用池内 1:1 等价）。</summary>
    private const uint DreamSolventMaterialId = 113021U;

    public static ImmutableArray<ImmutableArray<MaterialId>> Build(ImmutableArray<Combine> combines)
    {
        Dictionary<MaterialId, HashSet<MaterialId>> adjacency = [];

        foreach (Combine combine in combines)
        {
            if (combine.Type is not 9U)
            {
                continue;
            }

            if (combine.RecipeType is not RecipeType.RECIPE_TYPE_CONVERT)
            {
                continue;
            }

            if (combine.Materials.Length is not 2 || combine.Result.Count is not 1)
            {
                continue;
            }

            MaterialId resultId = combine.Result.Id;
            MaterialId? otherMaterial = null;
            foreach (ref readonly IdCount m in combine.Materials.AsSpan())
            {
                if (m.Id == DreamSolventMaterialId)
                {
                    continue;
                }

                if (m.Count is not 1)
                {
                    otherMaterial = null;
                    break;
                }

                otherMaterial = m.Id;
            }

            if (otherMaterial is null || otherMaterial.Value == resultId)
            {
                continue;
            }

            AddUndirectedEdge(adjacency, resultId, otherMaterial.Value);
        }

        return ToComponents(adjacency);
    }

    private static void AddUndirectedEdge(Dictionary<MaterialId, HashSet<MaterialId>> adjacency, MaterialId a, MaterialId b)
    {
        if (!adjacency.TryGetValue(a, out HashSet<MaterialId>? setA))
        {
            setA = [];
            adjacency[a] = setA;
        }

        if (!adjacency.TryGetValue(b, out HashSet<MaterialId>? setB))
        {
            setB = [];
            adjacency[b] = setB;
        }

        _ = setA.Add(b);
        _ = setB.Add(a);
    }

    private static ImmutableArray<ImmutableArray<MaterialId>> ToComponents(Dictionary<MaterialId, HashSet<MaterialId>> adjacency)
    {
        if (adjacency.Count is 0)
        {
            return ImmutableArray<ImmutableArray<MaterialId>>.Empty;
        }

        HashSet<MaterialId> visited = [];
        ImmutableArray<ImmutableArray<MaterialId>>.Builder groups = ImmutableArray.CreateBuilder<ImmutableArray<MaterialId>>();

        foreach (MaterialId start in adjacency.Keys)
        {
            if (visited.Contains(start))
            {
                continue;
            }

            List<MaterialId> component = [];
            Queue<MaterialId> queue = new();
            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                MaterialId id = queue.Dequeue();
                component.Add(id);

                if (!adjacency.TryGetValue(id, out HashSet<MaterialId>? neighbors))
                {
                    continue;
                }

                foreach (MaterialId n in neighbors)
                {
                    if (visited.Add(n))
                    {
                        queue.Enqueue(n);
                    }
                }
            }

            if (component.Count >= 2)
            {
                component.Sort(MaterialIdComparer.Shared);
                groups.Add(component.ToImmutableArray());
            }
        }

        return groups.ToImmutable();
    }
}
