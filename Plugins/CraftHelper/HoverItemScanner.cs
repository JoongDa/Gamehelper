namespace CraftHelper
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Reflection;
    using GameHelper;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameOffsets.Objects.UiElement;

    internal enum HoverScanState { WaitingForGame, PanelsClosed, NoItem, ReadFailed, Success }

    internal sealed class HoverScanResult
    {
        internal HoverScanState State = HoverScanState.PanelsClosed;
        internal string Detail = "Inventory and stash are closed.";
        internal bool InventoryOpen;
        internal bool StashOpen;
        internal string Source = string.Empty;
        internal int SlotsChecked;
        internal IntPtr SlotAddress;
        internal Item? Item;
        internal Base? Base;
        internal Mods? Mods;
    }

    internal sealed class HoverItemScanner
    {
        // Paths are relative to their respective panels, not interchangeable UI roots.
        private static readonly int[][] StashPaths = { new[] { 2, 0, 0, 0, 1, 1 }, new[] { 2, 0, 0, 0, 0, 1, 1 } };
        private static readonly int[][] NormalGridPaths = { new[] { 0, 0 }, new[] { 0 }, Array.Empty<int>() };
        private static readonly ConstructorInfo? ItemConstructor = typeof(Item).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(IntPtr) }, null);
        private readonly UiMemoryReader memory = new();
        private readonly HashSet<IntPtr> visitedSlots = new();
        private readonly List<(IntPtr Content, IntPtr Panel, string Source, bool Stash)> sources = new();
        private readonly List<IntPtr> emptyGridCandidates = new();
        private HoverScanResult result = new();
        private Vector2 mouse;
        private bool readFailed;

        internal HoverScanResult Scan(Vector2 mousePosition)
        {
            // No item or UI address survives a frame. Closed panels and failures clear immediately.
            this.result = new HoverScanResult();
            this.visitedSlots.Clear();
            this.sources.Clear();
            this.mouse = mousePosition;
            this.readFailed = false;
            try
            {
                if (Core.Process.Pid == 0 || Core.States.GameCurrentState != GameStateTypes.InGameState)
                {
                    this.result.State = HoverScanState.WaitingForGame;
                    this.result.Detail = "Waiting for an in-game session.";
                    return this.result;
                }

                if (!this.memory.Attach()) throw new InvalidOperationException("Game memory reader is unavailable.");
                var gameUi = Core.States.InGameStateObject.GameUi;
                if (gameUi.Address == IntPtr.Zero) throw new InvalidOperationException("Game UI root is unavailable.");

                if (Core.GHSettings.EnableControllerMode)
                {
                    this.FindInventory(gameUi.LeftPanel.Address, "Inventory P1", true);
                    this.FindInventory(gameUi.RightPanel.Address, "Inventory P2", true);
                    this.FindStash(gameUi.LeftPanel.Address, "Stash P1", true);
                    this.FindStash(gameUi.RightPanel.Address, "Stash P2", true);
                }
                else
                {
                    this.FindInventory(gameUi.RightPanel.Address, "Inventory", false);
                    this.FindStash(gameUi.LeftPanel.Address, "Stash", false);
                }

                foreach (var source in this.sources)
                {
                    if (source.Stash) this.ScanStash(source.Content, source.Source, source.Panel);
                    else this.ScanGrid(source.Content, source.Source, source.Panel);
                    if (this.result.Item != null) return this.result;
                }

                if (this.result.Item != null) return this.result;
                this.result.State = this.readFailed ? HoverScanState.ReadFailed :
                    this.result.InventoryOpen || this.result.StashOpen ? HoverScanState.NoItem : HoverScanState.PanelsClosed;
                if (this.result.State == HoverScanState.NoItem && this.result.Detail == "Inventory and stash are closed.")
                    this.result.Detail = "No item under the mouse in an open inventory/stash.";
                else if (this.result.State == HoverScanState.ReadFailed && this.result.Detail == "Inventory and stash are closed.")
                    this.result.Detail = "A UI pointer or child list could not be read this frame.";
            }
            catch (Exception ex)
            {
                this.result.Item = null;
                this.result.Base = null;
                this.result.Mods = null;
                this.result.State = HoverScanState.ReadFailed;
                this.result.Detail = ex.GetBaseException().Message;
            }

            return this.result;
        }

        private void FindInventory(IntPtr panel, string source, bool controller)
        {
            var inventory = this.ResolvePath(panel, controller ? new[] { 3, 0, 0, 1 } : new[] { 5 });
            if (inventory == IntPtr.Zero) return;
            this.result.InventoryOpen = true;
            var grid = this.ResolvePath(inventory, controller ? new[] { 0, 2 } : new[] { 36 });
            if (grid == IntPtr.Zero)
            {
                this.Fail("Inventory is visible, but its grid path could not be resolved.");
                return;
            }

            // The inventory layout container may have zero size; clip to its actual side panel.
            this.sources.Add((grid, panel, source, false));
        }

        private void FindStash(IntPtr panel, string source, bool controller)
        {
            var containers = controller ? new[] { new[] { 3, 0, 0, 0, 2 } } : StashPaths;
            foreach (var path in containers)
            {
                var tabs = this.ResolvePath(panel, path);
                if (tabs == IntPtr.Zero) continue;
                this.result.StashOpen = true;
                this.sources.Add((tabs, panel, source, true));
            }
        }

        private void ScanStash(IntPtr tabs, string source, IntPtr panel)
        {
            // Ghost/template pages can retain their visible flag. Select one actual nonzero layout.
            this.emptyGridCandidates.Clear();
            foreach (var tab in this.Children(tabs))
            {
                if (this.IsVisible(tab) && this.ScanStashTab(tab, source, panel)) return;
            }
            // An empty candidate cannot mask a later populated active tab. It is only inspected last.
            foreach (var grid in this.emptyGridCandidates)
            {
                this.ScanGrid(grid, source + " / Grid", panel);
                if (this.result.Item != null) return;
            }
            if (!this.readFailed)
                this.result.Detail = "Stash is open, but no supported active grid is visible (empty or different layout).";
        }

        private bool ScanStashTab(IntPtr tab, string source, IntPtr panel)
        {
            var tiersRoot = this.ResolvePath(tab, new[] { 0, 1 });
            var tiers = this.Children(tiersRoot);
            if (tiers.Length == 16 && HasBounds(tiersRoot))
            {
                foreach (var tier in tiers)
                {
                    var pagesRoot = this.ResolvePath(tier, new[] { 0, 1 });
                    foreach (var page in this.Children(pagesRoot))
                    {
                        this.ScanGrid(this.ResolvePath(page, new[] { 0 }), source + " / Waystone", panel);
                        if (this.result.Item != null) return true;
                    }
                }
                return true;
            }

            var pagesRootFragment = this.ResolvePath(tab, new[] { 0, 0, 0, 1 });
            var pages = this.Children(pagesRootFragment);
            if (pages.Length == 6 && HasBounds(pagesRootFragment))
            {
                foreach (var page in pages)
                {
                    this.ScanGrid(this.ResolvePath(page, new[] { 0, 0 }), source + " / Fragment", panel);
                    if (this.result.Item != null) return true;
                }
                return true;
            }

            // Normal/quad and specialized tabs sharing these flat grid shapes, including empty grids.
            var normalGrids = NormalGridPaths.Select(path => this.ResolvePath(tab, path)).ToArray();
            if (normalGrids.Any(this.IsScrollView))
            {
                // The core rectangle helper does not apply scrollbar offsets. Do not show the wrong item.
                this.Fail("Scrollable stash layout requires separate viewport/scroll handling; no item was read.");
                return true;
            }
            foreach (var grid in normalGrids)
            {
                if (!HasBounds(grid)) continue;
                var slots = this.Children(grid);
                if (slots.Length == 0 || !slots.Any(slot => this.TryVisibleNode(slot, out var node) && IsCell(node))) continue;
                if (!slots.Any(this.HasItemCell))
                {
                    this.emptyGridCandidates.Add(grid);
                    continue;
                }
                this.ScanGrid(grid, source + " / Grid", panel);
                return true;
            }
            return false;
        }

        private bool HasItemCell(IntPtr slot)
        {
            if (!this.TryVisibleNode(slot, out var node) || !IsCell(node) || !HasBounds(slot)) return false;
            var children = this.Children(slot);
            if (children.Count(child => this.TryVisibleNode(child, out var childNode) && IsCell(childNode)) > 1) return false;
            if (this.GetItemAddressFromElement(slot, false) != IntPtr.Zero) return true;
            return children.Any(child => this.TryVisibleNode(child, out var childNode) && !IsCell(childNode) &&
                                         this.GetItemAddressFromElement(child, false) != IntPtr.Zero);
        }

        private bool IsScrollView(IntPtr root)
        {
            // LootValue's known scroll shape: [1] content, [2] holder, [2][0] thumb.
            var children = this.Children(root);
            if (children.Length <= 2) return false;
            var thumb = this.ResolvePath(children[2], new[] { 0 });
            return thumb != IntPtr.Zero &&
                PluginUiElementReflection.TryGetAbsoluteRect(children[1], out _, out var contentSize) &&
                PluginUiElementReflection.TryGetAbsoluteRect(children[2], out var holderPos, out var holderSize) &&
                PluginUiElementReflection.TryGetAbsoluteRect(thumb, out var thumbPos, out var thumbSize) &&
                holderSize.X >= 4 && holderSize.X <= 64 && holderSize.Y >= 40 &&
                thumbSize.X >= 2 && thumbSize.X <= holderSize.X * 1.5f &&
                thumbSize.Y >= 8 && thumbSize.Y < holderSize.Y && contentSize.Y > holderSize.Y + 1 &&
                thumbPos.Y >= holderPos.Y - 2 && thumbPos.Y + thumbSize.Y <= holderPos.Y + holderSize.Y + 2;
        }

        private void ScanGrid(IntPtr grid, string source, IntPtr panel)
        {
            if (grid == IntPtr.Zero || !this.IsVisible(grid) || this.result.Item != null) return;
            if (!PluginUiElementReflection.TryGetAbsoluteRect(panel, out var panelPosition, out var panelSize))
            {
                this.Fail("Visible panel bounds could not be read.");
                return;
            }
            if (!Contains(this.mouse, panelPosition, panelSize)) return;

            foreach (var slot in this.Children(grid))
            {
                if (this.result.Item != null) break;
                if (!this.visitedSlots.Add(slot) || !this.TryVisibleNode(slot, out var node)) continue;
                // Grid containers are not item cells. Allow multi-cell armour/weapons, but not an entire tab.
                if (!IsCell(node)) continue;
                this.result.SlotsChecked++;
                if (!PluginUiElementReflection.TryGetAbsoluteRect(slot, out var position, out var size))
                {
                    this.Fail("Item slot bounds could not be read.");
                    continue;
                }
                if (!Contains(this.mouse, position, size)) continue;

                var children = this.Children(slot);
                if (children.Count(child => this.TryVisibleNode(child, out var childNode) && IsCell(childNode)) > 1)
                    continue; // A container of several cells is never a hovered item itself.

                // Scan only the hovered cell, never the panel/grid or other items' rectangles.
                var address = this.GetItemAddressFromElement(slot);
                if (address == IntPtr.Zero)
                {
                    // Some premium grids store the pointer in a bookkeeping child of the cell.
                    foreach (var child in children)
                    {
                        if (!this.TryVisibleNode(child, out var childNode)) continue;
                        if (IsCell(childNode) &&
                            (!PluginUiElementReflection.TryGetAbsoluteRect(child, out var childPosition, out var childSize) ||
                             !Contains(this.mouse, childPosition, childSize))) continue;
                        address = this.GetItemAddressFromElement(child);
                        if (address != IntPtr.Zero) break;
                    }
                }
                if (address == IntPtr.Zero) continue; // An empty cell is not a failed item read.
                this.ReadFreshItem(address, slot, source);
            }
        }

        private IntPtr GetItemAddressFromElement(IntPtr element, bool diagnose = true)
        {
            // Current LootValue pointer offset, followed by StashUtility's bounded candidate scan.
            var primaryRead = this.memory.TryReadPointer(element + 0x4E0, out var primary);
            var invalidPrimary = false;
            if (primaryRead && UiMemoryReader.IsAddress(primary))
            {
                if (PluginUiElementReflection.TryValidateItemAddress(primary, out _, out _)) return primary;
                invalidPrimary = true;
            }
            if (!this.memory.TryReadPointers(element + 0x20, (0x600 - 0x20) / 8, out var candidates))
            {
                // A cell may sit near an unreadable page boundary. Recover readable fields individually.
                var readable = new List<IntPtr>();
                for (var offset = 0x20; offset < 0x600; offset += 8)
                    if (this.memory.TryReadPointer(element + offset, out var pointer)) readable.Add(pointer);
                if (readable.Count == 0)
                {
                    if (diagnose) this.Fail("Hovered slot's item pointers could not be read.");
                    return IntPtr.Zero;
                }
                candidates = readable.ToArray();
            }
            foreach (var candidate in candidates.Distinct())
            {
                if (UiMemoryReader.IsAddress(candidate) &&
                    PluginUiElementReflection.TryValidateItemAddress(candidate, out _, out _)) return candidate;
            }
            if (diagnose && invalidPrimary) this.Fail("Hovered slot's item pointer is invalid.");
            return IntPtr.Zero;
        }

        private void ReadFreshItem(IntPtr address, IntPtr slot, string source)
        {
            if (ItemConstructor == null) throw new InvalidOperationException("Item(IntPtr) constructor is unavailable.");
            var item = ItemConstructor.Invoke(new object[] { address }) as Item;
            if (item == null || !item.IsValid || !item.Path.StartsWith("Metadata/Items/", StringComparison.OrdinalIgnoreCase) ||
                !item.TryGetComponent<Base>(out var itemBase) || !itemBase.IsParentValid(address))
            {
                this.Fail("Hovered item snapshot or Base component is invalid.");
                return;
            }
            // A missing Mods component is allowed (e.g. currency). An invalid component is a failed read.
            item.TryGetComponent<Mods>(out var mods);
            if (mods != null && !mods.IsParentValid(address))
            {
                this.Fail("Hovered item's Mods component could not be read.");
                return;
            }
            if (!this.IsVisible(slot))
            {
                this.Fail("Item panel closed during this frame's read.");
                return;
            }

            this.result.State = HoverScanState.Success;
            this.result.Detail = "Fresh item snapshot read successfully.";
            this.result.Source = source;
            this.result.SlotAddress = slot;
            this.result.Item = item;
            this.result.Base = itemBase;
            this.result.Mods = mods;
        }

        private IntPtr ResolvePath(IntPtr root, int[] path)
        {
            var current = root;
            if (!this.IsVisible(current)) return IntPtr.Zero;
            foreach (var index in path)
            {
                var children = this.Children(current);
                if (index < 0 || index >= children.Length) return IntPtr.Zero;
                current = children[index];
                if (!this.IsVisible(current)) return IntPtr.Zero;
            }
            return current;
        }

        private IntPtr[] Children(IntPtr address)
        {
            if (!this.TryVisibleNode(address, out var node)) return Array.Empty<IntPtr>();
            var vector = node.ChildrensPtr;
            var bytes = vector.Last.ToInt64() - vector.First.ToInt64();
            if (bytes < 0 || bytes % 8 != 0 || bytes > 4096 * 8 ||
                !this.memory.TryReadPointers(vector.First, (int)(bytes / 8), out var children))
            {
                this.Fail("Invalid or unreadable UI child vector.");
                return Array.Empty<IntPtr>();
            }
            return children;
        }

        private bool TryVisibleNode(IntPtr address, out UiElementBaseOffset node)
        {
            node = default;
            if (address == IntPtr.Zero) return false;
            if (!this.memory.TryReadNode(address, out node))
            {
                this.Fail("Invalid or unreadable UI element pointer.");
                return false;
            }
            return UiElementBaseFuncs.IsVisibleChecker(node.Flags);
        }

        private bool IsVisible(IntPtr address)
        {
            // Do not demand a nonzero rectangle: some visible layout containers have zero size.
            var visited = new HashSet<IntPtr>();
            while (address != IntPtr.Zero)
            {
                if (visited.Count >= 64 || !visited.Add(address))
                {
                    this.Fail("Invalid UI parent chain.");
                    return false;
                }
                if (!this.TryVisibleNode(address, out var node)) return false;
                address = node.ParentPtr;
            }
            return visited.Count > 0;
        }

        private void Fail(string detail)
        {
            this.readFailed = true;
            this.result.Detail = detail;
        }

        private static bool HasBounds(IntPtr address) => address != IntPtr.Zero &&
            PluginUiElementReflection.TryGetAbsoluteRect(address, out _, out _);

        private static bool IsCell(UiElementBaseOffset node) =>
            float.IsFinite(node.UnscaledSize.X) && float.IsFinite(node.UnscaledSize.Y) &&
            node.UnscaledSize.X >= 20 && node.UnscaledSize.Y >= 20 &&
            node.UnscaledSize.X <= 400 && node.UnscaledSize.Y <= 400;

        private static bool Contains(Vector2 point, Vector2 position, Vector2 size) =>
            float.IsFinite(point.X) && float.IsFinite(point.Y) &&
            point.X >= position.X && point.X < position.X + size.X &&
            point.Y >= position.Y && point.Y < position.Y + size.Y;
    }
}
