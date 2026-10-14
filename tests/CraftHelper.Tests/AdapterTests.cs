using CraftHelper;
using CraftHelper.Matching;

internal static class AdapterTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("Adapter copies public Mods IDs, categories and float values without scaling", () =>
        {
            var snapshot = Snapshot([("Strength7", (29f, float.NaN))], [("Strength7", (12f, float.NaN))], [("DualRoll", (23.5f, -8f))]);
            Equal(true, snapshot.IsValid);
            Equal(3, snapshot.Mods.Count);
            Equal(ModCategory.Explicit, snapshot.Mods[0].Category);
            Equal(ModCategory.Implicit, snapshot.Mods[1].Category);
            Equal(ModCategory.Enchant, snapshot.Mods[2].Category);
            Equal(23.5f, snapshot.Mods[2].Values[0]);
            Equal(MatchStatus.Matched, ModMatcher.Evaluate(snapshot, Target(
                AtLeast("Strength7", 29), AtLeast("Strength7", 12, ModCategory.Implicit), AtLeast("DualRoll", -8, ModCategory.Enchant, 1))).Status);
        });

        test("A failed scan cannot reuse earlier lists or a previous match", () =>
        {
            var snapshot = ModSnapshotAdapter.Create(false, [("Strength7", (29f, float.NaN))], [], [], "Mouse no longer hovers an item.");
            Equal(false, snapshot.IsValid);
            Equal(0, snapshot.Mods.Count);
            Equal(MatchStatus.Invalid, ModMatcher.Evaluate(snapshot, Target(AtLeast("Strength7", 29))).Status);
            Equal("Mouse no longer hovers an item.", snapshot.InvalidReason);
        });

        test("A null list in any category is Invalid including Missing", () =>
        {
            foreach (var snapshot in new[]
            {
                ModSnapshotAdapter.Create(true, null, [], [], "Read failed."),
                ModSnapshotAdapter.Create(true, [("Strength7", (29f, float.NaN))], null, [], "Read failed."),
                ModSnapshotAdapter.Create(true, [("Strength7", (29f, float.NaN))], [], null, "Read failed."),
            })
                Equal(MatchStatus.Invalid, ModMatcher.Evaluate(snapshot, Target(Missing("Absent"))).Status);
        });

        test("All empty live API lists are conservatively Invalid", () =>
        {
            var snapshot = Snapshot([]);
            Equal(false, snapshot.IsValid);
            Equal(MatchStatus.Invalid, ModMatcher.Evaluate(snapshot, Target(Missing("Strength7"))).Status);
            Equal(MatchStatus.Invalid, ModMatcher.Evaluate(snapshot, Target(AtLeast("Strength7", 29))).Status);
        });

        test("Core NaN value1 does not invalidate an available value0 or Exists", () =>
        {
            var snapshot = Snapshot([("Strength7", (29f, float.NaN))]);
            Equal(MatchStatus.Matched, ModMatcher.Evaluate(snapshot, Target(AtLeast("Strength7", 29))).Status);
            Equal(MatchStatus.Matched, ModMatcher.Evaluate(snapshot, Target(new ModCondition { ModId = "Strength7" })).Status);
            var result = ModMatcher.Evaluate(snapshot, Target(AtLeast("Strength7", 0, valueIndex: 1)));
            Equal(MatchStatus.Invalid, result.Status);
            Equal<float?>(null, result.Conditions[0].Evidence[0].ActualValue);
        });

        test("NaN value0 preserves the position of an available value1", () =>
        {
            var snapshot = Snapshot([("DualRoll", (float.NaN, 23f))]);
            Equal(true, float.IsNaN(snapshot.Mods[0].Values[0]));
            Equal(23f, snapshot.Mods[0].Values[1]);
            Equal(MatchStatus.Invalid, ModMatcher.Evaluate(snapshot, Target(AtLeast("DualRoll", 0))).Status);
            Equal(MatchStatus.Matched, ModMatcher.Evaluate(snapshot, Target(AtLeast("DualRoll", 23, valueIndex: 1))).Status);
        });

        test("Empty IDs and infinite values invalidate unrelated Missing conditions", () =>
        {
            foreach (var entry in new[]
            {
                ("", (29f, float.NaN)), ("Bad", (float.PositiveInfinity, float.NaN)), ("Bad", (29f, float.NegativeInfinity)),
            })
                Equal(MatchStatus.Invalid, ModMatcher.Evaluate(Snapshot([entry]), Target(Missing("Absent"))).Status);
        });

        test("NaN comparisons cannot be hidden by AND or OR", () =>
        {
            var snapshot = Snapshot([("Strength7", (29f, float.NaN))]);
            foreach (var logic in new[] { ConditionLogic.And, ConditionLogic.Or })
            {
                var target = Target(new ModCondition { ModId = "Strength7" }, AtLeast("Strength7", 0, valueIndex: 1));
                target.Logic = logic;
                Equal(MatchStatus.Invalid, ModMatcher.Evaluate(snapshot, target).Status);
            }
        });

        test("Ordinary equipment waystones and tablets share the same adapter and matcher", () =>
        {
            foreach (var id in new[] { "Strength7", "MapMonsterDamageAsFire", "TowerDroppedItemRarityIncrease" })
            {
                var result = ModMatcher.Evaluate(Snapshot([(id, (29f, float.NaN))]), Target(AtLeast(id, 29)));
                Equal(MatchStatus.Matched, result.Status);
                Equal(id, result.Conditions[0].Evidence[0].ModId);
            }
        });

        test("Every adapter call copies the current lists without retaining earlier values", () =>
        {
            (string name, (float value0, float value1) values)[] entries = [("Strength7", (29f, float.NaN))];
            var first = Snapshot(entries);
            entries[0] = ("Strength7", (20f, float.NaN));
            var next = Snapshot(entries);
            var target = Target(AtLeast("Strength7", 29));
            Equal(MatchStatus.Matched, ModMatcher.Evaluate(first, target).Status);
            Equal(MatchStatus.NotMatched, ModMatcher.Evaluate(next, target).Status);
            Equal(29f, first.Mods[0].Values[0]);
        });
    }

    private static ModSnapshot Snapshot((string name, (float value0, float value1) values)[] explicitMods,
        (string name, (float value0, float value1) values)[]? implicitMods = null,
        (string name, (float value0, float value1) values)[]? enchantMods = null) =>
        ModSnapshotAdapter.Create(true, explicitMods, implicitMods ?? [], enchantMods ?? [], string.Empty);

    private static ModCondition AtLeast(string id, int minimum, ModCategory category = ModCategory.Explicit, int valueIndex = 0) =>
        new() { ModId = id, Category = category, Kind = ConditionKind.Value, ValueIndex = valueIndex, Minimum = minimum };
    private static ModCondition Missing(string id) => new() { ModId = id, Kind = ConditionKind.Missing };
    private static TargetConditions Target(params ModCondition[] conditions) => new() { Conditions = conditions.ToList() };
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
    }
}
