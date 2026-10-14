using System.Text.Json;
using System.Text.RegularExpressions;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

/// <summary>A free, source-attributed rating modifier. Source and Target are exact aliases, not substrings.</summary>
public sealed record NwpSynergyRule(
    string Id,
    string SourceKind,
    IReadOnlyList<string> Source,
    IReadOnlyList<string> Target,
    int Modifier,
    bool Situational,
    string Condition,
    string StackingGroup,
    string SourceReference,
    int CpCost = 0);

/// <summary>
/// Read-only SP synergy queries. Does not calculate purchase/improvement CP, Ranger class bonuses,
/// ability modifiers, HP, percentage checks, or persist any character/catalog changes.
/// UI/review/exchange query contributions without changing purchase or improvement history.
/// </summary>
public static class NwpSynergyRules
{
    private const string ResourceName = "DungeonMasterCortex.NwpSynergies.json";
    private static readonly Lazy<IReadOnlyList<NwpSynergyRule>> BundledRules = new(LoadRules);
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "general", "common", "warrior", "rogue", "priest", "wizard", "fighter",
        "ranger", "paladin", "thief", "bard", "cleric", "druid", "psionicist"
    };

    public static IReadOnlyList<NwpSynergyRule> Rules => BundledRules.Value;

    /// <summary>Returns each owned rule once, including available (not yet applied) conditional rules.</summary>
    public static IReadOnlyList<NwpSynergyRule> GetContributions(
        CharGenState state, NonweaponProficiencyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (CharGenState.NormalizeCharacterModeToken(state.CharacterMode) != "players_option")
            return Array.Empty<NwpSynergyRule>();
        return GetContributions(definition, new Dictionary<string, IEnumerable<string>>
        {
            ["proficiency"] = state.SelectedNonweaponProficiencyIds.Concat(state.KitFreeNwpIds),
            ["trait"] = state.SelectedTraitIds,
            ["race"] = new[] { string.IsNullOrWhiteSpace(state.BaseRaceId) ? state.RaceId : state.BaseRaceId },
            ["class"] = state.SelectedClassIds.Append(state.ClassId)
        });
    }

    public static IReadOnlyList<NwpSynergyRule> GetContributions(
        CharacterSheet character, NonweaponProficiencyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (CharGenState.NormalizeCharacterModeToken(character.CharacterMode) != "players_option")
            return Array.Empty<NwpSynergyRule>();
        return GetContributions(definition, new Dictionary<string, IEnumerable<string>>
        {
            ["proficiency"] = character.NonweaponProficiencyIds.Concat(character.KitFreeNwpIds)
                .Concat(character.StructuredNonweaponProficiencies.SelectMany(record =>
                    new[] { record.ProficiencyId, record.Id, record.DisplayName }))
                .Concat(character.NonweaponProficiencies.Select(LegacyName)),
            ["trait"] = character.SelectedTraitIds.Concat(character.Traits.Select(LegacyName)),
            ["race"] = new[] { character.RaceId, character.RaceName },
            ["class"] = character.ClassIds.Append(character.ClassId).Append(character.ClassName)
        });
    }

    public static int GetPermanentBonus(CharGenState state, NonweaponProficiencyDefinition definition)
        => PermanentBonus(GetContributions(state, definition));

    public static int GetPermanentBonus(CharacterSheet character, NonweaponProficiencyDefinition definition)
        => PermanentBonus(GetContributions(character, definition));

    /// <summary>
    /// Reports every condition independently against the supplied already-permanent baseTarget.
    /// Individual targets are alternatives, not a blanket combined target. No permanent bonus is added again.
    /// </summary>
    public static string GetConditionalSummary(
        CharGenState state, NonweaponProficiencyDefinition definition, int baseTarget)
        => ConditionalSummary(GetContributions(state, definition), baseTarget);

    public static string GetConditionalSummary(
        CharacterSheet character, NonweaponProficiencyDefinition definition, int baseTarget)
        => ConditionalSummary(GetContributions(character, definition), baseTarget);

    /// <summary>
    /// Returns ONLY matching conditional modifiers. Conditions are explicit task assertions by the caller.
    /// "wild" implies "animals"; repeated conditions/ownership cannot duplicate a grant.
    /// "small_animal_snare" asserts the small-animal catch check after eight hours, not pit construction.
    /// Agriculture's *_relevant keys require explicit task relevance; own_specialty_or_school asserts
    /// that the studied spell belongs to the wizard's own specialty/school, not merely any school.
    /// Nonempty stacking groups use their highest modifier (stars stacking is unresolved, not assumed +5).
    /// </summary>
    public static int EvaluateConditionalBonus(
        CharGenState state, NonweaponProficiencyDefinition definition, IEnumerable<string> contextConditions)
        => ConditionalBonus(GetContributions(state, definition), contextConditions);

    public static int EvaluateConditionalBonus(
        CharacterSheet character, NonweaponProficiencyDefinition definition, IEnumerable<string> contextConditions)
        => ConditionalBonus(GetContributions(character, definition), contextConditions);

    private static IReadOnlyList<NwpSynergyRule> GetContributions(
        NonweaponProficiencyDefinition definition, Dictionary<string, IEnumerable<string>> ownership)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var tokens = ownership.ToDictionary(entry => entry.Key,
            entry => entry.Value.Select(value => Normalize(value, entry.Key)).ToHashSet(StringComparer.Ordinal));
        string id = Normalize(definition.Id, "proficiency");
        string name = Normalize(definition.Name, "proficiency");
        return Array.AsReadOnly(Rules.Where(rule =>
            rule.Target.Any(alias => Normalize(alias, "proficiency") is var target
                && target.Length > 0 && (target == id || target == name))
            && rule.Source.Any(alias => tokens[rule.SourceKind].Contains(Normalize(alias, rule.SourceKind))))
            .ToArray());
    }

    private static int PermanentBonus(IEnumerable<NwpSynergyRule> rules)
        => rules.Where(rule => !rule.Situational).Sum(rule => rule.Modifier);

    private static string ConditionalSummary(IEnumerable<NwpSynergyRule> rules, int baseTarget)
        => string.Join(Environment.NewLine, rules.Where(rule => rule.Situational).Select(rule =>
            $"{rule.Condition}: {rule.Modifier:+0;-0;0} ({rule.Id}), target {baseTarget + rule.Modifier}"
            + (rule.StackingGroup.Length == 0 ? "" :
                $"; stacking unresolved ({rule.StackingGroup}); evaluator uses highest only")
            + $"; source: {rule.SourceReference}"));

    private static int ConditionalBonus(IEnumerable<NwpSynergyRule> rules, IEnumerable<string> conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        var context = conditions.Select(NormalizeCondition).ToHashSet(StringComparer.Ordinal);
        if (context.Contains("wild"))
            context.Add("animals");
        var matching = rules.Where(rule => rule.Situational && context.Contains(rule.Condition)).ToArray();
        return matching.Where(rule => rule.StackingGroup.Length == 0).Sum(rule => rule.Modifier)
            + matching.Where(rule => rule.StackingGroup.Length > 0)
                .GroupBy(rule => rule.StackingGroup, StringComparer.Ordinal)
                .Sum(group => group.Max(rule => rule.Modifier));
    }

    private static string NormalizeCondition(string value)
    {
        string token = Snake(value);
        return token switch
        {
            "wild_animals" or "non_domesticated_animals" => "wild",
            "animal" or "domesticated_animals" => "animals",
            _ => token
        };
    }

    // Strip only recognized wrappers/categories; never arbitrary suffixes. AnimalEmpathy != Empathy.
    private static string Normalize(string value, string kind)
    {
        string token = Snake(value);
        string prefix = kind switch { "proficiency" => "nwp_", "trait" => "trait_", "race" => "race_", _ => "class_" };
        if (token.StartsWith(prefix, StringComparison.Ordinal))
            token = token[prefix.Length..];
        if (kind == "proficiency")
        {
            int separator = token.IndexOf('_');
            if (separator > 0 && Categories.Contains(token[..separator]))
                token = token[(separator + 1)..];
        }
        if (kind == "race")
        {
            // Race/subrace IDs use these whole-token prefixes. Not applied to traits/proficiencies.
            if (token.StartsWith("dwarf_", StringComparison.Ordinal)) token = "dwarf";
            if (token.StartsWith("halfling_", StringComparison.Ordinal)) token = "halfling";
        }
        return token.Replace("_", "", StringComparison.Ordinal);
    }

    private static string Snake(string value)
        => Regex.Replace(Regex.Replace(value ?? "", "([a-z0-9])([A-Z])", "$1_$2")
            .ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');

    // Legacy summaries store "Animal Lore 12 (8 CP...)"; retain only the exact name portion.
    private static string LegacyName(string value)
        => new Regex(@"\s+[+-]?\d+(?:\s|$)|\s*\(").Split(value ?? "", 2)[0].Trim();

    private static IReadOnlyList<NwpSynergyRule> LoadRules()
    {
        using var stream = typeof(NwpSynergyRules).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing bundled resource {ResourceName}.");
        var rules = JsonSerializer.Deserialize<NwpSynergyRule[]>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Bundled NWP synergy rules are empty.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (rules.Length is 0 or > 40)
            throw new InvalidOperationException("Bundled NWP synergy rule count must be 1–40.");
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id)
                || rule.SourceKind is not ("proficiency" or "trait" or "race" or "class")
                || rule.Source is not { Count: > 0 } || rule.Target is not { Count: > 0 }
                || rule.Source.Any(string.IsNullOrWhiteSpace) || rule.Target.Any(string.IsNullOrWhiteSpace)
                || rule.Modifier <= 0 || rule.CpCost != 0 || rule.Condition is null || rule.StackingGroup is null
                || rule.Situational != !string.IsNullOrWhiteSpace(rule.Condition)
                || string.IsNullOrWhiteSpace(rule.SourceReference))
                throw new InvalidOperationException($"Invalid bundled NWP synergy rule: {rule.Id}.");
        }
        return Array.AsReadOnly(rules.Select(rule => rule with
        {
            Source = Array.AsReadOnly(rule.Source.ToArray()),
            Target = Array.AsReadOnly(rule.Target.ToArray())
        }).ToArray());
    }
}