// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Service.Cultivation.Consumption;
using Snap.Hutao.Web.Hoyolab.Takumi.Event.Calculate;

namespace Snap.Hutao.UI.Xaml.View.Dialog;

internal sealed class CultivatePromotionDeltaOptions
{
    public CultivatePromotionDeltaOptions(AvatarPromotionDelta delta, ConsumptionSaveStrategyKind strategy, bool clearAvatarAndWeaponEntriesBeforeSync = false, bool syncInventoryItems = false, bool syncCharacterInfo = false)
    {
        delta.AvatarLevelTarget = delta.AvatarLevelTarget switch
        {
            >= 100 => 100,
            >= 95 => 95,
            _ => delta.AvatarLevelTarget
        };

        Delta = delta;
        Strategy = strategy;
        ClearAvatarAndWeaponEntriesBeforeSync = clearAvatarAndWeaponEntriesBeforeSync;
        SyncInventoryItems = syncInventoryItems;
        SyncCharacterInfo = syncCharacterInfo;
    }

    public AvatarPromotionDelta Delta { get; }

    public ConsumptionSaveStrategyKind Strategy { get; }

    /// <summary>
    /// 批量同步前是否清空当前计划中已有的角色与武器养成条目（不含家具等其它类型）。
    /// </summary>
    public bool ClearAvatarAndWeaponEntriesBeforeSync { get; }

    public bool SyncInventoryItems { get; }

    public bool SyncCharacterInfo { get; }
}