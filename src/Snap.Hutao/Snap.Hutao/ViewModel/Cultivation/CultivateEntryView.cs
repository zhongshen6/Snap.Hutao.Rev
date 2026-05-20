// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model;
using Snap.Hutao.Model.Entity;
using Snap.Hutao.Model.Entity.Primitive;
using Snap.Hutao.Model.Metadata.Item;
using Snap.Hutao.Model.Primitive;
using Snap.Hutao.UI.Xaml.Data;
using System.Collections.Immutable;
using System.Text;

namespace Snap.Hutao.ViewModel.Cultivation;

internal sealed partial class CultivateEntryView : Item, IPropertyValuesProvider
{
    private CultivateEntryView(CultivateEntry entry, Item item, ImmutableArray<CultivateItemView> items, string? relatedAvatarName)
    {
        Id = entry.Id;
        EntryId = entry.InnerId;
        Name = item.Name;
        Icon = item.Icon;
        Badge = item.Badge;
        Quality = item.Quality;
        Items = items;
        Type = entry.Type;

        RelatedAvatarLine = relatedAvatarName is { } name ? SH.FormatViewModelCultivationEntryRelatedAvatar(name) : null;

        Description = ParseDescription(entry);
        IsToday = items.Any(i => i.IsToday);
        RotationalItemIds = [.. items.Where(i => i.DaysOfWeek is not DaysOfWeek.Any).Select(i => i.Inner.Id)];
        DaysOfWeek = MaterialIds.GetDaysOfWeek(RotationalItemIds.AsSpan());

        static string ParseDescription(CultivateEntry entry)
        {
            if (entry.LevelInformation is null)
            {
                return SH.ViewModelCultivationEntryViewDescriptionDefault;
            }

            CultivateEntryLevelInformation info = entry.LevelInformation;

            switch (entry.Type)
            {
                case CultivateType.AvatarAndSkill:
                    {
                        StringBuilder stringBuilder = new();

                        if (info.AvatarLevelFrom != info.AvatarLevelTo)
                        {
                            stringBuilder.Append("Lv.").Append(info.AvatarLevelFrom).Append(" → Lv.").Append(info.AvatarLevelTo).Append(' ');
                            stringBuilder.AppendLine();
                        }
                        else
                        {
                            if (info.AvatarIsPromoting)
                            {
                                stringBuilder.Append("Lv.").Append(info.AvatarLevelFrom).Append(" (").Append(SH.ViewModelCultivationEntryViewPromoteOnlyHint).Append(')');
                            }
                        }

                        if (info.SkillALevelFrom != info.SkillALevelTo)
                        {
                            stringBuilder.Append("A: ").Append(info.SkillALevelFrom).Append(" → ").Append(info.SkillALevelTo).Append(' ');
                        }

                        if (info.SkillELevelFrom != info.SkillELevelTo)
                        {
                            stringBuilder.Append("E: ").Append(info.SkillELevelFrom).Append(" → ").Append(info.SkillELevelTo).Append(' ');
                        }

                        if (info.SkillQLevelFrom != info.SkillQLevelTo)
                        {
                            stringBuilder.Append("Q: ").Append(info.SkillQLevelFrom).Append(" → ").Append(info.SkillQLevelTo).Append(' ');
                        }

                        return stringBuilder.ToStringTrimEndNewLine();
                    }

                case CultivateType.Weapon:
                    {
                        StringBuilder stringBuilder = new();

                        if (info.WeaponLevelFrom != info.WeaponLevelTo)
                        {
                            stringBuilder.Append("Lv.").Append(info.WeaponLevelFrom).Append(" → Lv.").Append(info.WeaponLevelTo);
                        }
                        else
                        {
                            if (info.WeaponIsPromoting)
                            {
                                stringBuilder.Append("Lv.").Append(info.WeaponLevelFrom).Append(" (").Append(SH.ViewModelCultivationEntryViewPromoteOnlyHint).Append(')');
                            }
                        }

                        return stringBuilder.ToString();
                    }
            }

            return string.Empty;
        }
    }

    public ImmutableArray<CultivateItemView> Items { get; set; }

    public ImmutableArray<MaterialId> RotationalItemIds { get; }

    public bool IsToday { get; }

    public DaysOfWeek DaysOfWeek { get; }

    public string Description { get; }

    /// <summary>
    /// 武器条目在养成计算中与角色条目关联时的展示文案（含本地化前缀）。
    /// </summary>
    public string? RelatedAvatarLine { get; }

    internal Guid EntryId { get; }

    internal CultivateType Type { get; }

    public static CultivateEntryView Create(CultivateEntry entry, Item item, ImmutableArray<CultivateItemView> items, string? relatedAvatarName = null)
    {
        return new(entry, item, items, relatedAvatarName);
    }
}