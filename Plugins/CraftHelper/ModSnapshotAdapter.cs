namespace CraftHelper;

using System;
using System.Collections.Generic;
using CraftHelper.Matching;

/// <summary>Copies existing public Mods lists; it does not read or parse game memory.</summary>
internal static class ModSnapshotAdapter
{
    internal static ModSnapshot Create(
        bool scanSucceeded,
        IReadOnlyList<(string name, (float value0, float value1) values)>? explicitMods,
        IReadOnlyList<(string name, (float value0, float value1) values)>? implicitMods,
        IReadOnlyList<(string name, (float value0, float value1) values)>? enchantMods,
        string failureReason)
    {
        if (!scanSucceeded || explicitMods == null || implicitMods == null || enchantMods == null)
            return Invalid(string.IsNullOrWhiteSpace(failureReason) ? "Current item Mods API data is unavailable." : failureReason);

        // Core has no public read-completeness flag. An entirely empty read cannot establish absence reliably.
        if (explicitMods.Count + implicitMods.Count + enchantMods.Count == 0)
            return Invalid("All three Mods API lists are empty; a valid unmodified item cannot be distinguished from an incomplete read.");

        var mods = new List<RawMod>();
        foreach (var (category, entries) in new[]
        {
            (ModCategory.Explicit, explicitMods), (ModCategory.Implicit, implicitMods), (ModCategory.Enchant, enchantMods),
        })
        {
            foreach (var (name, values) in entries)
            {
                if (string.IsNullOrWhiteSpace(name) || float.IsInfinity(values.value0) || float.IsInfinity(values.value1))
                    return Invalid("Mods API returned an empty ID or infinite value.");
                // Keep both positions. Core uses NaN for an unavailable slot; never substitute zero or shift value1.
                mods.Add(new RawMod { Category = category, Id = name, Values = Array.AsReadOnly(new[] { values.value0, values.value1 }) });
            }
        }
        return new ModSnapshot { IsValid = true, Mods = mods.AsReadOnly() };
    }

    private static ModSnapshot Invalid(string reason) => new() { IsValid = false, InvalidReason = reason };
}
