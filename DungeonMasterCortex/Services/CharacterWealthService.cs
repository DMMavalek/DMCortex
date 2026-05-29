using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Utilities;

namespace DungeonMasterCortex.Services;

public static class CharacterWealthService
{
    private static readonly Regex StartingFundsRollRegex = new(
        @"^(?<dice>\d+)d(?<sides>\d+)(?:\s*(?:x|\*)\s*(?<mult>\d+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, (int DiceCount, int DiceSides, int Multiplier)> StartingGoldByClass =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["fighter"] = (5, 4, 10),
            ["paladin"] = (5, 4, 10),
            ["ranger"] = (5, 4, 10),
            ["cleric"] = (3, 6, 10),
            ["druid"] = (3, 6, 10),
            ["thief"] = (2, 6, 10),
            ["bard"] = (3, 6, 10),
            ["wizard"] = (2, 4, 10),
            ["psionicist"] = (3, 4, 10),
        };

    public record StartingFundsResult(int GoldPieces, int SilverPieces, int CopperPieces, string Summary);

    public static int ToCopper(int pp, int gp, int sp, int cp)
        => Math.Max(0, pp) * 500 + Math.Max(0, gp) * 100 + Math.Max(0, sp) * 10 + Math.Max(0, cp);

    public static int ToCopper(int gp, int sp, int cp)
        => ToCopper(0, gp, sp, cp);

    public static void FromCopper(int totalCopper, out int gp, out int sp, out int cp)
    {
        int value = Math.Max(0, totalCopper);
        gp = value / 100;
        value %= 100;
        sp = value / 10;
        cp = value % 10;
    }

    public static int GetTotalCopper(CharacterSheet character)
        => ToCopper(character.PlatinumPieces, character.GoldPieces, character.SilverPieces, character.CopperPieces);

    private static void FromCopperExpanded(int totalCopper, out int pp, out int gp, out int sp, out int cp)
    {
        int value = Math.Max(0, totalCopper);
        pp = value / 500;
        value %= 500;
        gp = value / 100;
        value %= 100;
        sp = value / 10;
        cp = value % 10;
    }

    public static void FromCopperWithPlatinum(int totalCopper, out int pp, out int gp, out int sp, out int cp)
        => FromCopperExpanded(totalCopper, out pp, out gp, out sp, out cp);

    public static void SetFromCopper(CharacterSheet character, int totalCopper)
    {
        FromCopperExpanded(totalCopper, out int pp, out int gp, out int sp, out int cp);
        character.PlatinumPieces = pp;
        character.GoldPieces = gp;
        character.SilverPieces = sp;
        character.CopperPieces = cp;
    }

    public static bool TrySpend(CharacterSheet character, int gp, int sp, int cp, out string reason)
    {
        int spendCopper = ToCopper(gp, sp, cp);
        int available = GetTotalCopper(character);
        if (spendCopper <= 0)
        {
            reason = "Spend amount must be greater than zero.";
            return false;
        }
        if (available < spendCopper)
        {
            reason = $"Not enough funds. Need {FormatCoins(spendCopper)}, have {FormatCoins(available)}.";
            return false;
        }

        SetFromCopper(character, available - spendCopper);
        reason = string.Empty;
        return true;
    }

    public static void Add(CharacterSheet character, int gp, int sp, int cp)
        => Add(character, 0, gp, sp, cp);

    public static void Add(CharacterSheet character, int pp, int gp, int sp, int cp)
    {
        int addCopper = ToCopper(pp, gp, sp, cp);
        int total = GetTotalCopper(character);
        SetFromCopper(character, total + addCopper);
    }

    public static bool EnsureStartingFunds(CharacterSheet character)
    {
        if (character.StartingFundsAssigned)
            return false;

        string classId = character.ClassIds.Count > 0
            ? character.ClassIds[0]
            : character.ClassId;

        int startingGold = RollStartingGoldGp(classId);
        Add(character, startingGold, 0, 0);
        character.StartingFundsAssigned = true;
        return true;
    }

