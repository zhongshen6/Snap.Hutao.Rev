// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.InterChange.Achievement;
using Snap.Hutao.Model.InterChange.GachaLog;
using Snap.Hutao.Model.InterChange.Inventory;
using Snap.Hutao.Service.Achievement;
using Snap.Hutao.Service.AvatarInfo.Factory;
using Snap.Hutao.Service.Cultivation;
using Snap.Hutao.Service.GachaLog;
using Snap.Hutao.Service.Notification;
using Snap.Hutao.ViewModel.HardChallenge;
using Snap.Hutao.ViewModel.RoleCombat;
using Snap.Hutao.ViewModel.SpiralAbyss;
using Snap.Hutao.Web.Hoyolab.Hk4e.Event.GachaInfo;
using Snap.Hutao.Web.Hoyolab.Takumi.GameRecord.HardChallenge;
using Snap.Hutao.Web.Hoyolab.Takumi.GameRecord.RoleCombat;
using Snap.Hutao.Web.Hoyolab.Takumi.GameRecord.Avatar;
using System.Collections.Immutable;
using CalculableAvatar = Snap.Hutao.Web.Hoyolab.Takumi.Event.Calculate.Avatar;
using CalculableWeapon = Snap.Hutao.Web.Hoyolab.Takumi.Event.Calculate.Weapon;
using CalculateItem = Snap.Hutao.Web.Hoyolab.Takumi.Event.Calculate.Item;
using CloudGachaItem = Snap.Hutao.Web.Hutao.GachaLog.GachaItem;
using WebSpiralAbyss = Snap.Hutao.Web.Hoyolab.Takumi.GameRecord.SpiralAbyss.SpiralAbyss;

namespace Snap.Hutao.Service.Metadata;

[Service(ServiceLifetime.Singleton)]
internal sealed partial class ExternalMetadataGuard
{
    private readonly IMessenger messenger;

    [GeneratedConstructor]
    public partial ExternalMetadataGuard(IServiceProvider serviceProvider);

    public bool ValidateAvatarDetails(SummaryFactoryMetadataContext context, IEnumerable<DetailedCharacter> characters)
    {
        return Validate(validator =>
        {
            foreach (DetailedCharacter character in characters)
            {
                validator.RequireAvatarInfo(context, character);
            }
        });
    }

    public bool ValidateGachaLogPage(GachaLogServiceMetadataContext context, GachaLogPage page)
    {
        return Validate(validator =>
        {
            foreach (GachaLogItem item in page.List)
            {
                validator.RequireGachaLogItem(context, item);
            }
        });
    }

    public bool ValidateGachaLogItems(GachaLogServiceMetadataContext context, IEnumerable<CloudGachaItem> items)
    {
        return Validate(validator =>
        {
            foreach (CloudGachaItem item in items)
            {
                validator.RequireGachaLogItemId(context, item.ItemId);
            }
        });
    }

    public bool ValidateUIGF(GachaLogServiceMetadataContext context, ImmutableArray<UIGFEntry<Hk4eItem>> entries, HashSet<uint> uids)
    {
        return Validate(validator =>
        {
            foreach (ref readonly UIGFEntry<Hk4eItem> entry in entries.AsSpan())
            {
                if (!uids.Contains(entry.Uid))
                {
                    continue;
                }

                foreach (Hk4eItem item in entry.List)
                {
                    if (item.ItemId is not 0U)
                    {
                        validator.RequireGachaLogItemId(context, item.ItemId);
                    }
                }
            }
        });
    }

    public bool ValidateInventoryItems(ICultivationMetadataContext context, IEnumerable<CalculateItem> items)
    {
        return Validate(validator =>
        {
            foreach (CalculateItem item in items)
            {
                validator.RequireCultivationItem(context, item);
            }
        });
    }

    public bool ValidateCalculableItems(ICultivationMetadataContext context, ImmutableArray<CalculableAvatar> avatars, ImmutableArray<CalculableWeapon> weapons)
    {
        return Validate(validator =>
        {
            foreach (ref readonly CalculableAvatar avatar in avatars.AsSpan())
            {
                validator.RequireAvatar(context, avatar.Id);
            }

            foreach (ref readonly CalculableWeapon weapon in weapons.AsSpan())
            {
                validator.RequireWeapon(context, weapon.Id);
            }
        });
    }

    public bool ValidateUIIF(ICultivationMetadataContext context, UIIF uiif)
    {
        return Validate(validator =>
        {
            foreach (UIIFItem item in uiif.List)
            {
                if (item.Material is not null)
                {
                    validator.RequireMaterial(context, item.ItemId);
                }
            }
        });
    }

    public bool ValidateUIAF(AchievementServiceMetadataContext context, UIAF uiaf)
    {
        return Validate(validator =>
        {
            foreach (UIAFItem item in uiaf.List)
            {
                validator.RequireAchievement(context, item.Id);
            }
        });
    }

    public bool ValidateSpiralAbyss(SpiralAbyssMetadataContext context, WebSpiralAbyss data)
    {
        return Validate(validator => validator.RequireSpiralAbyss(context, data));
    }

    public bool ValidateRoleCombat(RoleCombatMetadataContext context, RoleCombatData data)
    {
        return Validate(validator => validator.RequireRoleCombat(context, data));
    }

    public bool ValidateHardChallenge(HardChallengeMetadataContext context, HardChallengeData data)
    {
        return Validate(validator => validator.RequireHardChallenge(context, data));
    }

    public bool ValidateHardChallengePopularity(HardChallengeMetadataContext context, HardChallengePopularity popularity)
    {
        return Validate(validator =>
        {
            foreach (HardChallengeSimpleAvatar avatar in popularity.AvatarList)
            {
                validator.RequireAvatar(context, avatar.AvatarId);
            }
        });
    }

    private bool Validate(Action<ExternalMetadataValidator> validate)
    {
        ExternalMetadataValidator validator = new();
        validate(validator);
        return ReportIfValid(validator);
    }

    private bool ReportIfValid(ExternalMetadataValidator validator)
    {
        if (!validator.HasMissingMetadata)
        {
            return true;
        }

        messenger.Send(InfoBarMessage.Warning(SH.FormatServiceExternalMetadataIncomplete(validator.MissingMetadataDescription)));
        return false;
    }
}
