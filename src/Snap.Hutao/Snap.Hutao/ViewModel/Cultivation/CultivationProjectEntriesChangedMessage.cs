// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

namespace Snap.Hutao.ViewModel.Cultivation;

/// <summary>
/// 当前养成计划的条目在 UI 外被批量变更（例如从「我的角色」同步）后，通知养成页刷新列表与统计。
/// </summary>
internal sealed class CultivationProjectEntriesChangedMessage
{
    public static readonly CultivationProjectEntriesChangedMessage Empty = new();

    private CultivationProjectEntriesChangedMessage()
    {
    }
}
