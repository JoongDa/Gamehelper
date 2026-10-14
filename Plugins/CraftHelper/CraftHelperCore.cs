namespace CraftHelper
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Numerics;
    using GameHelper.Plugin;
    using ImGuiNET;

    // PCore requires a settings type; Phase 1 has no settings to persist.
    public sealed class CraftHelperSettings : IPSettings { }

    public sealed class CraftHelperCore : PCore<CraftHelperSettings>
    {
        private readonly HoverItemScanner scanner = new();

        public override string GetDescription() => "Read-only inventory and stash item scanner (Phase 1).";

        public override void OnEnable(bool isGameOpened) { }

        public override void OnDisable() { }

        public override void SaveSettings() { }

        public override void DrawSettings()
        {
            ImGui.TextUnformatted("Phase 1: inspect inventory/stash items in the CraftHelper window.");
        }

        public override void DrawUI()
        {
            // Scan even when the window is collapsed; nothing is retained from a previous frame.
            var scan = this.scanner.Scan(ImGui.GetMousePos());
            ImGui.SetNextWindowSize(new Vector2(540, 440), ImGuiCond.FirstUseEver);
            var visible = ImGui.Begin("CraftHelper", ImGuiWindowFlags.NoSavedSettings);
            try
            {
                if (!visible)
                {
                    return;
                }

                ImGui.TextUnformatted($"Status: {scan.State}");
                ImGui.TextUnformatted($"Inventory: {(scan.InventoryOpen ? "Open" : "Closed")} | Stash: {(scan.StashOpen ? "Open" : "Closed")}");
                ImGui.TextWrapped(scan.Detail);
                ImGui.TextUnformatted($"Slots checked: {scan.SlotsChecked}");
                if (scan.Item == null || scan.Base == null)
                {
                    return;
                }

                ImGui.Separator();
                ImGui.TextUnformatted($"Source: {scan.Source}");
                ImGui.TextUnformatted($"Slot Address: 0x{scan.SlotAddress.ToInt64():X}");
                ImGui.TextUnformatted($"Item Base Name: {scan.Base.BaseItemName}");
                ImGui.TextUnformatted($"Internal Name: {scan.Base.InternalName}");
                ImGui.TextUnformatted($"Item Path: {scan.Item.Path}");
                ImGui.TextUnformatted($"Item Address: 0x{scan.Item.Address.ToInt64():X}");

                var mods = scan.Mods;
                if (mods == null)
                {
                    ImGui.TextUnformatted("Rarity and mods: unavailable (item has no Mods component).");
                    return;
                }

                ImGui.TextUnformatted($"Rarity: {mods.Rarity}");
                ImGui.TextDisabled("Raw mod IDs and values from game memory.");
                DrawMods("Explicit Mods", mods.ExplicitMods);
                DrawMods("Implicit Mods", mods.ImplicitMods);
                DrawMods("Enchant Mods", mods.EnchantMods);
            }
            finally
            {
                ImGui.End();
            }
        }

        private static void DrawMods(string title, List<(string name, (float value0, float value1) values)> mods)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"{title} ({mods.Count})");
            if (mods.Count == 0)
            {
                ImGui.TextDisabled("None");
            }

            foreach (var (name, values) in mods)
            {
                ImGui.TextUnformatted($"{name}: ({FormatValue(values.value0)}, {FormatValue(values.value1)})");
            }
        }

        private static string FormatValue(float value) =>
            float.IsNaN(value) ? "-" : value.ToString("G", CultureInfo.InvariantCulture);
    }
}
