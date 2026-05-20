// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Intrinsic;

namespace Snap.Hutao.ViewModel.Cultivation;

/// <summary>
/// 材料统计右键浮层中一行「未完成」养成条目：名称前展示角色/武器图标。
/// </summary>
internal sealed class StatisticsConsumerMenuLine
{
    private StatisticsConsumerMenuLine()
    {
    }

    public bool IsPlainMessage { get; private init; }

    public string? PlainMessage { get; private init; }

    public bool ShowRichRow => !IsPlainMessage;

    public Uri LeadingIcon { get; private init; } = default!;

    public QualityType LeadingQuality { get; private init; }

    public bool HasSecondIcon { get; private init; }

    public Uri SecondIcon { get; private init; } = default!;

    public QualityType SecondQuality { get; private init; }

    public string FirstName { get; private init; } = string.Empty;

    /// <summary>双名称行中间分隔（与武器关联条目的「角色·武器」展示一致，使用间隔号）。</summary>
    public string BetweenSeparator { get; private init; } = "\u00B7";

    public string? SecondName { get; private init; }

    /// <summary>计划需求量后缀，一般为全角括号包裹的数量 <c>（12）</c>。</summary>
    public string CountSuffix { get; private init; } = string.Empty;

    public static StatisticsConsumerMenuLine Plain(string message)
    {
        return new()
        {
            IsPlainMessage = true,
            PlainMessage = message,
        };
    }

    public static StatisticsConsumerMenuLine SingleIcon(Uri icon, QualityType quality, string name, string countSuffix)
    {
        return new()
        {
            LeadingIcon = icon,
            LeadingQuality = quality,
            FirstName = name,
            CountSuffix = countSuffix,
            HasSecondIcon = false,
            SecondIcon = default!,
            SecondQuality = QualityType.QUALITY_NONE,
        };
    }

    public static StatisticsConsumerMenuLine AvatarAndWeapon(
        Uri avatarIcon,
        QualityType avatarQuality,
        string avatarName,
        Uri weaponIcon,
        QualityType weaponQuality,
        string weaponName,
        string countSuffix)
    {
        return new()
        {
            LeadingIcon = avatarIcon,
            LeadingQuality = avatarQuality,
            FirstName = avatarName,
            HasSecondIcon = true,
            SecondIcon = weaponIcon,
            SecondQuality = weaponQuality,
            SecondName = weaponName,
            CountSuffix = countSuffix,
        };
    }
}
