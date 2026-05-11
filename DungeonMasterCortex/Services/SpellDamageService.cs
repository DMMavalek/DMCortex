using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public static class SpellDamageService
{
    public static bool HasDamageConfiguration(SpellDefinition spell)
    {
        return !string.IsNullOrWhiteSpace(spell.Damage)
            || !string.IsNullOrWhiteSpace(spell.DamageStep)
            || !string.IsNullOrWhiteSpace(spell.DamageMax)
            || !string.IsNullOrWhiteSpace(spell.DamageMaxAtLevel)
            || !string.IsNullOrWhiteSpace(spell.DamageScaleStartLevel)
            || !string.IsNullOrWhiteSpace(spell.DamageScaleEveryLevels);
    }

    public static string BuildDamageDisplay(SpellDefinition spell, int casterLevel)
    {
        if (TryBuildScaledDamageExpression(spell, casterLevel, out string expression))
            return spell.IsHealing ? $"Heals {expression} HP" : expression;

        string summary = BuildConfiguredDamageSummary(spell);
        return spell.IsHealing ? $"Heals {summary}" : summary;
    }

    public static bool TryBuildScaledDamageExpression(SpellDefinition spell, int casterLevel, out string expression)
    {
        expression = string.Empty;

        if (string.IsNullOrWhiteSpace(spell.Damage))
            return false;

        if (!TryParseDamageExpression(spell.Damage, out var totalDamage))
            return false;

        int? startLevel = TryParsePositiveInt(spell.DamageScaleStartLevel);
        int? everyLevels = TryParsePositiveInt(spell.DamageScaleEveryLevels);
        int? maxAtLevel = TryParsePositiveInt(spell.DamageMaxAtLevel);

        if (!string.IsNullOrWhiteSpace(spell.DamageStep))
        {
            if (!startLevel.HasValue || !everyLevels.HasValue)
                return false;

            if (!TryParseDamageExpression(spell.DamageStep, out var stepDamage))
                return false;

            int effectiveLevel = maxAtLevel.HasValue
                ? Math.Min(casterLevel, maxAtLevel.Value)
                : casterLevel;

            if (effectiveLevel >= startLevel.Value)
            {
                int increments = (effectiveLevel - startLevel.Value) / everyLevels.Value;

                // For scales that begin after level 1 (e.g., Magic Missile at level 3),
                // include the first step at the start level itself.
                if (startLevel.Value > 1)
                    increments += 1;

                totalDamage.Add(stepDamage, increments);
            }
        }

        if (maxAtLevel.HasValue
            && casterLevel >= maxAtLevel.Value
            && !string.IsNullOrWhiteSpace(spell.DamageMax)
            && TryParseDamageExpression(spell.DamageMax, out var cappedDamage))
        {
            expression = cappedDamage.ToDisplayString();
            return true;
        }

        expression = totalDamage.ToDisplayString();
        return true;
    }

    private static string BuildConfiguredDamageSummary(SpellDefinition spell)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(spell.Damage))
            parts.Add($"Base {spell.Damage.Trim()}");
        if (!string.IsNullOrWhiteSpace(spell.DamageStep))
            parts.Add($"+{spell.DamageStep.Trim()}");

        if (!string.IsNullOrWhiteSpace(spell.DamageScaleEveryLevels))
        {
            string cadence = $"every {spell.DamageScaleEveryLevels.Trim()} level";
            if (!string.Equals(spell.DamageScaleEveryLevels.Trim(), "1", StringComparison.Ordinal))
                cadence += "s";

            if (!string.IsNullOrWhiteSpace(spell.DamageScaleStartLevel))
                parts.Add($"from level {spell.DamageScaleStartLevel.Trim()}, {cadence}");
            else
                parts.Add(cadence);
        }
        else if (!string.IsNullOrWhiteSpace(spell.DamageScaleStartLevel))
        {
            parts.Add($"starts at level {spell.DamageScaleStartLevel.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(spell.DamageMaxAtLevel) && !string.IsNullOrWhiteSpace(spell.DamageMax))
            parts.Add($"caps at level {spell.DamageMaxAtLevel.Trim()} ({spell.DamageMax.Trim()})");
        else if (!string.IsNullOrWhiteSpace(spell.DamageMaxAtLevel))
            parts.Add($"caps at level {spell.DamageMaxAtLevel.Trim()}");
        else if (!string.IsNullOrWhiteSpace(spell.DamageMax))
            parts.Add($"max damage {spell.DamageMax.Trim()}");

        return parts.Count > 0 ? string.Join("; ", parts) : "No damage configured.";
    }

    private static int? TryParsePositiveInt(string? text)
    {
        return int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value
            : null;
    }

    private static bool TryParseDamageExpression(string? text, out DamageExpression expression)
    {
        expression = new DamageExpression();
        string input = (text ?? string.Empty).Replace(" ", string.Empty);
        if (string.IsNullOrWhiteSpace(input))
            return false;

        int index = 0;
        while (index < input.Length)
        {
            int sign = 1;
            if (input[index] == '+' || input[index] == '-')
            {
                sign = input[index] == '-' ? -1 : 1;
                index++;
            }

            int start = index;
            while (index < input.Length && input[index] != '+' && input[index] != '-')
                index++;

            if (start == index)
                return false;

            string token = input[start..index];
            if (token.Contains('d', StringComparison.OrdinalIgnoreCase))
            {
                if (sign < 0)
                    return false;

                string[] parts = token.Split(['d', 'D'], StringSplitOptions.None);
                if (parts.Length != 2)
                    return false;

                string countText = string.IsNullOrWhiteSpace(parts[0]) ? "1" : parts[0];
                if (!int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int diceCount) || diceCount <= 0)
                    return false;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int diceSides) || diceSides <= 0)
                    return false;

                expression.AddDice(diceSides, diceCount);
            }
            else
            {
                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int flatValue))
                    return false;

                expression.FlatBonus += sign * flatValue;
            }
        }

        return expression.HasContent;
    }

    private sealed class DamageExpression
    {
        private readonly Dictionary<int, int> _diceBySides = new();

        public int FlatBonus { get; set; }

        public bool HasContent => _diceBySides.Count > 0 || FlatBonus != 0;

        public void AddDice(int sides, int count)
        {
            _diceBySides[sides] = _diceBySides.TryGetValue(sides, out int existing)
                ? existing + count
                : count;
        }

        public void Add(DamageExpression other, int multiplier)
        {
            if (multiplier <= 0)
                return;

            foreach (var kv in other._diceBySides)
                AddDice(kv.Key, kv.Value * multiplier);

            FlatBonus += other.FlatBonus * multiplier;
        }

        public string ToDisplayString()
        {
            var parts = _diceBySides
                .Where(kv => kv.Value > 0)
                .OrderBy(kv => kv.Key)
                .Select(kv => $"{kv.Value}d{kv.Key}")
                .ToList();

            if (FlatBonus > 0)
                parts.Add($"+{FlatBonus}");
            else if (FlatBonus < 0)
                parts.Add(FlatBonus.ToString(CultureInfo.InvariantCulture));

            if (parts.Count == 0)
                return "0";

            string expression = string.Concat(parts);
            return expression.StartsWith('+') ? expression[1..] : expression;
        }
    }
}