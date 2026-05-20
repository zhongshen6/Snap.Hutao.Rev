// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.UI.Xaml.Controls;
using Snap.Hutao.Core.Setting;
using Snap.Hutao.Factory.ContentDialog;
using Snap.Hutao.Model.Cultivation;
using Snap.Hutao.Service.Cultivation.Consumption;
using Snap.Hutao.Web.Hoyolab.Takumi.Event.Calculate;

namespace Snap.Hutao.UI.Xaml.View.Dialog;

[DependencyProperty<AvatarPromotionDelta>("PromotionDelta", NotNull = true, CreateDefaultValueCallbackName = nameof(CreatePromotionDeltaDefaultValue))]
[DependencyProperty<bool>("ClearAvatarAndWeaponEntriesBeforeSync", DefaultValue = false, NotNull = true)]
[DependencyProperty<bool>("SyncInventoryItems", DefaultValue = false, NotNull = true)]
[DependencyProperty<bool>("SyncCharacterInfo", DefaultValue = false, NotNull = true)]
internal sealed partial class CultivatePromotionDeltaBatchDialog : ContentDialog
{
    private readonly IContentDialogFactory contentDialogFactory;

    public CultivatePromotionDeltaBatchDialog(IServiceProvider serviceProvider, CultivateProjectAvatarPropertyBatchPreferences? initialPreferences = null)
    {
        InitializeComponent();

        contentDialogFactory = serviceProvider.GetRequiredService<IContentDialogFactory>();

        if (initialPreferences is not null)
        {
            ApplyInitialPreferences(initialPreferences);
        }
    }

    public async ValueTask<ValueResult<bool, CultivatePromotionDeltaOptions>> GetPromotionDeltaBaselineAsync()
    {
        if (await contentDialogFactory.EnqueueAndShowAsync(this).ShowTask.ConfigureAwait(false) is not ContentDialogResult.Primary)
        {
            return new(false, default!);
        }

        await contentDialogFactory.TaskContext.SwitchToMainThreadAsync();

        LocalSetting.Set(SettingKeys.CultivationAvatarLevelCurrent, PromotionDelta.AvatarLevelCurrent);
        LocalSetting.Set(SettingKeys.CultivationAvatarLevelTarget, PromotionDelta.AvatarLevelTarget);

        if (PromotionDelta.SkillList is [{ } skillA, { } skillE, { } skillQ, ..])
        {
            LocalSetting.Set(SettingKeys.CultivationAvatarSkillACurrent, skillA.LevelCurrent);
            LocalSetting.Set(SettingKeys.CultivationAvatarSkillATarget, skillA.LevelTarget);
            LocalSetting.Set(SettingKeys.CultivationAvatarSkillECurrent, skillE.LevelCurrent);
            LocalSetting.Set(SettingKeys.CultivationAvatarSkillETarget, skillE.LevelTarget);
            LocalSetting.Set(SettingKeys.CultivationAvatarSkillQCurrent, skillQ.LevelCurrent);
            LocalSetting.Set(SettingKeys.CultivationAvatarSkillQTarget, skillQ.LevelTarget);
        }

        if (PromotionDelta.Weapon is { } weapon)
        {
            LocalSetting.Set(SettingKeys.CultivationWeapon90LevelCurrent, weapon.LevelCurrent);
            LocalSetting.Set(SettingKeys.CultivationWeapon90LevelTarget, weapon.LevelTarget);
        }

        return new(true, new CultivatePromotionDeltaOptions(PromotionDelta, (ConsumptionSaveStrategyKind)SaveModeSelector.SelectedIndex, ClearAvatarAndWeaponEntriesBeforeSync, SyncInventoryItems, SyncCharacterInfo));
    }

    private void ApplyInitialPreferences(CultivateProjectAvatarPropertyBatchPreferences p)
    {
        PromotionDelta.AvatarLevelTarget = p.AvatarLevelTarget;

        if (PromotionDelta.SkillList is [{ } a, { } e, { } q, ..])
        {
            a.LevelTarget = p.SkillATarget;
            e.LevelTarget = p.SkillETarget;
            q.LevelTarget = p.SkillQTarget;
        }

        if (PromotionDelta.Weapon is { } w)
        {
            w.LevelTarget = p.WeaponLevelTarget;
        }

        SaveModeSelector.SelectedIndex = int.Clamp(p.ConsumptionSaveStrategyIndex, 0, 2);
        ClearAvatarAndWeaponEntriesBeforeSync = p.ClearAvatarAndWeaponEntriesBeforeSync;
        SyncInventoryItems = p.SyncInventoryItems;
        SyncCharacterInfo = p.SyncCharacterInfo;
    }

    private static object CreatePromotionDeltaDefaultValue()
    {
        return AvatarPromotionDelta.CreateForBaseline();
    }
}
