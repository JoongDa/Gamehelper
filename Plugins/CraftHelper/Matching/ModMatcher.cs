namespace CraftHelper.Matching;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
/// Pure exact-ID matcher. Invalid input has precedence over both AND and OR, including
/// a duplicate ID whose required value cannot be read. No tier or display-unit inference.
/// </summary>
public static class ModMatcher
{
    public static MatchResult Evaluate(ModSnapshot? snapshot, TargetConditions? target)
    {
        if (target?.Conditions == null || target.Conditions.Count == 0)
        {
            return new(MatchStatus.Invalid, "Target conditions are missing or empty.", Array.Empty<ConditionResult>());
        }

        var commonInvalidReason = !Enum.IsDefined(target.Logic)
            ? "Unknown AND/OR condition logic."
            : ValidateSnapshot(snapshot);
        var results = target.Conditions.Select((condition, index) =>
            commonInvalidReason != null
                ? new ConditionResult(index, Describe(condition), MatchStatus.Invalid, commonInvalidReason, Array.Empty<ModEvidence>())
                : EvaluateCondition(snapshot!, condition, index)).ToArray();

        var invalid = results.FirstOrDefault(result => result.Status == MatchStatus.Invalid);
        if (invalid != null)
        {
            return new(MatchStatus.Invalid, $"Condition {invalid.ConditionIndex + 1}: {invalid.Reason}", Array.AsReadOnly(results));
        }

        var matchedCount = results.Count(result => result.Status == MatchStatus.Matched);
        var matched = target.Logic == ConditionLogic.And
            ? matchedCount == results.Length
            : matchedCount > 0;
        return new(
            matched ? MatchStatus.Matched : MatchStatus.NotMatched,
            $"{matchedCount}/{results.Length} conditions matched ({target.Logic.ToString().ToUpperInvariant()}).",
            Array.AsReadOnly(results));
    }

    private static string? ValidateSnapshot(ModSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            return "No current item snapshot.";
        }

        if (!snapshot.IsValid)
        {
            return string.IsNullOrWhiteSpace(snapshot.InvalidReason) ? "The item API data is invalid or unavailable." : snapshot.InvalidReason;
        }

        if (snapshot.Mods == null)
        {
            return "The snapshot has no mod collection.";
        }

        for (var index = 0; index < snapshot.Mods.Count; index++)
        {
            var mod = snapshot.Mods[index];
            if (mod == null || string.IsNullOrWhiteSpace(mod.Id) || mod.Values == null ||
                !Enum.IsDefined(mod.Category) || mod.Category == ModCategory.Any || mod.Values.Any(float.IsInfinity))
            {
                return $"Raw mod {index + 1} has invalid ID, category, or values.";
            }
        }

