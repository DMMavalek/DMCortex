using System.Reflection;
using System.Runtime.CompilerServices;
using DungeonMasterCortex;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;
using DungeonMasterCortex.Views;

internal static class Program
{
    private static int checks;
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void Equal<T>(T expected, T actual, string message)
    {
        checks++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}; actual {actual}");
    }

    private static object Invoke(Type type, string name, object? target, params object[] args)
        => type.GetMethods(target is null ? PrivateStatic : PrivateInstance)
            .Single(method => method.Name == name && method.GetParameters().Length == args.Length)
            .Invoke(target, args)!;

    private static int Review(CharGenState state, List<NonweaponProficiencyDefinition> definitions)
        => (int)Invoke(typeof(CharGenReviewScreen), "CalculateTotalNwpCp", null, state, definitions);

    private static int BaselineReview(CharGenState state, List<NonweaponProficiencyDefinition> definitions)
        => (int)Invoke(typeof(CharGenReviewScreen), "CalculateTotalNwpCp", null,
            state.BaselineNonweaponProficiencyIds, state.BaselineNonweaponProficiencyImprovements, definitions, state);

    [STAThread]
    private static void Main()
    {
        var tracking = new NonweaponProficiencyDefinition("nwp_tracking", "Tracking", "Warrior", 2,
            "Wisdom", -6, "fixture", CpCost: 4);
        var trackingAlias = tracking with { Id = "import_tracking", Name = " Tracking " };
        var kit = tracking with { Id = "nwp_weaving", Name = "Weaving", CpCost = 3 };
        var paid = tracking with { Id = "nwp_history", Name = "History", CpCost = 2 };
        var stale = paid with { Id = "nwp_stale", Name = "Stale" };
        var fallback = paid with { Id = "nwp_fallback", CpCost = 0, Slots = 3 };
        var definitions = new List<NonweaponProficiencyDefinition> { tracking, trackingAlias, kit, paid, stale, fallback };

        // No screen/window construction, catalog loading, or user-local storage access.
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var weapon = (CharGenWeaponProficienciesScreen)RuntimeHelpers.GetUninitializedObject(typeof(CharGenWeaponProficienciesScreen));
        typeof(CharGenWeaponProficienciesScreen).GetField("_app", PrivateInstance)!.SetValue(weapon, window);
        typeof(CharGenWeaponProficienciesScreen).GetField("_catalog", PrivateInstance)!.SetValue(weapon,
            new CharacterOptionCatalog(definitions, Array.Empty<TraitDefinition>(), Array.Empty<DisadvantageDefinition>()));
        int Weapon(string method, params object[] args)
            => (int)Invoke(typeof(CharGenWeaponProficienciesScreen), method, weapon, args);

        foreach (string mode in new[] { "new", "existing", "levelup" })
        {
            var state = new CharGenState
            {
                CharacterMode = "players_option", ClassId = "ranger",
                SelectedClassIds = new() { "ranger" },
                SelectedClassAbilityIds = new() { RangerTrackingRules.TrackingAbilityId },
                KitFreeNwpIds = new() { kit.Id.ToUpperInvariant(), kit.Id },
                IsExistingCharacterMode = mode == "existing", IsLevelUpMode = mode == "levelup",
                SelectedNonweaponProficiencyIds = new() { tracking.Id, trackingAlias.Id, kit.Id, paid.Id, paid.Id.ToUpperInvariant() },
                BaselineNonweaponProficiencyIds = new() { tracking.Id, trackingAlias.Id, kit.Id, paid.Id },
                SelectedNonweaponProficiencyImprovements = new()
                {
                    [tracking.Id] = 3, [trackingAlias.Id] = 1, [kit.Id] = 2, [paid.Id] = 5,
                    [stale.Id] = 99, ["missing"] = 88,
                },
                BaselineNonweaponProficiencyImprovements = new()
                {
                    [tracking.Id] = 1, [trackingAlias.Id] = 1, [kit.Id] = 2, [paid.Id] = 2, [stale.Id] = 77,
                },
            };
            typeof(MainWindow).GetField("<CharGen>k__BackingField", PrivateInstance)!.SetValue(window, state);
            Equal(true, NwpGrantRules.IsFree(state, trackingAlias), "Tracking name alias uses IsTracking");
            Equal(true, NwpGrantRules.IsFree(state, kit), "Non-Tracking kit grant is case insensitive");
            Equal(false, NwpGrantRules.IsFree(state, paid), "Ordinary NWP is not granted");
            Equal(0, NwpGrantRules.GetAcquisitionCost(state, tracking), "Class grant costs zero acquisition");
            Equal(0, NwpGrantRules.GetAcquisitionCost(state, kit, 12), "Grant waives supplied acquisition only");
            Equal(7, NwpGrantRules.GetAcquisitionCost(state, paid, 7), "Caller-supplied adjusted cost is preserved");
            Equal(3, NwpGrantRules.GetAcquisitionCost(state, fallback), "Catalog slot fallback unchanged");
            Equal(1, NwpGrantRules.GetAcquisitionCost(state, fallback with { Slots = 0 }), "Slot fallback minimum unchanged");
            Equal(15, Review(state, definitions), $"{mode}: current = two paid acquisitions + eleven improvements");
            Equal(8, BaselineReview(state, definitions), $"{mode}: baseline = one paid acquisition + six improvements");
            Equal(7, Review(state, definitions) - BaselineReview(state, definitions), "Update delta includes purchase and improvement changes only");
            Equal(4, Weapon("GetSelectedNwpPurchaseCp"), "Weapon current charges repeated paid acquisitions");
            Equal(2, Weapon("GetNwpPurchaseCpForIds", state.BaselineNonweaponProficiencyIds), "Weapon baseline has identical grant policy");
            Equal(11, Weapon("GetTotalNwpImprovementCp"), "Weapon improvements count selected distinct ids once");
            Equal(6, (int)Invoke(typeof(CharGenWeaponProficienciesScreen), "GetNwpImprovementCpForState", null,
                state.BaselineNonweaponProficiencyIds, state.BaselineNonweaponProficiencyImprovements), "Baseline improvements exclude stale entries");
            Equal(11, NwpGrantRules.GetImprovementCost(state.SelectedNonweaponProficiencyIds,
                state.SelectedNonweaponProficiencyImprovements), "Review header improvement count ignores repeated acquisition");
            string kitSummary = (string)Invoke(typeof(CharGenReviewScreen), "FormatNwpSummary", null, state, kit);
            Equal(true, kitSummary.Contains("2 CP improvement; granted by kit"), "Kit formatter preserves paid improvement cost");
            string trackingSummary = (string)Invoke(typeof(CharGenReviewScreen), "FormatNwpSummary", null, state, tracking);
            Equal(true, trackingSummary.Contains("3 CP improvement; granted by class"), "Tracking formatter preserves paid improvement cost");

            state.SelectedClassAbilityIds.Add(RangerTrackingRules.TrackingAbilityId);
            state.SelectedAbilitiesByClass["ranger"] = new() { RangerTrackingRules.TrackingAbilityId, RangerTrackingRules.TrackingAbilityId };
            Equal(15, Review(state, definitions), "Duplicate ability/kit grants never subtract twice or refund improvements");
            Equal(0, NwpGrantRules.GetTotalBaseCost(new[] { tracking.Id, tracking.Id, kit.Id },
                new Dictionary<string, int>(), definitions, state), "Repeated grants cannot produce negative totals");
            Equal(15, Review(state, definitions.Concat(new[] { paid with { CpCost = 90 } }).ToList()), "Duplicate catalog definitions do not double charge");

            state.SelectedClassAbilityIds.Clear();
            state.SelectedAbilitiesByClass.Clear();
            Equal(4, NwpGrantRules.GetAcquisitionCost(state, tracking), "Ranger membership alone is not a grant");
            state.ClassId = "fighter";
            state.SelectedClassIds = new() { "fighter" };
            state.SelectedClassAbilityIds.Add(RangerTrackingRules.TrackingAbilityId);
            Equal(4, NwpGrantRules.GetAcquisitionCost(state, trackingAlias), "Tracking ability without Ranger membership is not a grant");
            state.CharacterMode = "core_rules";
            Equal(0, Weapon("GetSelectedNwpPurchaseCp"), "Weapon core CP accounting remains off");
        }

        var ordinary = new CharGenState { CharacterMode = "players_option" };
        Equal(7, NwpGrantRules.GetTotalBaseCost(new[] { paid.Id, paid.Id.ToUpperInvariant() },
            new Dictionary<string, int> { [paid.Id] = 3, [stale.Id] = 99 }, definitions, ordinary),
            "Repeated paid purchases cost each; shared improvement only once");
        Equal(7, NwpGrantRules.GetTotalBaseCost(new[] { paid.Id, paid.Id.ToUpperInvariant() },
            new Dictionary<string, int> { [paid.Id] = 3, [paid.Id.ToUpperInvariant()] = 3 }, definitions, ordinary),
            "Case-alias improvement keys are not separate purchases");
        Equal(2, NwpGrantRules.GetTotalBaseCost(new[] { paid.Id, "missing" },
            new Dictionary<string, int> { [paid.Id] = -2, ["missing"] = 99 }, definitions, ordinary),
            "Negative improvements and unknown definitions do not contribute");
        Equal(0, NwpGrantRules.GetTotalBaseCost(Array.Empty<string>(),
            new Dictionary<string, int> { [paid.Id] = 99 }, definitions, ordinary), "All unselected improvements are stale");

        foreach (int cost in new[] { 2, 4, 7 })
        {
            var state = new CharGenState
            {
                ClassId = "ranger", SelectedClassAbilityIds = new() { RangerTrackingRules.TrackingAbilityId },
                SelectedNonweaponProficiencyIds = new() { tracking.Id },
                SelectedNonweaponProficiencyImprovements = new() { [tracking.Id] = 3 },
            };
            var definition = tracking with { CpCost = cost };
            Equal(3, Review(state, new() { definition }), "Grant leaves all paid improvements in total");
            Equal(cost, cost + 3 - Review(state, new() { definition }), "Correction impact follows actual catalog cost, not fixed six CP");
        }

        var original = new AbilityDefinition
        {
            Effect = new AbilityEffect
            {
                NwpCheckBonusesById = new(StringComparer.OrdinalIgnoreCase) { ["nwp_riding_land"] = 2 },
            },
        };
        var clone = (AbilityDefinition)Invoke(typeof(RulesEngine), "CloneAbility", null, original);
        Equal(2, clone.Effect.NwpCheckBonusesById["NWP_RIDING_LAND"], "Clone preserves targeted bonus and comparer");
        Equal(false, ReferenceEquals(original.Effect.NwpCheckBonusesById, clone.Effect.NwpCheckBonusesById), "Targeted bonuses are deep copied");
        clone.Effect.NwpCheckBonusesById["nwp_riding_land"] = 9;
        Equal(2, original.Effect.NwpCheckBonusesById["nwp_riding_land"], "Clone mutation cannot change source ability");
        Console.WriteLine($"PASS: {checks} NWP accounting regression checks.");
    }
}