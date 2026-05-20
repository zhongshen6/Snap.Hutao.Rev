// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Primitive;
using System.Collections.Immutable;

namespace Snap.Hutao.Service.Metadata.ContextAbstraction;

/// <summary>
/// 周本 Boss 掉落材料「异梦转化」互通组（由 Combine 元数据解析）。
/// </summary>
internal interface IMetadataWeeklyBossMaterialInterchangeGroupsSource : IMetadataContext
{
    ImmutableArray<ImmutableArray<MaterialId>> WeeklyBossMaterialInterchangeGroups { get; set; }
}
