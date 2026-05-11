using System;
using System.Text.RegularExpressions;

namespace DungeonMasterCortex.Utilities;

/// <summary>
/// Parses and rolls dice expressions like "1d12", "2d6+3", "1d8-1", etc.
/// </summary>
public static class DiceRoller
{
    private static readonly Random _random = new();
    
    /// <summary>
    /// Rolls a dice expression and returns the total.
    /// Examples: "1d12", "2d6+3", "1d8-1"
    /// Returns 0 if the expression is invalid or null.
    /// </summary>
    public static int Roll(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return 0;

        try
        {
            return RollInternal(expression.Trim());
        }
        catch
        {
            return 0; // Fail gracefully on malformed expressions
        }
    }

    /// <summary>
    /// Gets the average roll for a dice expression without randomness.
    /// Useful for previewing expected values.
    /// </summary>
    public static int Average(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return 0;

        try
        {
            var match = Regex.Match(expression.Trim(), @"^(\d+)d(\d+)([\+\-]\d+)?$", RegexOptions.IgnoreCase);
            if (!match.Success)
                return 0;

            int numDice = int.Parse(match.Groups[1].Value);
            int dieSize = int.Parse(match.Groups[2].Value);
            int modifier = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;

            // Average of a die is (1 + max) / 2
            int dieAverage = (1 + dieSize) / 2;
            return (numDice * dieAverage) + modifier;
        }
        catch
        {
            return 0;
        }
    }

    private static int RollInternal(string expression)
    {
        // Pattern: NdM, NdM+X, NdM-X
        var match = Regex.Match(expression, @"^(\d+)d(\d+)([\+\-]\d+)?$", RegexOptions.IgnoreCase);
        
        if (!match.Success)
            throw new ArgumentException($"Invalid dice expression: {expression}");

        int numDice = int.Parse(match.Groups[1].Value);
        int dieSize = int.Parse(match.Groups[2].Value);
        int modifier = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;

        // Sanity checks
        if (numDice <= 0 || numDice > 100)
            throw new ArgumentException($"Number of dice must be between 1 and 100, got {numDice}");
        if (dieSize <= 0 || dieSize > 1000)
            throw new ArgumentException($"Die size must be between 1 and 1000, got {dieSize}");

        int total = 0;
        for (int i = 0; i < numDice; i++)
        {
            total += _random.Next(1, dieSize + 1);
        }

        return total + modifier;
    }
}
