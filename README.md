# FarmTogether2.AutoSellMod

FarmTogether2.AutoSellMod 是 Farm Together 2 的 BepInEx IL2CPP Mod。当前版本为 `1.1.1`。Mod 会定期扫描农场仓库和已放置的城镇商店，在资源达到设定比例后自动出售超出目标比例的部分。

## 支持的游戏版本

当前兼容目标为 Steam build `24069957`。加载时会校验 `GameAssembly.dll` 与 `global-metadata.dat` 的 SHA-256；文件缺失或指纹不符时，插件不会启用出售逻辑。其他 build 尚未纳入兼容性声明。

## 前置组件

本 Mod 使用 [BepInEx Unity IL2CPP](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html)。Windows 64 位版本锁定为 [BepInEx Unity IL2CPP `6.0.0-be.755+3fab71a`](https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip)。

按 BepInEx 官方说明把前置组件解压到游戏根目录，并先启动游戏一次，让 BepInEx 生成配置与 IL2CPP interop 文件。

## 下载、安装与卸载

从 GitHub Release 下载 `FarmTogether2.AutoSellMod-v1.1.1.zip`，将压缩包内的 `BepInEx` 目录合并到游戏根目录。安装后的 DLL 路径应为：

```text
BepInEx/plugins/FarmTogether2.AutoSellMod/FarmTogether2.AutoSellMod.dll
```

启动游戏后，BepInEx 日志应包含：

```text
Loading [FarmTogether2.AutoSellMod 1.1.1]
```

卸载时删除 `BepInEx/plugins/FarmTogether2.AutoSellMod` 目录。不要把 reference assembly、stub 或 IL2CPP interop DLL 复制进游戏的插件目录。

## 行为

AutoSell 每隔一段时间扫描仓库与可用商店。资源占用比例达到 `TriggerRatio` 后，Mod 会按商店单次交易量出售超出目标比例的部分，并受商店剩余使用次数限制。仓库已满、超出目标比例的数量不足一份交易，但资源总量足以完成一份交易时，`SellOneTradeWhenFull` 可允许出售一次。

同一资源有多个报价时，收益币种优先级为奖章（Medals）→ 钻石（Bills）→ 金币（Coins）。同一报价包含多种收益币种时，按其中优先级最高的币种排序；相同优先级保持商店和报价的发现顺序。

候选报价按上述顺序逐项重新评估。执行每项报价前，Mod 都会重新读取资源库存和对应商店的剩余使用次数，并根据该报价的单次交易量重新计算可售数量。高优先级报价受剩余使用次数或单次交易量限制时，后续报价仍会依次评估。原生请求的交易次数上限为 `32767`，避免游戏把更大的 16 位编码值解读为负数。

联网时，每次请求只出售一份商店交易量。同一资源已有请求等待确认时，不会发送第二个请求。插件紧贴原生 `SellResources` 调用重新读取当前报价，并调用游戏自己的 `FarmMoney * float` operator 计算收益，然后保存当前 session、资源种类、资源数量和三种收益币种，再把请求标为待确认。只有本次原生调用尚未返回时同步触发的城镇交换事件完整匹配这些字段，而且原生调用随后正常返回，插件才记录 `Sold` 日志并显示提示。

没有同步匹配事件，或原生调用抛出异常时，请求都会继续阻止同一资源的后续出售。15 秒后，插件会把请求标为结果未知并记录一次警告；晚到事件和其他手动交换不会解除阻止状态，只有农场、当前玩家或插件生命周期重置才会清除请求。暂时无法读取 session identity 时也不会清除请求。关闭配置总开关不会丢弃已经提交的待确认状态。离线模式的首次请求仍可按本次计算结果批量出售。

每项报价的读取和执行彼此隔离；单项失败不会中止同轮其余报价。扫描、商店报价和出售异常共用 10 秒警告节流窗口，连续异常不保证逐项写入日志。

活动车（Event Shack）的 `Event` 与 `EventB` 资源默认允许出售。`ExcludedResources` 默认值为 `GoldNugget`，所以金块仍默认排除。

