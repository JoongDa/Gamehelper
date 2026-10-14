# CraftHelper Phase 2 精简交付记录

2026-10-14（Asia/Shanghai）。分支 `feature/crafthelper-hover`，仓库 `JoongDa/Gamehelper`。精简版本已审核通过，准备 Windows 实机验证；功能只读，不发送游戏输入。

## 当前实现

- 恢复 Phase 1 HoverItemScanner，不改 Hover 路径、`ReadFreshItem` 或既有 Item / Base / Mods 读取。每次绘制重新扫描，不沿用上一帧 Item。
- 删除本次新增的 Core 快照 API / reader / vector 验证，撤销本次对 Core 的新增方法；GameHelper Core 和 GameOffsets 无本轮 diff。
- Current Item 使用公共 Base 名称、Path、Mods.Rarity 和 Explicit / Implicit / Enchant 列表。Raw 区域显示同一 API 的原始 ID 和两个 float 值，不承诺全部 int 值。Stats From Mods 暂不提供真实数据，因为 Mods 没有对应公共 API。
- 必要的 ModMatcher 留在 CraftHelper 内：Ordinal 精确 ID、分类、Exists / Missing、value0 / value1 的 `>=` / `<=` / 闭区间、顶层 AND / OR。返回 Matched / NotMatched / Invalid 并显示逐条件证据；无 T 级推断、百分比换算或通用词缀翻译。
- JSON 条件编辑、保存和配置失败提示保留。已知无效扫描、组件不可用、非法配置和所需值不可用返回 Invalid，AND / OR 均保持 Invalid 优先。

## 文件范围

生产修改仅在 CraftHelper：

- 修改 `Plugins/CraftHelper/CraftHelperCore.cs`。
- 新增 `Plugins/CraftHelper/CraftHelperSettings.cs`、`ConditionConfiguration.cs`、`ModSnapshotAdapter.cs`、`Matching/ModMatchModels.cs`、`Matching/ModMatcher.cs`。

测试 / 文档：`tests/CraftHelper.Tests/` 中保留真实可执行测试项目、纯匹配 / 配置 / adapter 测试及 README；本次新增的 Core fake-memory 测试已删除。复用来源及基线行号见 [CraftHelper-Phase2-Reuse.md](CraftHelper-Phase2-Reuse.md)。`.gitignore` 保留 artifacts 忽略项；用户的 `GameOverlay.sln` 修改留在本地，不纳入本功能提交。日志、ZIP、运行配置与历史审查产物不纳入提交。

撤回的新增文件为 `GameHelper/RemoteObjects/Components/ModsSnapshot.cs`、`ModsSnapshotReader.cs`、`GameHelper/Utils/StdVectorValidation.cs` 和 `tests/CraftHelper.Tests/CoreSnapshotTests.cs`。此前对 `Mods.cs`、`ObjectMagicProperties.cs`、`SafeMemoryHandle.cs` 和 Phase 1 Scanner 的任务增量已撤回，没有覆盖其他用户修改。

与上一版 21 文件审查源码包比较，按代码物理行（含空行）统计：生产代码 2251 → 1760，减少 491 行；测试代码 961 → 541，减少 420 行；合计减少 **911 行**。本次 Core 任务增量共 468 行已全部撤回。统计明细：`F:\Gamehelper\artifacts\phase2-simplified\line-reduction.csv`；该口径不包含文档文字减少。

## 条件示例

```json
{
  "Logic": "And",
  "Conditions": [
    { "ModId": "Strength7", "Category": "Explicit", "Kind": "Value", "ValueIndex": 0, "Comparison": "GreaterThanOrEqual", "Minimum": 29 },
    { "ModId": "LocalCriticalMultiplier6", "Category": "Explicit", "Kind": "Value", "ValueIndex": 0, "Comparison": "GreaterThanOrEqual", "Minimum": 23 }
  ]
}
```

`LessThanOrEqual` 用 Maximum，`Range` 用 Minimum / Maximum（含端点）；存在 / 缺失用 `Kind: "Exists"` / `"Missing"`。Any 覆盖三类词缀；ID 大小写精确。条件检查 API 返回的值：NaN 不转零，需要该位置时为 Invalid。

UI 编辑框使用上面的条件 JSON。点击 Save Conditions 后写入 `<运行目录>\Plugins\CraftHelper\config\settings.json`。磁盘设置文件有一层字符串字段，不是直接放条件对象；实际格式示例：

