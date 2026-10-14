namespace CraftHelper.Matching;

using System;
using System.Collections.Generic;

public enum ModCategory { Explicit, Implicit, Enchant, Any }
public enum MatchStatus { Matched, NotMatched, Invalid }
public enum ConditionLogic { And, Or }
public enum ConditionKind { Exists, Missing, Value }
public enum ValueComparison { GreaterThanOrEqual, LessThanOrEqual, Range }

/// <summary>Copied Core Mods API data; NaN denotes an unavailable value slot.</summary>
public sealed class ModSnapshot
{
    // True means the caller's available API data passed local checks; it cannot certify a complete memory read.
    public bool IsValid { get; init; }
    public string InvalidReason { get; init; } = string.Empty;
    public IReadOnlyList<RawMod> Mods { get; init; } = Array.Empty<RawMod>();
}

public sealed class RawMod
{
    public ModCategory Category { get; init; }
    public string Id { get; init; } = string.Empty;
    public IReadOnlyList<float> Values { get; init; } = Array.Empty<float>();
}

/// <summary>Serializable configuration, independent of item type and the game process.</summary>
public sealed class TargetConditions
{
    public ConditionLogic Logic { get; set; } = ConditionLogic.And;
    public List<ModCondition> Conditions { get; set; } = new();
}

public sealed class ModCondition
{
    public string ModId { get; set; } = string.Empty;
    public ModCategory Category { get; set; } = ModCategory.Explicit;
    public ConditionKind Kind { get; set; } = ConditionKind.Exists;
    public int ValueIndex { get; set; }
    public ValueComparison Comparison { get; set; } = ValueComparison.GreaterThanOrEqual;
    public int? Minimum { get; set; }
    public int? Maximum { get; set; }
}

public sealed record MatchResult(
    MatchStatus Status,
    string Reason,
    IReadOnlyList<ConditionResult> Conditions);

public sealed record ConditionResult(
    int ConditionIndex,
    string Description,
    MatchStatus Status,
    string Reason,
    IReadOnlyList<ModEvidence> Evidence);

public sealed record ModEvidence(
    ModCategory Category,
    string ModId,
    IReadOnlyList<float> Values,
    float? ActualValue,
    bool? ComparisonPassed,
    string Detail);
