namespace CraftHelper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Numerics;
    using System.Text.Json;
    using CraftHelper.Matching;
    using GameHelper.Plugin;
    using ImGuiNET;

    public sealed class CraftHelperCore : PCore<CraftHelperSettings>
    {
        private readonly HoverItemScanner scanner = new();
        private string? parsedJson;
        private TargetConditions? target;
        private string configurationError = string.Empty;
        private string settingsError = string.Empty;
        private string SettingPath => Path.Combine(this.DllDirectory, "config", "settings.json");

        public override string GetDescription() => "Read-only item details and raw Mod ID condition matching (Phase 2).";

        public override void OnEnable(bool isGameOpened)
        {
            this.settingsError = string.Empty;
            try
            {
                if (File.Exists(this.SettingPath))
                    this.Settings = JsonSerializer.Deserialize<CraftHelperSettings>(File.ReadAllText(this.SettingPath))
                        ?? throw new InvalidDataException("Settings contain null.");
            }
            catch (Exception ex)
            {
                // Never replace an unreadable user configuration with example conditions.
                this.Settings.TargetConditionsJson = string.Empty;
                this.settingsError = $"Could not load settings: {ex.GetBaseException().Message}";
            }
            this.parsedJson = null;
        }

        public override void OnDisable() { }

        public override void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this.SettingPath)!);
                File.WriteAllText(this.SettingPath, JsonSerializer.Serialize(this.Settings, new JsonSerializerOptions { WriteIndented = true }));
                this.settingsError = string.Empty;
            }
            catch (Exception ex) { this.settingsError = $"Could not save settings: {ex.GetBaseException().Message}"; }
        }

        public override void DrawSettings()
        {
            ImGui.PushID("CraftHelperSettings");
            try { this.DrawTargetConditions(); }
            finally { ImGui.PopID(); }
        }

        public override void DrawUI()
        {
            // Scan on every draw, including collapsed windows. Never reuse a previous item.
            var scan = this.scanner.Scan(ImGui.GetMousePos());
            var snapshot = ModSnapshotAdapter.Create(scan.State == HoverScanState.Success && scan.Item != null,
                scan.Mods?.ExplicitMods, scan.Mods?.ImplicitMods, scan.Mods?.EnchantMods,
                scan.State == HoverScanState.Success ? "Hovered item has no readable Mods component." : scan.Detail);
            ImGui.SetNextWindowSize(new Vector2(680, 700), ImGuiCond.FirstUseEver);
            var visible = ImGui.Begin("CraftHelper", ImGuiWindowFlags.NoSavedSettings);
            try
            {
                if (!visible) return;
                ImGui.SeparatorText("Current Item");
                ImGui.TextUnformatted($"Status: {scan.State}");
                ImGui.TextUnformatted($"Inventory: {(scan.InventoryOpen ? "Open" : "Closed")} | Stash: {(scan.StashOpen ? "Open" : "Closed")}");
                ImGui.TextWrapped(scan.Detail);
                ImGui.TextUnformatted($"Slots checked: {scan.SlotsChecked}");
                DrawCurrentItem(scan);
                // Edits take effect in the same frame, including invalid drafts.
                this.DrawTargetConditions();
                var result = ModMatcher.Evaluate(snapshot, this.target);
                ImGui.SeparatorText("Match Result");
                var color = result.Status switch
                {
                    MatchStatus.Matched => new Vector4(0.3f, 0.9f, 0.4f, 1),
                    MatchStatus.NotMatched => new Vector4(1, 0.7f, 0.3f, 1),
                    _ => new Vector4(1, 0.4f, 0.4f, 1),
                };
                ImGui.TextColored(color, result.Status.ToString());
                ImGui.TextWrapped(result.Reason);
                foreach (var condition in result.Conditions)
                {
                    ImGui.Separator();
                    ImGui.TextWrapped($"{condition.ConditionIndex + 1}. {condition.Description}: {condition.Status}");
                    ImGui.TextWrapped(condition.Reason);
                }
            }
            finally { ImGui.End(); }
        }

        private static void DrawCurrentItem(HoverScanResult scan)
        {
            if (scan.Item == null || scan.Base == null) return;
            ImGui.TextUnformatted($"Source: {scan.Source}");
            ImGui.TextUnformatted($"Slot Address: 0x{scan.SlotAddress.ToInt64():X}");
            ImGui.TextUnformatted($"Item Name (Base): {scan.Base.BaseItemName}");
            ImGui.TextUnformatted($"Internal Name: {scan.Base.InternalName}");
            ImGui.TextWrapped($"Path: {scan.Item.Path}");
            ImGui.TextUnformatted($"Item Address: 0x{scan.Item.Address.ToInt64():X}");
            var mods = scan.Mods;
            if (mods == null)
            {
                ImGui.TextUnformatted("Rarity and mods: unavailable (item has no Mods component).");
                return;
            }
            ImGui.TextUnformatted($"Rarity: {mods.Rarity}");
            ImGui.TextDisabled("Raw Mod IDs and float values exposed by Core. '-' means unavailable.");
            DrawMods("Explicit Mods", mods.ExplicitMods);
            DrawMods("Implicit Mods", mods.ImplicitMods);
            DrawMods("Enchant Mods", mods.EnchantMods);
        }

        private void DrawTargetConditions()
        {
            ImGui.SeparatorText("Target Conditions");
            ImGui.TextWrapped("Edit JSON. IDs are case-sensitive and exact. Any means Explicit / Implicit / Enchant. Range is inclusive.");
            ImGui.TextWrapped("T1/T2 mapping and display percentage conversion are unsupported. Mod ID suffixes are not tiers.");
            var json = this.Settings.TargetConditionsJson ?? string.Empty;
            if (ImGui.InputTextMultiline("##TargetConditions", ref json, 32768, new Vector2(-1, 190)))
                this.Settings.TargetConditionsJson = json;
            if (ImGui.Button("Save Conditions")) this.SaveSettings();
            ImGui.SameLine();
            if (ImGui.Button("Load Example")) this.Settings.TargetConditionsJson = ConditionConfiguration.ExampleJson;
            this.ParseConditions();
            if (this.target != null)
                ImGui.TextUnformatted($"Current logic: {this.target.Logic} | Conditions: {this.target.Conditions?.Count ?? 0}");
            if (!string.IsNullOrEmpty(this.configurationError)) ImGui.TextWrapped(this.configurationError);
            if (!string.IsNullOrEmpty(this.settingsError)) ImGui.TextWrapped(this.settingsError);
        }

        private void ParseConditions()
        {
            var json = this.Settings.TargetConditionsJson ?? string.Empty;
            if (string.Equals(this.parsedJson, json, StringComparison.Ordinal)) return;
            this.parsedJson = json;
            ConditionConfiguration.TryParse(json, out this.target, out this.configurationError);
        }

        private static void DrawMods(string title, IReadOnlyList<(string name, (float value0, float value1) values)> mods)
        {
            if (!ImGui.CollapsingHeader($"{title} ({mods.Count})", ImGuiTreeNodeFlags.DefaultOpen)) return;
            if (mods.Count == 0) ImGui.TextDisabled("None");
            foreach (var (name, values) in mods)
                ImGui.TextUnformatted($"{name}: ({FormatValue(values.value0)}, {FormatValue(values.value1)})");
        }

        private static string FormatValue(float value) => float.IsNaN(value) ? "-"
            : value.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
    }
}
