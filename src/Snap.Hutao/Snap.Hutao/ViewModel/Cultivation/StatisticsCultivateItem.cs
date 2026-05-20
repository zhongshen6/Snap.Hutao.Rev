// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Metadata.Item;
using System.Collections.Immutable;

namespace Snap.Hutao.ViewModel.Cultivation;

internal sealed class StatisticsCultivateItem
{
    private readonly TimeSpan offset;

    private StatisticsCultivateItem(Material inner, TimeSpan offset)
    {
        Inner = inner;
        this.offset = offset;
        ExcludedFromPresentation = true;
    }

    private StatisticsCultivateItem(Material inner, Model.Entity.CultivateItem entity, TimeSpan offset)
    {
        Inner = inner;
        Count = entity.Count;
        this.offset = offset;
    }

    public Material Inner { get; }

    public uint Count { get; set; }

    public uint Current { get; set; }

    /// <summary>
    /// 升级材料合并后的展示用持有量；未启用合并时为 <see langword="null"/>。
    /// </summary>
    public uint? MergeAdjustedCurrent { get; set; }

    /// <summary>
    /// 周本材料异梦转化池内调配后的展示用持有量；未启用时为 <see langword="null"/>。在 <see cref="MergeAdjustedCurrent"/> 之后应用。
    /// </summary>
    public uint? WeeklyBossInterchangeAdjustedCurrent { get; set; }

    public uint DisplayCurrent { get => WeeklyBossInterchangeAdjustedCurrent ?? MergeAdjustedCurrent ?? Current; }

    public bool IsFinished { get => DisplayCurrent >= Count; }

    public string FormattedCount { get => $"{DisplayCurrent}/{Count}"; }

    private bool HasStatisticsAdjustedDisplay { get => MergeAdjustedCurrent.HasValue || WeeklyBossInterchangeAdjustedCurrent.HasValue; }

    /// <summary>未启用合并展示链时，使用紧凑 <see cref="FormattedCount"/>。</summary>
    public bool ShowNonMergeCompactCount { get => !HasStatisticsAdjustedDisplay; }

    /// <summary>已合并但合成前后有效持有量与背包数一致，不显示括号，仅「合并后 / 需求」加空格。</summary>
    public bool ShowMergeSpacedWithoutParen { get => HasStatisticsAdjustedDisplay && DisplayCurrent == Current; }

    /// <summary>合并后有效持有与背包原数不同，显示「合并后 (背包原数)」。</summary>
    public bool ShowMergeInventoryParen { get => HasStatisticsAdjustedDisplay && DisplayCurrent != Current; }

    /// <summary>首位合并显示量 &gt; 背包原数时着红色（相对原库存变多）。</summary>
    public bool MergeDisplayLeadUseRed { get => HasStatisticsAdjustedDisplay && DisplayCurrent > Current; }

    /// <summary>首位合并显示量 &lt; 背包原数时着绿色（相对原库存变少，如低档被向上消耗）。</summary>
    public bool MergeDisplayLeadUseGreen { get => HasStatisticsAdjustedDisplay && DisplayCurrent < Current; }

    /// <summary>背包原数，紧接在合并后数字后，如 <c>(44)</c>。</summary>
    public string RawInventoryParenthetical { get => $"({Current})"; }

    /// <summary>有括号行：「/需求」紧接括号，如 <c>/55</c>。</summary>
    public string SlashCountSuffixForParen { get => $"/{Count}"; }

    /// <summary>无括号行：「 / 需求」含空格。</summary>
    public string SlashCountSuffix { get => $" / {Count}"; }

    public bool IsToday { get => Inner.IsItemOfToday(offset, true); }

    /// <summary>
    /// 材料统计右键浮层中按行展示的「未完成」养成条目（每人一行，名称前为角色/武器图标，需求量以括号标注）。
    /// </summary>
    public ImmutableArray<StatisticsConsumerMenuLine> StatisticsConsumerMenuLines { get; set; }

    internal bool ExcludedFromPresentation { get; set; }

    public static StatisticsCultivateItem Create(Material inner, TimeSpan offset)
    {
        return new(inner, offset);
    }

    public static StatisticsCultivateItem Create(Material inner, Model.Entity.CultivateItem entity, TimeSpan offset)
    {
        return new(inner, entity, offset);
    }
}