    public static StartingFundsResult RollStartingFundsForCharGen(CharGenState cg, RulesEngine rules)
    {
        var classIds = (cg.SelectedClassIds.Count > 0 ? cg.SelectedClassIds : new List<string> { cg.ClassId })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (classIds.Count == 0)
        {
            int fallback = RollStartingGoldGp(string.Empty);
            return new StartingFundsResult(fallback, 0, 0, $"Default roll {fallback} gp");
        }

        var classRolls = new List<(string ClassId, int Gold, string RollSpec)>();
        foreach (var classId in classIds)
        {
            string rollSpec = ResolveStartingFundsRollSpec(classId, rules);
            int gold = RollFromSpec(rollSpec);
            classRolls.Add((classId, gold, rollSpec));
        }

        var best = classRolls
            .OrderByDescending(x => x.Gold)
            .First();

        int finalGold = best.Gold;
        var summaryParts = new List<string>
        {
            classRolls.Count == 1
                ? $"{best.ClassId}: {best.RollSpec} -> {best.Gold} gp"
                : $"Best of {string.Join(", ", classRolls.Select(x => $"{x.ClassId} {x.RollSpec}={x.Gold}gp"))}"
        };

        if (!string.IsNullOrWhiteSpace(cg.KitId))
        {
            var kit = rules.Kits.FirstOrDefault(k => string.Equals(k.Id, cg.KitId, StringComparison.OrdinalIgnoreCase));
            if (kit is not null)
            {
                if (!string.IsNullOrWhiteSpace(kit.StartingFundsRollOverride))
                {
                    int overrideGold = RollFromSpec(kit.StartingFundsRollOverride);
                    finalGold = overrideGold;
                    summaryParts.Add($"kit override {kit.StartingFundsRollOverride} -> {overrideGold} gp");
                }

                if (kit.StartingFundsMultiplierPercent != 100)
                {
                    int adjusted = (int)Math.Round(finalGold * (kit.StartingFundsMultiplierPercent / 100.0), MidpointRounding.AwayFromZero);
                    summaryParts.Add($"kit multiplier {kit.StartingFundsMultiplierPercent}%: {finalGold} -> {adjusted} gp");
                    finalGold = adjusted;
                }

                if (kit.StartingFundsGoldBonus != 0)
                {
                    int adjusted = Math.Max(0, finalGold + kit.StartingFundsGoldBonus);
                    summaryParts.Add($"kit bonus {kit.StartingFundsGoldBonus:+#;-#;0} gp: {finalGold} -> {adjusted} gp");
                    finalGold = adjusted;
                }
            }
        }

        return new StartingFundsResult(Math.Max(0, finalGold), 0, 0, string.Join("; ", summaryParts));
    }

    public static int RollStartingGoldGp(string? classId)
    {
        string spec = ResolveStartingFundsRollSpec(classId ?? string.Empty, null);
        return RollFromSpec(spec);
    }

    private static string ResolveStartingFundsRollSpec(string classId, RulesEngine? rules)
    {
        if (rules is not null
            && rules.Classes.TryGetValue(classId, out var cls)
            && !string.IsNullOrWhiteSpace(cls.StartingFundsRoll))
        {
            return cls.StartingFundsRoll.Trim();
        }

        var fallback = StartingGoldByClass.TryGetValue((classId ?? string.Empty).Trim(), out var defaultSpec)
            ? defaultSpec
            : (DiceCount: 3, DiceSides: 6, Multiplier: 10);
        return $"{fallback.DiceCount}d{fallback.DiceSides}x{fallback.Multiplier}";
    }

    private static int RollFromSpec(string? spec)
    {
        string value = string.IsNullOrWhiteSpace(spec) ? "3d6x10" : spec.Trim();
        var match = StartingFundsRollRegex.Match(value);
        if (!match.Success)
            return RollStartingGoldGpFromTuple((3, 6, 10));

        int diceCount = int.Parse(match.Groups["dice"].Value);
        int diceSides = int.Parse(match.Groups["sides"].Value);
        int multiplier = match.Groups["mult"].Success ? int.Parse(match.Groups["mult"].Value) : 1;

        int rolled = DiceRoller.Roll($"{diceCount}d{diceSides}");
        if (rolled <= 0)
            rolled = RollStartingGoldGpFromTuple((3, 6, 10));

        return Math.Max(0, rolled * Math.Max(1, multiplier));
    }

    private static int RollStartingGoldGpFromTuple((int DiceCount, int DiceSides, int Multiplier) spec)
    {
        int total = 0;
        for (int i = 0; i < spec.DiceCount; i++)
            total += Random.Shared.Next(1, spec.DiceSides + 1);
        return total * spec.Multiplier;
    }

    public static string FormatCoins(int totalCopper)
    {
        FromCopperExpanded(totalCopper, out int pp, out int gp, out int sp, out int cp);
        return $"{pp} pp, {gp} gp, {sp} sp, {cp} cp";
    }

    public static string FormatCoins(CharacterSheet character)
    {
        int pp = Math.Max(0, character.PlatinumPieces);
        int gp = Math.Max(0, character.GoldPieces);
        int sp = Math.Max(0, character.SilverPieces);
        int cp = Math.Max(0, character.CopperPieces);
        int gems = Math.Max(0, character.GemCount);
        int gemValueGp = Math.Max(0, character.GemValueGoldPieces);
        return $"{pp} pp, {gp} gp, {sp} sp, {cp} cp, gems {gems} ({gemValueGp} gp value)";
    }
}
