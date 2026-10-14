using System.Text.Json;
using System.Text.Json.Serialization;
using CraftHelper;
using CraftHelper.Matching;

// Deliberately dependency-free: the tests run without a game, ImGui, or a test-framework restore.
var tests = new List<(string Name, Action Run)>
{
    ("AND matches both requested mods", () =>
    {
        var result = Evaluate(Equipment(), AtLeast("Strength7", 29), AtLeast("LocalCriticalMultiplier6", 23));
        Equal(MatchStatus.Matched, result.Status);
        Equal(2, result.Conditions.Count);
        Equal("Strength7", result.Conditions[0].Evidence[0].ModId);
        Equal(29, result.Conditions[0].Evidence[0].ActualValue);
        Equal(true, result.Conditions[0].Evidence[0].ComparisonPassed);
    }),
    ("AND fails when one value is below minimum", () =>
    {
        var result = Evaluate(Equipment(), AtLeast("Strength7", 30), AtLeast("LocalCriticalMultiplier6", 23));
        Equal(MatchStatus.NotMatched, result.Status);
        Equal(MatchStatus.Matched, result.Conditions[1].Status);
    }),
    ("OR succeeds when one condition matches", () =>
        Equal(MatchStatus.Matched, EvaluateWithLogic(Equipment(), ConditionLogic.Or, AtLeast("Strength7", 30), AtLeast("LocalCriticalMultiplier6", 23)).Status)),
    ("OR fails when no conditions match", () =>
        Equal(MatchStatus.NotMatched, EvaluateWithLogic(Equipment(), ConditionLogic.Or, AtLeast("Strength7", 30), Exists("Dexterity9")).Status)),
    (">= includes equality", () => Equal(MatchStatus.Matched, Evaluate(Equipment(), AtLeast("Strength7", 29)).Status)),
    (">= rejects below minimum", () => Equal(MatchStatus.NotMatched, Evaluate(Equipment(), AtLeast("Strength7", 30)).Status)),
    ("<= includes equality", () => Equal(MatchStatus.Matched, Evaluate(Equipment(), AtMost("Strength7", 29)).Status)),
    ("<= rejects above maximum", () => Equal(MatchStatus.NotMatched, Evaluate(Equipment(), AtMost("Strength7", 28)).Status)),
    ("Range includes lower endpoint", () => Equal(MatchStatus.Matched, Evaluate(Equipment(), InRange("Strength7", 29, 35)).Status)),
    ("Range includes upper endpoint", () => Equal(MatchStatus.Matched, Evaluate(Equipment(), InRange("Strength7", 20, 29)).Status)),
    ("Range includes interior value", () => Equal(MatchStatus.Matched, Evaluate(Equipment(), InRange("Strength7", 20, 35)).Status)),
    ("Range rejects outside both endpoints", () =>
    {
        Equal(MatchStatus.NotMatched, Evaluate(Equipment(), InRange("Strength7", 10, 20)).Status);
        Equal(MatchStatus.NotMatched, Evaluate(Equipment(), InRange("Strength7", 30, 40)).Status);
    }),
    ("Missing matches absent ID on an explicitly valid pure input", () => Equal(MatchStatus.Matched, Evaluate(Equipment(), Missing("Dexterity9")).Status)),
    ("Missing rejects an existing ID and shows its values", () =>
    {
        var result = Evaluate(Equipment(), Missing("Strength7"));
        Equal(MatchStatus.NotMatched, result.Status);
        Equal(29, result.Conditions[0].Evidence[0].Values[0]);
    }),
    ("An explicitly valid empty pure input can establish absence", () =>
    {
        Equal(MatchStatus.Matched, Evaluate(Snapshot(), Missing("Strength7")).Status);
        Equal(MatchStatus.NotMatched, Evaluate(Snapshot(), Exists("Strength7")).Status);
    }),
    ("Explicit Implicit and Enchant remain distinct", () =>
    {
        var snapshot = Snapshot(Mod("Strength7", ModCategory.Explicit, 29), Mod("Strength7", ModCategory.Implicit, 12), Mod("Strength7", ModCategory.Enchant, 40));
        Equal(MatchStatus.Matched, Evaluate(snapshot, AtLeast("Strength7", 29)).Status);
        Equal(MatchStatus.NotMatched, Evaluate(snapshot, AtLeast("Strength7", 29, ModCategory.Implicit)).Status);
        Equal(MatchStatus.Matched, Evaluate(snapshot, AtLeast("Strength7", 29, ModCategory.Enchant)).Status);
    }),
    ("Any category can match across the three real categories", () =>
        Equal(MatchStatus.Matched, Evaluate(Snapshot(Mod("Strength7", ModCategory.Enchant, 29)), AtLeast("Strength7", 29, ModCategory.Any)).Status)),
    ("Missing is scoped to configured category", () =>
    {
        var snapshot = Snapshot(Mod("Strength7", ModCategory.Implicit, 29));
        Equal(MatchStatus.Matched, Evaluate(snapshot, Missing("Strength7")).Status);
        Equal(MatchStatus.NotMatched, Evaluate(snapshot, Missing("Strength7", ModCategory.Any)).Status);
    }),
    ("ID suffix is exact and cannot be treated as a tier", () =>
    {
        Equal(MatchStatus.NotMatched, Evaluate(Equipment(), Exists("Strength"), Exists("Strength6")).Status);
        Equal(MatchStatus.Matched, Evaluate(Equipment(), Missing("Strength6")).Status);
    }),
    ("ID comparison is ordinal and case sensitive", () => Equal(MatchStatus.NotMatched, Evaluate(Equipment(), Exists("strength7")).Status)),
    ("Value1 can be selected independently of value0", () =>
        Equal(MatchStatus.Matched, Evaluate(Snapshot(Mod("DualRoll", ModCategory.Explicit, 100, 23)), AtLeast("DualRoll", 23, valueIndex: 1)).Status)),
    ("Core float values remain unscaled and integer thresholds keep their precision", () =>
    {
        var snapshot = Snapshot(Mod("RawMaximum", ModCategory.Explicit, int.MaxValue), Mod("RawMinimum", ModCategory.Explicit, int.MinValue));
        Equal(MatchStatus.Matched, Evaluate(snapshot, AtLeast("RawMaximum", int.MaxValue), AtMost("RawMinimum", int.MinValue)).Status);
        Equal(MatchStatus.NotMatched, Evaluate(snapshot, AtMost("RawMaximum", 100)).Status);
        // Core's float has already rounded Int32.MaxValue up to 2147483648; do not round the threshold to match it.
        Equal(MatchStatus.NotMatched, Evaluate(snapshot, AtMost("RawMaximum", int.MaxValue)).Status);
        Equal((float)int.MaxValue, Evaluate(snapshot, Exists("RawMaximum")).Conditions[0].Evidence[0].Values[0]);
        Equal(MatchStatus.NotMatched, Evaluate(snapshot, InRange("RawMaximum", int.MaxValue, int.MaxValue)).Status);
        Equal(MatchStatus.Matched, Evaluate(Snapshot(Mod("Fractional", ModCategory.Explicit, 29.5f)), InRange("Fractional", 29, 30)).Status);
    }),
    ("Negative and zero values are valid raw numbers", () =>
        Equal(MatchStatus.Matched, Evaluate(Snapshot(Mod("Negative", ModCategory.Explicit, -8), Mod("Zero", ModCategory.Explicit, 0)), InRange("Negative", -10, -5), InRange("Zero", 0, 0)).Status)),
    ("Null current snapshot is Invalid", () => Equal(MatchStatus.Invalid, Evaluate(null, Exists("Strength7")).Status)),
    ("Failed or incomplete read remains Invalid including Missing", () =>
    {
        var result = Evaluate(new ModSnapshot { IsValid = false, InvalidReason = "Item address changed during read.", Mods = Equipment().Mods }, Missing("Dexterity9"));
        Equal(MatchStatus.Invalid, result.Status);
        Contains("address changed", result.Reason);
    }),
    ("Malformed unrelated raw ID or infinite value invalidates the snapshot", () =>
    {
        Equal(MatchStatus.Invalid, Evaluate(Snapshot(Mod("Strength7", ModCategory.Explicit, 29), Mod("", ModCategory.Implicit, 3)), Missing("Dexterity9")).Status);
        Equal(MatchStatus.Invalid, Evaluate(Snapshot(Mod("Strength7", ModCategory.Explicit, 29), Mod("Bad", ModCategory.Implicit, float.PositiveInfinity)), Missing("Dexterity9")).Status);
    }),
    ("Null raw mod collection is Invalid", () =>
        Equal(MatchStatus.Invalid, Evaluate(new ModSnapshot { IsValid = true, Mods = null! }, Exists("Strength7")).Status)),
    ("Null raw mod instance is Invalid", () => Equal(MatchStatus.Invalid, Evaluate(Snapshot(new RawMod[] { null! }), Missing("Strength7")).Status)),
    ("Null raw value collection is Invalid", () =>
        Equal(MatchStatus.Invalid, Evaluate(Snapshot(new RawMod { Id = "Strength7", Values = null! }), Exists("Strength7")).Status)),
    ("Unknown or Any raw category is Invalid", () =>
    {
        Equal(MatchStatus.Invalid, Evaluate(Snapshot(Mod("Strength7", (ModCategory)99, 29)), Exists("Strength7")).Status);
        Equal(MatchStatus.Invalid, Evaluate(Snapshot(Mod("Strength7", ModCategory.Any, 29)), Exists("Strength7")).Status);
    }),
    ("A requested value missing from an existing mod is Invalid", () =>
    {
        var result = Evaluate(Equipment(), AtLeast("Strength7", 1, valueIndex: 1));
        Equal(MatchStatus.Invalid, result.Status);
        Equal(29, result.Conditions[0].Evidence[0].Values[0]);
        Equal(null, result.Conditions[0].Evidence[0].ActualValue);
        Contains("value1 is unavailable", result.Reason);
    }),
    ("Exists supports a known valueless mod", () => Equal(MatchStatus.Matched, Evaluate(Snapshot(Mod("Valueless", ModCategory.Explicit)), Exists("Valueless")).Status)),
    ("Empty or null target is Invalid", () =>
    {
        Equal(MatchStatus.Invalid, ModMatcher.Evaluate(Equipment(), null).Status);
        Equal(MatchStatus.Invalid, ModMatcher.Evaluate(Equipment(), new TargetConditions()).Status);
        Equal(MatchStatus.Invalid, ModMatcher.Evaluate(Equipment(), new TargetConditions { Conditions = null! }).Status);
    }),
    ("Null or blank condition is Invalid", () =>
    {
        Equal(MatchStatus.Invalid, Evaluate(Equipment(), new ModCondition[] { null! }).Status);
        Equal(MatchStatus.Invalid, Evaluate(Equipment(), Exists(" ")).Status);
    }),
    ("Unknown condition enum values are Invalid", () =>
    {
        Equal(MatchStatus.Invalid, Evaluate(Equipment(), new ModCondition { ModId = "Strength7", Category = (ModCategory)99 }).Status);
        Equal(MatchStatus.Invalid, Evaluate(Equipment(), new ModCondition { ModId = "Strength7", Kind = (ConditionKind)99 }).Status);
        Equal(MatchStatus.Invalid, Evaluate(Equipment(), new ModCondition { ModId = "Strength7", Kind = ConditionKind.Value, Comparison = (ValueComparison)99, Minimum = 0 }).Status);
        Equal(MatchStatus.Invalid, EvaluateWithLogic(Equipment(), (ConditionLogic)99, Exists("Strength7")).Status);
    }),
    ("Missing numeric bound is Invalid rather than silently zero", () =>
    {
        Equal(MatchStatus.Invalid, Evaluate(Equipment(), new ModCondition { ModId = "Strength7", Kind = ConditionKind.Value }).Status);
        Equal(MatchStatus.Invalid, Evaluate(Equipment(), new ModCondition { ModId = "Strength7", Kind = ConditionKind.Value, Comparison = ValueComparison.LessThanOrEqual }).Status);
        Equal(MatchStatus.Invalid, Evaluate(Equipment(), new ModCondition { ModId = "Strength7", Kind = ConditionKind.Value, Comparison = ValueComparison.Range, Minimum = 20 }).Status);
    }),
    ("Exists and Missing cannot silently ignore numeric configuration", () =>
    {
        foreach (var kind in new[] { ConditionKind.Exists, ConditionKind.Missing })
        {
            Equal(MatchStatus.Invalid, Evaluate(Equipment(), new ModCondition { ModId = "Strength7", Kind = kind, Minimum = 0 }).Status);
            Equal(MatchStatus.Invalid, Evaluate(Equipment(), new ModCondition { ModId = "Strength7", Kind = kind, Maximum = 29 }).Status);
            Equal(MatchStatus.Invalid, Evaluate(Equipment(), new ModCondition { ModId = "Strength7", Kind = kind, ValueIndex = 1 }).Status);
            Equal(MatchStatus.Invalid, Evaluate(Equipment(), new ModCondition { ModId = "Strength7", Kind = kind, Comparison = ValueComparison.LessThanOrEqual }).Status);
        }
    }),
    ("One-sided comparisons cannot silently ignore the opposite bound", () =>
    {
        Equal(MatchStatus.Invalid, Evaluate(Equipment(), new ModCondition { ModId = "Strength7", Kind = ConditionKind.Value, Comparison = ValueComparison.GreaterThanOrEqual, Minimum = 20, Maximum = 40 }).Status);
        Equal(MatchStatus.Invalid, Evaluate(Equipment(), new ModCondition { ModId = "Strength7", Kind = ConditionKind.Value, Comparison = ValueComparison.LessThanOrEqual, Minimum = 20, Maximum = 40 }).Status);
    }),
    ("Reversed range is Invalid", () => Equal(MatchStatus.Invalid, Evaluate(Equipment(), InRange("Strength7", 30, 20)).Status)),
    ("Unsupported value index is Invalid even when ID is absent", () =>
    {
        Equal(MatchStatus.Invalid, Evaluate(Equipment(), AtLeast("Absent", 0, valueIndex: -1)).Status);
        Equal(MatchStatus.Invalid, Evaluate(Equipment(), AtLeast("Absent", 0, valueIndex: 2)).Status);
    }),
    ("Duplicate exact IDs match when any available value qualifies", () =>
    {
        var result = Evaluate(Snapshot(Mod("Strength7", ModCategory.Explicit, 12), Mod("Strength7", ModCategory.Explicit, 29)), AtLeast("Strength7", 29));
        Equal(MatchStatus.Matched, result.Status);
        Equal(2, result.Conditions[0].Evidence.Count);
        Equal(false, result.Conditions[0].Evidence[0].ComparisonPassed);
        Equal(true, result.Conditions[0].Evidence[1].ComparisonPassed);
    }),
    ("An unavailable duplicate value cannot be hidden by a qualifying duplicate", () =>
        Equal(MatchStatus.Invalid, Evaluate(Snapshot(Mod("Strength7", ModCategory.Explicit), Mod("Strength7", ModCategory.Explicit, 29)), AtLeast("Strength7", 29)).Status)),
    ("OR cannot hide an Invalid numeric read", () =>
    {
        var result = EvaluateWithLogic(Equipment(), ConditionLogic.Or, Exists("Strength7"), AtLeast("LocalCriticalMultiplier6", 1, valueIndex: 1));
        Equal(MatchStatus.Invalid, result.Status);
        Equal(MatchStatus.Matched, result.Conditions[0].Status);
        Equal(MatchStatus.Invalid, result.Conditions[1].Status);
    }),
    ("AND cannot hide Invalid behind NotMatched", () =>
    {
        var result = Evaluate(Equipment(), Exists("Absent"), AtLeast("Strength7", 1, valueIndex: 1));
        Equal(MatchStatus.Invalid, result.Status);
        Equal(MatchStatus.NotMatched, result.Conditions[0].Status);
        Equal(MatchStatus.Invalid, result.Conditions[1].Status);
    }),
    ("OR cannot hide an Invalid configuration and evaluates every condition", () =>
    {
        var result = EvaluateWithLogic(Equipment(), ConditionLogic.Or, Exists("Strength7"), InRange("Strength7", 40, 20), Missing("Absent"));
        Equal(MatchStatus.Invalid, result.Status);
        Equal(3, result.Conditions.Count);
        Equal(MatchStatus.Matched, result.Conditions[2].Status);
    }),
    ("Equipment waystone and tablet IDs use the same entry point", () =>
    {
        foreach (var id in new[] { "Strength7", "MapMonsterDamageAsFire", "TowerDroppedItemRarityIncrease" })
        {
            Equal(MatchStatus.Matched, Evaluate(Snapshot(Mod(id, ModCategory.Explicit, 29)), AtLeast(id, 29)).Status);
        }
    }),
    ("Fresh snapshots never retain a previous item result", () =>
    {
        var conditions = Target(ConditionLogic.And, AtLeast("Strength7", 29));
        Equal(MatchStatus.Matched, ModMatcher.Evaluate(Equipment(), conditions).Status);
        Equal(MatchStatus.NotMatched, ModMatcher.Evaluate(Snapshot(Mod("Strength7", ModCategory.Explicit, 20)), conditions).Status);
        Equal(MatchStatus.Invalid, ModMatcher.Evaluate(null, conditions).Status);
    }),
    ("Evidence keeps original raw values after caller mutation", () =>
    {
        var values = new[] { 29f };
        var result = Evaluate(Snapshot(new RawMod { Category = ModCategory.Explicit, Id = "Strength7", Values = values }), AtLeast("Strength7", 29));
        values[0] = 1;
        Equal(29, result.Conditions[0].Evidence[0].Values[0]);
        Equal(29, result.Conditions[0].Evidence[0].ActualValue);
    }),
    ("JSON targets preserve enum names, zero, negative numbers and conditions", () =>
    {
        const string json = """
        { "Logic": "And", "Conditions": [
          { "ModId": "Strength7", "Category": "Explicit", "Kind": "Value", "ValueIndex": 0, "Comparison": "GreaterThanOrEqual", "Minimum": 29 },
          { "ModId": "LocalCriticalMultiplier6", "Category": "Explicit", "Kind": "Value", "ValueIndex": 0, "Comparison": "Range", "Minimum": 0, "Maximum": 23 },
          { "ModId": "Absent", "Category": "Any", "Kind": "Missing" }
        ] }
        """;
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var target = JsonSerializer.Deserialize<TargetConditions>(json, options);
        Equal(MatchStatus.Matched, ModMatcher.Evaluate(Equipment(), target).Status);
        var roundTrip = JsonSerializer.Deserialize<TargetConditions>(JsonSerializer.Serialize(target, options), options)!;
        Equal(0, roundTrip.Conditions[1].Minimum);
        Equal(MatchStatus.Matched, ModMatcher.Evaluate(Equipment(), roundTrip).Status);
        var negative = JsonSerializer.Deserialize<TargetConditions>("""{"Conditions":[{"ModId":"Negative","Kind":"Value","Minimum":-10}]}""", options);
        Equal(MatchStatus.Matched, ModMatcher.Evaluate(Snapshot(Mod("Negative", ModCategory.Explicit, -8)), negative).Status);
    }),
    ("Shipped example parses and matches the equipment example", () =>
    {
        Equal(true, ConditionConfiguration.TryParse(ConditionConfiguration.ExampleJson, out var target, out var error));
        Equal(string.Empty, error);
        Equal(MatchStatus.Matched, ModMatcher.Evaluate(Equipment(), target).Status);
    }),
    ("Configuration rejects unsupported Tier fields", () =>
    {
        InvalidJson("""{"Tier":1,"Conditions":[{"ModId":"Strength7"}]}""");
        InvalidJson("""{"Conditions":[{"ModId":"Strength7","Tier":"T1"}]}""");
    }),
    ("Configuration rejects unknown enum names", () =>
    {
        InvalidJson("""{"Logic":"Xor","Conditions":[{"ModId":"Strength7"}]}""");
        InvalidJson("""{"Conditions":[{"ModId":"Strength7","Category":"Suffix"}]}""");
        InvalidJson("""{"Conditions":[{"ModId":"Strength7","Kind":"Tier"}]}""");
        InvalidJson("""{"Conditions":[{"ModId":"Strength7","Kind":"Value","Comparison":"Equal","Minimum":29}]}""");
    }),
    ("Configuration rejects integer enum values", () =>
    {
        InvalidJson("""{"Logic":0,"Conditions":[{"ModId":"Strength7"}]}""");
        InvalidJson("""{"Conditions":[{"ModId":"Strength7","Category":99}]}""");
    }),
    ("Configuration rejects malformed JSON", () =>
    {
        InvalidJson("{ broken json");
        InvalidJson("""{"Conditions":[{"ModId":"Strength7",}]}""");
    }),
    ("Configuration rejects null and empty targets", () =>
    {
        InvalidJson("null");
        InvalidJson(string.Empty);
        InvalidJson("{}");
        InvalidJson("""{"Conditions":[]}""");
        InvalidJson("""{"Conditions":null}""");
        InvalidJson("""{"Conditions":[null]}""");
    }),
    ("Configuration rejects out-of-range or fractional integer thresholds", () =>
    {
        InvalidJson("""{"Conditions":[{"ModId":"Strength7","Kind":"Value","Minimum":2147483648}]}""");
        InvalidJson("""{"Conditions":[{"ModId":"Strength7","Kind":"Value","Minimum":-2147483649}]}""");
        InvalidJson("""{"Conditions":[{"ModId":"Strength7","Kind":"Value","Minimum":29.5}]}""");
    }),
    ("Configuration rejects semantic errors before any game read", () =>
    {
        InvalidJson("""{"Conditions":[{"ModId":"Strength7","Kind":"Value"}]}""");
        InvalidJson("""{"Conditions":[{"ModId":"Strength7","Kind":"Value","ValueIndex":2,"Minimum":29}]}""");
        InvalidJson("""{"Conditions":[{"ModId":"Strength7","Kind":"Value","Comparison":"Range","Minimum":40,"Maximum":20}]}""");
        InvalidJson("""{"Conditions":[{"ModId":"","Kind":"Exists"}]}""");
    }),
    ("Configuration rejects ambiguous numeric settings on Exists or Missing", () =>
    {
        InvalidJson("""{"Conditions":[{"ModId":"Strength7","Minimum":29}]}""");
        foreach (var kind in new[] { "Exists", "Missing" })
        {
            InvalidJson($$"""{"Conditions":[{"ModId":"Strength7","Kind":"{{kind}}","Minimum":0}]}""");
            InvalidJson($$"""{"Conditions":[{"ModId":"Strength7","Kind":"{{kind}}","Maximum":29}]}""");
            InvalidJson($$"""{"Conditions":[{"ModId":"Strength7","Kind":"{{kind}}","ValueIndex":1}]}""");
            InvalidJson($$"""{"Conditions":[{"ModId":"Strength7","Kind":"{{kind}}","Comparison":"LessThanOrEqual"}]}""");
        }
    }),
    ("Configuration rejects opposite bounds for one-sided comparisons", () =>
    {
        InvalidJson("""{"Conditions":[{"ModId":"Strength7","Kind":"Value","Comparison":"GreaterThanOrEqual","Minimum":20,"Maximum":40}]}""");
        InvalidJson("""{"Conditions":[{"ModId":"Strength7","Kind":"Value","Comparison":"LessThanOrEqual","Minimum":20,"Maximum":40}]}""");
    }),
    ("A failed parse clears a previously parsed configuration", () =>
    {
        Equal(true, ConditionConfiguration.TryParse(ConditionConfiguration.ExampleJson, out var target, out _));
        Equal(false, ConditionConfiguration.TryParse("{ broken json", out target, out var error));
        Equal<TargetConditions?>(null, target);
        Contains("Invalid target JSON", error);
        Equal(MatchStatus.Invalid, ModMatcher.Evaluate(Equipment(), target).Status);
    }),
    ("Saved outer settings preserve target JSON without falling back", () =>
    {
        var original = new CraftHelperSettings { TargetConditionsJson = ConditionConfiguration.ExampleJson };
        var saved = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<CraftHelperSettings>(saved)!;
        Equal(original.TargetConditionsJson, restored.TargetConditionsJson);
        Equal(true, ConditionConfiguration.TryParse(restored.TargetConditionsJson, out var target, out _));
        Equal(MatchStatus.Matched, ModMatcher.Evaluate(Equipment(), target).Status);
    }),
    ("Outer settings reject missing required target JSON", () => RejectSettings("{}")),
    ("Outer settings reject unknown or misspelled target fields", () =>
    {
        RejectSettings("""{"TargetConditionJson":"null"}""");
        RejectSettings("""{"TargetConditionsJson":"null","Typo":true}""");
    }),
    ("Outer settings reject non-string target JSON", () =>
    {
        RejectSettings("""{"TargetConditionsJson":29}""");
        RejectSettings("""{"TargetConditionsJson":{}}""");
    }),
    ("An explicit null saved target is Invalid rather than example fallback", () =>
    {
        var restored = JsonSerializer.Deserialize<CraftHelperSettings>("""{"TargetConditionsJson":null}""")!;
        Equal<string?>(null, restored.TargetConditionsJson);
        Equal(false, ConditionConfiguration.TryParse(restored.TargetConditionsJson, out var target, out var error));
        Equal<TargetConditions?>(null, target);
        Equal(MatchStatus.Invalid, ModMatcher.Evaluate(Equipment(), target).Status);
        Contains("Invalid target JSON", error);
    }),
    ("Invalid saved inner JSON remains Invalid rather than example fallback", () =>
    {
        var restored = JsonSerializer.Deserialize<CraftHelperSettings>("""{"TargetConditionsJson":"null"}""")!;
        Equal("null", restored.TargetConditionsJson);
        Equal(false, ConditionConfiguration.TryParse(restored.TargetConditionsJson, out var target, out _));
        Equal(MatchStatus.Invalid, ModMatcher.Evaluate(Equipment(), target).Status);
    }),
};

