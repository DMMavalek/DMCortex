using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

/// <summary>Verified free acquisition grants and catalog-base accounting only.
/// Does not calculate crossover penalties or class cost reductions.</summary>
public static class NwpGrantRules
{
    public static bool IsFree(CharGenState state, NonweaponProficiencyDefinition definition)
        => state.KitFreeNwpIds.Contains(definition.Id, StringComparer.OrdinalIgnoreCase)
            || (RangerTrackingRules.IsTracking(definition.Id, definition.Name)
                && RangerTrackingRules.HasTrackingGrant(state));

    /// <summary>Returns zero for a grant; otherwise preserves a supplied policy-adjusted cost
    /// or uses the catalog CP/slot fallback. Improvements are never waived.</summary>
    public static int GetAcquisitionCost(
        CharGenState state, NonweaponProficiencyDefinition definition, int? baseCost = null)
        => IsFree(state, definition) ? 0
            : baseCost ?? (definition.CpCost > 0 ? definition.CpCost : Math.Max(1, definition.Slots));

    public static int GetImprovementCost(
        IEnumerable<string> ids, IReadOnlyDictionary<string, int> improvements)
    {
        var selected = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return improvements.Where(entry => selected.Contains(entry.Key))
            .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Sum(group => Math.Max(0, group.Max(entry => entry.Value)));
    }

    /// <summary>Counts every selected acquisition, but improvements once per selected distinct id.
    /// Unknown definitions and stale unselected improvements do not contribute.</summary>
    public static int GetTotalBaseCost(
        IEnumerable<string> ids, IReadOnlyDictionary<string, int> improvements,
        IEnumerable<NonweaponProficiencyDefinition> definitions, CharGenState state)
    {
        var byId = definitions.GroupBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var selected = ids.Where(byId.ContainsKey).ToList();
        return selected.Sum(id => GetAcquisitionCost(state, byId[id]))
            + GetImprovementCost(selected, improvements);
    }
}