        return null;
    }

    private static ConditionResult EvaluateCondition(ModSnapshot snapshot, ModCondition? condition, int index)
    {
        var description = Describe(condition);
        ConditionResult Result(MatchStatus status, string reason, IReadOnlyList<ModEvidence>? evidence = null) =>
            new(index, description, status, reason, evidence ?? Array.Empty<ModEvidence>());

        var configurationError = ValidateCondition(condition);
        if (configurationError != null)
        {
            return Result(MatchStatus.Invalid, configurationError);
        }

        var configured = condition!;
        var matchingMods = snapshot.Mods.Where(mod =>
            string.Equals(mod.Id, configured.ModId, StringComparison.Ordinal) &&
            (configured.Category == ModCategory.Any || configured.Category == mod.Category)).ToArray();
        var evidence = new List<ModEvidence>(matchingMods.Length);

        foreach (var mod in matchingMods)
        {
            // Evidence owns the raw values, so later caller changes cannot alter an evaluation.
            var values = Array.AsReadOnly(mod.Values.ToArray());
            float? actualValue = null;
            bool? comparisonPassed = null;
            var detail = $"{mod.Category} {mod.Id}: ({string.Join(", ", values)})";
            if (configured.Kind == ConditionKind.Value)
            {
                if (configured.ValueIndex >= values.Count || float.IsNaN(values[configured.ValueIndex]))
                {
                    detail += $"; value{configured.ValueIndex} is unavailable.";
                }
                else
                {
                    actualValue = values[configured.ValueIndex];
                    comparisonPassed = Compare(actualValue.Value, configured);
                    detail += $"; value{configured.ValueIndex}={Format(actualValue.Value)} {DescribeComparison(configured)} => {comparisonPassed}.";
                }
            }

            evidence.Add(new(mod.Category, mod.Id, values, actualValue, comparisonPassed, detail));
        }

        var frozenEvidence = evidence.AsReadOnly();
        if (configured.Kind == ConditionKind.Missing)
        {
            return matchingMods.Length == 0
                ? Result(MatchStatus.Matched, $"Exact ID '{configured.ModId}' is absent in the available {configured.Category} Mods API data.")
                : Result(MatchStatus.NotMatched, string.Join("; ", evidence.Select(entry => entry.Detail)), frozenEvidence);
        }

        if (matchingMods.Length == 0)
        {
            return Result(MatchStatus.NotMatched, $"Exact ID '{configured.ModId}' was not found in {configured.Category}.");
        }

        if (configured.Kind == ConditionKind.Exists)
        {
            return Result(MatchStatus.Matched, string.Join("; ", evidence.Select(entry => entry.Detail)), frozenEvidence);
        }

        var evidenceReason = string.Join("; ", evidence.Select(entry => entry.Detail));
        if (evidence.Any(entry => entry.ActualValue == null))
        {
            return Result(MatchStatus.Invalid, evidenceReason, frozenEvidence);
        }

        return Result(evidence.Any(entry => entry.ComparisonPassed == true) ? MatchStatus.Matched : MatchStatus.NotMatched, evidenceReason, frozenEvidence);
    }

    private static string? ValidateCondition(ModCondition? condition)
    {
        if (condition == null || string.IsNullOrWhiteSpace(condition.ModId))
        {
            return "The condition or its exact ModId is missing.";
        }

        if (!Enum.IsDefined(condition.Category) || !Enum.IsDefined(condition.Kind))
        {
            return "Unknown mod category or condition kind.";
        }

        if (condition.Kind != ConditionKind.Value)
        {
            return condition.Minimum != null || condition.Maximum != null || condition.ValueIndex != 0 ||
                condition.Comparison != ValueComparison.GreaterThanOrEqual
                ? "Exists/Missing conditions cannot specify numeric bounds, a nondefault comparison, or a nonzero value index; use Kind Value for numeric comparisons."
                : null;
        }

        if (condition.ValueIndex is < 0 or > 1 || !Enum.IsDefined(condition.Comparison))
        {
            return "Value conditions require value0 or value1 and a supported comparison.";
        }

        return condition.Comparison switch
        {
            ValueComparison.GreaterThanOrEqual when condition.Maximum != null => ">= accepts Minimum only; remove Maximum or use Range.",
            ValueComparison.LessThanOrEqual when condition.Minimum != null => "<= accepts Maximum only; remove Minimum or use Range.",
            ValueComparison.GreaterThanOrEqual when condition.Minimum == null => ">= requires an integer Minimum.",
            ValueComparison.LessThanOrEqual when condition.Maximum == null => "<= requires an integer Maximum.",
            ValueComparison.Range when condition.Minimum == null || condition.Maximum == null => "Range requires integer Minimum and Maximum.",
            ValueComparison.Range when condition.Minimum > condition.Maximum => "Range Minimum must be <= Maximum.",
            _ => null,
        };
    }

    private static bool Compare(float actual, ModCondition condition) => condition.Comparison switch
    {
        ValueComparison.GreaterThanOrEqual => (double)actual >= condition.Minimum!.Value,
        ValueComparison.LessThanOrEqual => (double)actual <= condition.Maximum!.Value,
        ValueComparison.Range => (double)actual >= condition.Minimum!.Value && (double)actual <= condition.Maximum!.Value,
        _ => throw new InvalidOperationException("Comparison must be validated before evaluation."),
    };

    public static string Describe(ModCondition? condition) => condition == null
        ? "Invalid condition"
        : condition.Kind switch
        {
            ConditionKind.Exists => $"{condition.Category}: exact ID '{condition.ModId}' exists",
            ConditionKind.Missing => $"{condition.Category}: exact ID '{condition.ModId}' is missing",
            ConditionKind.Value => $"{condition.Category}: exact ID '{condition.ModId}', value{condition.ValueIndex} {DescribeComparison(condition)}",
            _ => $"Invalid condition kind for '{condition.ModId}'",
        };

    private static string DescribeComparison(ModCondition condition) => condition.Comparison switch
    {
        ValueComparison.GreaterThanOrEqual => $">= {Format(condition.Minimum)}",
        ValueComparison.LessThanOrEqual => $"<= {Format(condition.Maximum)}",
        ValueComparison.Range => $"in [{Format(condition.Minimum)}, {Format(condition.Maximum)}] (inclusive)",
        _ => "unsupported comparison",
    };

    private static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "?";
    private static string Format(float value) => value.ToString(CultureInfo.InvariantCulture);
}