AdapterTests.Run((name, run) => tests.Add((name, run)));

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {error.Message}");
    }
}

Console.WriteLine($"CraftHelper: {tests.Count - failures}/{tests.Count} tests passed.");
return failures == 0 ? 0 : 1;

static RawMod Mod(string id, ModCategory category, params float[] values) => new() { Id = id, Category = category, Values = values };
static ModSnapshot Snapshot(params RawMod[] mods) => new() { IsValid = true, Mods = mods };
static ModSnapshot Equipment() => Snapshot(Mod("Strength7", ModCategory.Explicit, 29), Mod("LocalCriticalMultiplier6", ModCategory.Explicit, 23));
static ModCondition Exists(string id, ModCategory category = ModCategory.Explicit) => new() { ModId = id, Category = category, Kind = ConditionKind.Exists };
static ModCondition Missing(string id, ModCategory category = ModCategory.Explicit) => new() { ModId = id, Category = category, Kind = ConditionKind.Missing };
static ModCondition AtLeast(string id, int minimum, ModCategory category = ModCategory.Explicit, int valueIndex = 0) => new() { ModId = id, Category = category, Kind = ConditionKind.Value, ValueIndex = valueIndex, Comparison = ValueComparison.GreaterThanOrEqual, Minimum = minimum };
static ModCondition AtMost(string id, int maximum, ModCategory category = ModCategory.Explicit) => new() { ModId = id, Category = category, Kind = ConditionKind.Value, Comparison = ValueComparison.LessThanOrEqual, Maximum = maximum };
static ModCondition InRange(string id, int minimum, int maximum) => new() { ModId = id, Kind = ConditionKind.Value, Comparison = ValueComparison.Range, Minimum = minimum, Maximum = maximum };
static TargetConditions Target(ConditionLogic logic, params ModCondition[] conditions) => new() { Logic = logic, Conditions = conditions.ToList() };
static MatchResult Evaluate(ModSnapshot? snapshot, params ModCondition[] conditions) => ModMatcher.Evaluate(snapshot, Target(ConditionLogic.And, conditions));
static MatchResult EvaluateWithLogic(ModSnapshot? snapshot, ConditionLogic logic, params ModCondition[] conditions) => ModMatcher.Evaluate(snapshot, Target(logic, conditions));
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
    }
}

static void Contains(string expected, string actual)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
    }
}

static void InvalidJson(string json)
{
    Equal(false, ConditionConfiguration.TryParse(json, out var target, out var error));
    Equal<TargetConditions?>(null, target);
    if (string.IsNullOrWhiteSpace(error))
    {
        throw new InvalidOperationException("An invalid configuration must explain its failure.");
    }
}

static void RejectSettings(string json)
{
    try
    {
        JsonSerializer.Deserialize<CraftHelperSettings>(json);
    }
    catch (JsonException)
    {
        return;
    }

    throw new InvalidOperationException("Invalid outer settings must be rejected without defaulting to the example target.");
}
