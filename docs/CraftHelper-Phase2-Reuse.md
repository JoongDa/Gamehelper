# CraftHelper Phase 2 复用调查（精简版）

调查基线为 `feature/crafthelper-hover` 的 `d2f6fe1050c535fd5bc88933469f9cc9c9e7bef1`；下面行号均指该基线。精简版不修改 GameHelper Core、GameOffsets 或其他插件，恢复 Phase 1 HoverItemScanner，直接消费既有公共组件数据。此前新增的 `Mods.TryReadSnapshot` 和 Core 内存读取旁路已撤回。

## 直接复用的接口

| 能力 | 文件 / 行号 | 使用边界 |
| --- | --- | --- |
| 每次 Hover 创建新 Item | `Plugins/CraftHelper/HoverItemScanner.cs:313-349` | 复用 Phase 1 `ReadFreshItem`，通过反射调用既有 internal 构造，不复用上一帧 Item。 |
| Item 构造 / 读取 | `GameHelper/RemoteObjects/States/InGameStateObjects/Item.cs:21-45` | 已有 Item / Entity 读取，不新增物品解析。 |
| 组件定位 | `GameHelper/RemoteObjects/States/InGameStateObjects/Entity.cs:182-222` | public `TryGetComponent<T>`；组件缓存属于当前 Entity 实例。已有缓存命中早于 `shouldCache` 判断，传 false 不能绕过旧实例缓存。 |
| Path / 有效性 | `GameHelper/RemoteObjects/States/InGameStateObjects/Entity.cs:77,103` | public `Path` / `IsValid`；不能证明完整 Mods 读取成功。 |
| 底材名称 | `GameHelper/RemoteObjects/Components/Base.cs:34,40` | public `BaseItemName` / `InternalName`。Item Name 使用底材名称，不承诺完整 Rare / Unique 名称。 |
| 组件归属 | `GameHelper/RemoteObjects/Components/ComponentBase.cs:64-66` | public `IsParentValid`，沿用 Scanner 现有 Owner 检查。 |
| 稀有度 / 分类词缀 | `GameHelper/RemoteObjects/Components/Mods.cs:28,35-39` | public `Rarity`、`ExplicitMods`、`ImplicitMods`、`EnchantMods`；没有 Waystone / Tablet / Jewel 类型限制。 |
| Mod ID / 数值解析 | `GameHelper/RemoteObjects/Components/ObjectMagicProperties.cs:107-150` | `Mods` 已间接调用 internal `AddToMods` / `GetModName` / `GetValue`。插件只读解析结果，不复制、反射调用或新增内存解析。 |

`GetValue` 返回 `(float value0, float value1)`：无值为 `(NaN, NaN)`；一个值使用既有 inline `Value0` 并令 value1 为 NaN；多个值只公开前两个。它由原始整数转换为 float，较大整数可能失去精度。精简版保留该 API 语义，显示和比较 API 返回值，不声称取得全部原始整数。NaN 表示不可用位置，不转为零，不移动 value1；需要该位置进行数值比较时返回 Invalid。Mod ID 的数字后缀不代表 T 级，数值不擅自换算为游戏 UI 百分比。

## StashUtility 可参考部分

| Inspector 区域 | `Plugins/StashUtility/StashUtilityCore.cs` 基线行号 | 数据来源 / 采用方式 |
| --- | --- | --- |
| Item Details | 541-548 | Item.Address / Path、Base.BaseItemName / InternalName；直接调用同一 Core API。 |
| Explicit / Implicit / Enchant Mods | 551-578 | public Mods 分类列表；直接使用同一列表。 |
| Raw Game Memory Mods | 594-603 | 同一三类列表的 concat 和 raw name，没有另一套通用解析器；CraftHelper 保留分类展示。 |
| Stats From Mods | 579-582、2777-2799 | private `GetStatsFromMods` 反射读取 `ModsOffsets.Details0.StatsFromMods`，异常返回空结果；不是公共 Mods API，本轮暂停该区域的真实数据展示，不另造 reader。 |
| Stats From Magic Properties | 586-590 | public `ObjectMagicProperties.ModStats`，来源为另一组件的 Details1，不能冒充 Mods.StatsFromMods。 |