```json
{
  "TargetConditionsJson": "{\"Logic\":\"And\",\"Conditions\":[{\"ModId\":\"Strength7\",\"Category\":\"Explicit\",\"Kind\":\"Value\",\"ValueIndex\":0,\"Comparison\":\"GreaterThanOrEqual\",\"Minimum\":29},{\"ModId\":\"LocalCriticalMultiplier6\",\"Category\":\"Explicit\",\"Kind\":\"Value\",\"ValueIndex\":0,\"Comparison\":\"GreaterThanOrEqual\",\"Minimum\":23}]}"
}
```

此文件由插件生成，属于本地运行设置，不提交 Git。使用本项目默认 Release 输出时，运行目录为 `GameHelper\bin\Release\net10.0-windows\win-x64`。

## 可执行验证

真实测试项目位于 `F:\Gamehelper\tests\CraftHelper.Tests\CraftHelper.Tests.csproj`。它是可执行测试器，使用 `dotnet run`；`dotnet test GameOverlay.sln` 不会执行这些断言。

从仓库根目录执行：

```powershell
dotnet restore GameOverlay.sln --verbosity minimal
dotnet restore tests\CraftHelper.Tests\CraftHelper.Tests.csproj --verbosity minimal
dotnet build GameOverlay.sln -c Release --no-restore -t:Rebuild --verbosity minimal
dotnet build tests\CraftHelper.Tests\CraftHelper.Tests.csproj -c Release --no-restore -t:Rebuild --verbosity minimal
dotnet run --project tests\CraftHelper.Tests\CraftHelper.Tests.csproj -c Release --no-build --no-restore
```

旧报告的 105 项对应现已撤回的 Core 快照版本。本次精简后实际执行上述命令：

| 检查 | 结果 | 日志（`F:\Gamehelper\artifacts\phase2-commit-validation\`） |
| --- | --- | --- |
| 完整 Solution Release Rebuild | 0 errors、122 warnings，退出码 0 | `release-build.log` |
| 测试项目 Release Rebuild | 0 errors、0 warnings，退出码 0 | `test-build.log` |
| 可执行测试 | **76 / 76 通过**，退出码 0 | `tests.log` |

保留 AND / OR、精确 ID、分类、数值边界 / 区间、缺失词缀、无效数据 / 配置及 adapter 复制与 NaN 语义测试；删除对已撤回 Core reader 的测试。上述为提交前重新执行的结果，日志位于 `F:\Gamehelper\artifacts\phase2-commit-validation\`。122 条 Solution 警告来自既有依赖和已有源码，本轮不修复无关问题。上述测试不加载真实游戏进程。

## 剩余风险与 Windows 验证

原 `ObjectMagicProperties` 共享字符串缓存风险单独记录；新 Item 不会清空该缓存。现有 Core 把部分读取失败表示为空 / default 或吞掉更新异常，公共列表没有完整性标志。adapter 对全部三类为空保守 Invalid，但看似正常的部分非空数据仍无法可靠识别。Missing / Exists 只针对可用 API 数据，不能证明底层读取完整。float API 只保留两值且大整数精度有限；不宣称高精度原始 int 快照。

1. 在 `F:\AutoCraft\GameHelper2\Gamehelper-CraftHelper` 保存已有本地修改，然后从 `origin` 拉取 `feature/crafthelper-hover`（使用 `git pull --ff-only origin feature/crafthelper-hover`），确认测试项目存在。不沿用旧源码包中已撤回的 Core reader。
2. 在目标目录执行上述 Release / `dotnet run` 命令。精简插件不再依赖新增 Core API，但运行 Core 应与当前项目构建及现有游戏版本一致。
3. 在 Windows + PoE2 检查普通装备 / Waystone / Tablet、三类词缀、鼠标移出、关闭面板、切页、地址失效与每帧耗时。合法无词缀装备保守 Invalid 属于明确限制。
4. 特别验证同一 Item 地址内容改变后是否刷新数值及证据；Phase 1 新 Item 机制在源码上触发新组件读取，实机才能确认当前游戏行为。共享缓存与被 Core 吞掉的读取失败也不能由纯逻辑测试证明。

没有自动左键、通货使用、Craft 循环或物品移动；StashUtility、LootValue、PickupHelper、AutoHotKeyTrigger 源码未修改。Windows 实机验证仍待执行。
