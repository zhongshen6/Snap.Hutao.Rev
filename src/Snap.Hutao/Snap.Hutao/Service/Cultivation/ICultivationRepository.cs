// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Entity;
using Snap.Hutao.Service.Abstraction;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace Snap.Hutao.Service.Cultivation;

internal interface ICultivationRepository : IRepository<CultivateEntryLevelInformation>,
    IRepository<CultivateProject>,
    IRepository<CultivateEntry>,
    IRepository<CultivateItem>
{
    void AddCultivateProject(CultivateProject project);

    void RemoveCultivateEntryById(Guid entryId);

    void RemoveCultivateItemRangeByEntryId(Guid entryId);

    void RemoveCultivateProjectById(Guid projectId);

    ImmutableArray<CultivateEntry> GetCultivateEntryImmutableArrayByProjectId(Guid projectId);

    ImmutableArray<CultivateItem> GetCultivateItemImmutableArrayByEntryId(Guid entryId);

    ObservableCollection<CultivateProject> GetCultivateProjectCollection();

    ImmutableArray<Guid> GetCultivateProjectInnerIds();

    CultivateProject? GetCultivateProjectById(Guid projectId);

    void AddCultivateEntry(CultivateEntry entry);

    void AddCultivateItemRange(IEnumerable<CultivateItem> toAdd);

    void UpdateCultivateItem(CultivateItem item);

    void RemoveLevelInformationByEntryId(Guid entryId);

    void AddLevelInformation(CultivateEntryLevelInformation levelInformation);

    ImmutableArray<CultivateEntry> GetCultivateEntryImmutableArrayIncludingLevelInformationByProjectId(Guid projectId);

    ImmutableArray<CultivateEntry> GetCultivateEntryImmutableArrayByProjectIdAndItemId(Guid projectId, uint itemId);

    /// <summary>
    /// 解析当前计划中指定角色的养成条目（类型为 CultivateType.AvatarAndSkill）。
    /// 若存在多条历史记录，取最近插入数据库的一条（按 SQLite rowid 倒序）。
    /// </summary>
    Guid? TryGetAvatarCultivateEntryInnerId(Guid projectId, uint avatarId);

    Guid GetCultivateProjectIdByEntryId(Guid entryId);

    /// <summary>
    /// 联表查询某计划下所有养成物品及其所属条目（用于材料统计未勾选条目等）。
    /// </summary>
    ImmutableArray<(CultivateEntry Entry, CultivateItem Item)> GetCultivateEntryItemPairsByProjectId(Guid projectId);
}