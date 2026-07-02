// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Metadata;
using Snap.Hutao.Model.Metadata.Abstraction;
using Snap.Hutao.Model.Metadata.Avatar;
using Snap.Hutao.Model.Metadata.Weapon;
using Snap.Hutao.Model.Primitive;
using Snap.Hutao.Service.Achievement;
using Snap.Hutao.Service.AvatarInfo.Factory;
using Snap.Hutao.Service.Cultivation;
using Snap.Hutao.Service.GachaLog;
using Snap.Hutao.Service.Metadata.ContextAbstraction.ImmutableDictionary;
using Snap.Hutao.Web.Hoyolab.Hk4e.Event.GachaInfo;
using System.Collections.Immutable;
using System.Globalization;

namespace Snap.Hutao.Service.Metadata;

internal sealed partial class ExternalMetadataValidator
{
    private readonly HashSet<string> missingItems = [];

    public bool HasMissingMetadata { get; private set; }

    public string MissingMetadataDescription
    {
        get
        {
            const int maxDisplayCount = 5;
            IEnumerable<string> displayItems = missingItems.Take(maxDisplayCount);
            string suffix = missingItems.Count > maxDisplayCount ? "..." : string.Empty;
            return $"{string.Join(", ", displayItems)}{suffix}";
        }
    }

    public bool RequireGachaLogItem(GachaLogServiceMetadataContext context, GachaLogItem item)
    {
        if (string.Equals(item.ItemType, SH.ModelInterchangeUIGFItemTypeAvatar, StringComparison.Ordinal))
        {
            return RequireKnown(context.NameAvatarMap.ContainsKey(item.Name), "Avatar", item.Name);
        }

        if (string.Equals(item.ItemType, SH.ModelInterchangeUIGFItemTypeWeapon, StringComparison.Ordinal))
        {
            return RequireKnown(context.NameWeaponMap.ContainsKey(item.Name), "Weapon", item.Name);
        }

        return RequireKnown(false, "GachaItem", $"{item.ItemType},{item.Name}");
    }

    public bool RequireGachaLogItemId(GachaLogServiceMetadataContext context, uint itemId)
    {
        uint place = itemId.StringLength;
        bool known = place switch
        {
            8U => context.IdAvatarMap.ContainsKey(itemId),
            5U => context.IdWeaponMap.ContainsKey(itemId),
            _ => false,
        };

        return RequireKnown(known, "GachaItem", itemId.ToString(CultureInfo.InvariantCulture));
    }

    public bool RequireAvatar(IMetadataDictionaryIdAvatarSource context, AvatarId id)
    {
        return RequireKnown(context.IdAvatarMap.ContainsKey(id), "Avatar", id);
    }

    public bool RequireWeapon(IMetadataDictionaryIdWeaponSource context, WeaponId id)
    {
        return RequireKnown(context.IdWeaponMap.ContainsKey(id), "Weapon", id);
    }

    public bool RequireMaterial(IMetadataDictionaryIdMaterialSource context, MaterialId id)
    {
        return RequireKnown(context.IdMaterialMap.ContainsKey(id), "Material", id);
    }

    public bool RequireAchievement(AchievementServiceMetadataContext context, AchievementId id)
    {
        return RequireKnown(context.IdAchievementMap.ContainsKey(id), "Achievement", id);
    }

    public bool RequireReliquary(SummaryFactoryMetadataContext context, ReliquaryId id)
    {
        if (!context.IdReliquaryMap.TryGetValue(id, out Model.Metadata.Reliquary.Reliquary? reliquary))
        {
            return RequireKnown(false, "Reliquary", id);
        }

        return RequireKnown(context.IdReliquarySetMap.ContainsKey(reliquary.SetId), "ReliquarySet", reliquary.SetId);
    }

    public bool RequireAvatarInfo(SummaryFactoryMetadataContext context, Web.Hoyolab.Takumi.GameRecord.Avatar.DetailedCharacter character)
    {
        if (AvatarIds.IsPlayer(character.Base.Id))
        {
            return true;
        }

        bool valid = RequireAvatar(context, character.Base.Id);

        if (context.IdWeaponMap.TryGetValue(character.Weapon.Id, out Weapon? weapon))
        {
            if (context.IdDictionaryWeaponLevelPromoteMap.TryGetValue(weapon.PromoteId, out ImmutableDictionary<PromoteLevel, Promote>? promoteMap))
            {
                valid &= RequireKnown(promoteMap.ContainsKey(character.Weapon.PromoteLevel), "WeaponPromoteLevel", character.Weapon.PromoteLevel);
            }
            else
            {
                valid &= RequireKnown(false, "WeaponPromote", weapon.PromoteId);
            }

            valid &= RequireKnown(context.LevelDictionaryWeaponGrowCurveMap.ContainsKey(character.Weapon.Level), "WeaponLevel", character.Weapon.Level);
        }
        else
        {
            valid &= RequireWeapon(context, character.Weapon.Id);
        }

        foreach (Web.Hoyolab.Takumi.GameRecord.Avatar.Reliquary reliquary in character.Relics)
        {
            valid &= RequireReliquary(context, reliquary.Id);
        }

        return valid;
    }

    public bool RequireCultivationItem(ICultivationMetadataContext context, Web.Hoyolab.Takumi.Event.Calculate.Item item)
    {
        return RequireMaterial(context, item.Id);
    }

    public bool RequireKnown<T>(bool known, string kind, T id)
    {
        if (!known)
        {
            missingItems.Add($"{kind}:{id}");
            HasMissingMetadata = true;
        }

        return known;
    }
}