private `EvaluateAndHighlightItem` 的类型限制在 2196-2204；CraftHelper 不调用筛选器，也不依赖 StashUtility 实例。private `IsJewelModMatch`（2091-2183）含 Jewel 名称、关键词与子串启发式；Waystone（2308-2329）和 Tablet（2372-2387）也使用子串匹配。Tablet / Jewel 的 roll 检查把 NaN 转零、选择非零值并取绝对值（2379-2387、2496-2501），不适合通用精确 ID 和原始有符号值条件。

`Plugins/StashUtility/Data/ModDatabase.cs` 的 public `AllWaystoneMods:8`、`AllTabletMods:46`、`AllJewelMods:133` 只覆盖这三类。Jewel 数据加载位于 135-163，对应 jewel JSON；没有普通装备完整 Mod 数据库或公共通用 matcher。`GoodModPatterns` / `BadModPatterns`（`StashUtilitySettings.cs:110-111,153-154`）及 `ModRequiredMinRolls`（`:156`、`Models/JewelGroup.cs:10-12`）仅作为配置思路参考，未复制数据库、类型过滤器或整个筛选系统。

因此 CraftHelper 内必要的纯 ModMatcher 使用 `StringComparison.Ordinal` 精确 ID、分类、value0 / value1 数值比较、存在 / 缺失及顶层 AND / OR；普通装备、Waystone、Tablet 共用入口。

## LootValue / PickupHelper

`Plugins/LootValue/ItemModHelper.cs:10-81` 的类型为 internal。`GetModLines:12-27` 合并组件文本并丢失分类；`ReadFreshItem:30-49` 与 Phase 1 同类；`FormatModLine:62-81` 只是占位符格式化，未提供通用词缀翻译或 T 级映射。`PoeNinjaPriceFetcher.cs:500-574` 的 private listing 相似度匹配使用文本相等、包含、数字抽取，不适合精确 Mod ID 比较。没有新增 LootValue 依赖。

`Plugins/PickupHelper/Native.cs:15,34-43` 的类型和 `LeftClick` 均为 internal，调用在 `PickupHelperCore.cs:117`。本轮只调查，不调用、不复制输入封装；PickupHelper 和 AutoHotKeyTrigger 行为保持不变。

## ReadFreshItem 的刷新边界与原 Core 风险

`ReadFreshItem` 每次新建 Item，并首次取得新实例的 Base / Mods。新组件从零地址设置到当前地址，触发 `RemoteObjectBase.cs:81-105` 的更新；`Mods.UpdateData:55-72` 的 `hasAddressChanged` 在这次初始读取为 true。因此它避免复用上一帧组件列表，理论上能重新读取同地址已改变的词缀。它不是原子快照；同地址变化、快速移走物品、切换仓库页的实际表现仍需 Windows + PoE2 验证。

以下均为原 Core 风险，单独记录，不在本轮修改 Core：

- `ObjectMagicProperties.GetModName:119-125` 按 Mods.DAT 行地址使用全局 `Core.GgpkStringCache`；新 Item 不清除此共享缓存，失败读取或地址复用的缓存风险仍存在。
- `SafeMemoryHandle.ReadMemory:76-95` 失败返回 default，`ReadStdVector:175-185` / `ReadMemoryArray:197-217` 可用空数组表示失败；`AddToMods:107-116` 跳过零 Mod 指针；`RemoteObjectBase:108-111` 捕获更新异常。公共 Mods 列表没有完整读取成功标志。
- 插件捕获可见异常、无效扫描 / 组件、异常 ID / 无限值和所需 NaN；全部三类列表为空时保守返回 Invalid，合法无词缀装备也会落入该边界。部分非空读取失败若返回看似正常的列表，现有公共 API 无法可靠识别，存在 / 缺失判断只针对可用 API 数据，不能证明完整内存读取。
- 三态聚合对已知 Invalid 保持优先；任何 AND / OR 的其他条件都不能把它降级为 NotMatched 或 Matched。编译与纯逻辑测试无法证明上述底层读取完整性、共享缓存或当前游戏 offsets 正确。