完整权限玩家无需调用原生商店开放状态检查。权限受限的玩家仍遵循游戏返回的商店状态；原生检查抛出异常时，该商店本轮不会参与出售。相关警告受统一的 10 秒节流限制；写入日志时会包含城镇槽位和异常信息。

## 配置

BepInEx 首次加载插件后会生成配置。源码定义的默认值如下：

| 分组 | 配置项 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `General` | `Enabled` | `true` | AutoSell 总开关 |
| `General` | `CheckIntervalSeconds` | `5.0` | 扫描间隔，运行时最低为 1 秒 |
| `Sell` | `TriggerRatio` | `0.80` | 触发比例，运行时限制在 `0.01` 至 `0.999` |
| `Sell` | `ExcludedResources` | `GoldNugget` | 不自动出售的 `FarmResourceType` 名称；支持逗号、分号或空白分隔 |
| `Migration` | `ConfigSchemaVersion` | `0` | 内部迁移标记，不应手动修改 |
| `Sell` | `SellOneTradeWhenFull` | `true` | 仓库已满时允许至少出售一次 |
| `UI` | `ShowSellPopup` | `true` | 出售后显示屏幕提示 |
| `UI` | `SellPopupSeconds` | `3.0` | 提示显示时长；运行时限制在 0.5 至 10 秒 |
| `Debug` | `DebugLog` | `false` | 记录出售尝试及无效排除项 |

`ExcludedResources` 中无法识别的名称会忽略；启用 `DebugLog` 后会写入警告日志。

## Event/EventB 配置迁移

旧版本默认排除 `Event,EventB,GoldNugget`。首次加载新版配置时，只有仍保留这组旧默认值且迁移版本低于 1 的配置会改为 `GoldNugget`。已经自定义的排除项不会改动。迁移完成后，即使后来再次加入 `Event` 或 `EventB`，Mod 也不会重复覆盖。

## 兼容性与已知限制

- 只会使用已放置且当前可访问的城镇商店；没有对应商店报价的资源不会自动出售。
- 不支持 Ticket Trader（`TownFeatureNuggetShop`），也不会自动收集 ticket。
- 权限受限的玩家仍受游戏原生商店开放状态约束。
- 游戏更新会使二进制指纹校验失败，出售逻辑保持停用，直到该 build 完成兼容性验证并更新插件。
- 联网 RPC 不返回 host 侧的成功或失败状态。城镇交换事件也没有请求 ID 和发起玩家字段。插件只接受当前 `SellResources` 调用内同步出现的事件，并同时核对 session、资源种类、精确数量及 Coins、Bills、Medals；调用异常或没有同步匹配时保留待确认状态。插件不会在超时后自动重试，以免结果未知的请求与新请求重叠。
- 屏幕提示依赖 Unity IMGUI。提示接口不可用时，Mod 会停用提示渲染；出售逻辑不依赖提示界面。

## 开发

不依赖游戏程序集的 managed tests 以 `net8.0` 为目标；开发环境须提供 .NET 8 SDK 和 .NET 8 runtime。运行命令如下：

```powershell
dotnet test tests/FarmTogether2.AutoSellMod.Tests/FarmTogether2.AutoSellMod.Tests.csproj -c Release
```

仓库通过锁定的 FarmTogether2-ModKit 支持 Hosted 与 LocalInterop 两种构建模式：

```powershell
pwsh ./scripts/build.ps1 -GameApiMode Hosted -Configuration Release
pwsh ./scripts/build.ps1 -GameApiMode LocalInterop -Configuration Release -InteropDir '<absolute-path-to-BepInEx/interop>'
```

Hosted 模式不读取本机游戏文件，也不会部署插件；LocalInterop 模式使用指定的真实 interop 程序集验证编译结果。

## 声明

本项目是非官方社区 Mod，与 Milkstone Studios 无关联。使用者须自行拥有 Farm Together 2 的合法副本。本仓库与发布包不包含游戏文件。

## 许可证

源码按 [MIT License](LICENSE) 发布。
