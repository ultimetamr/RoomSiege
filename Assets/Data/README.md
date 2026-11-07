# RoomSiege 数据层

本目录包含 JSON 配置、可序列化 C# 数据模型、校验器、难度倍率纯函数及 EditMode 测试。未接入场景、渲染、交互或 GameManager，未实现攻击、移动、刷怪、召唤、售卖等玩法。

## 文件与使用

- `Config/roomsiege.balance.json`：唯一基础数值配置，schemaVersion 为 1。
- `BalanceDefinitions.cs`：`RoomSiege.Data` 命名空间下的塔、等级、敌人、波次、难度和经济数据。
- `BalanceJson.Parse(string)`：通过 Unity JsonUtility 解析并校验，返回 `BalanceCatalog`；非法配置抛出 `FormatException`。
- `DifficultyCalculator.Apply(enemy, difficulty)`：返回独立的只读 `EnemyStats`，不修改基础配置。
- `DifficultyCalculator.CalculateCampaignTotals(catalog, difficulty)`：汇总配置中明确列出的波次敌人。
- `Tests/EditMode/`：Editor 专用测试程序集，不进入 Android Player。

调用示例（由调用方提供 JSON 文本，本目录不实现资源加载或生命周期组件）：

```csharp
using RoomSiege.Data;

BalanceCatalog catalog = BalanceJson.Parse(jsonText);
DifficultyDefinition standard = catalog.GetDifficulty("Standard");
EnemyStats grunt = DifficultyCalculator.Apply(catalog.GetEnemy("Grunt"), standard);
// 未应用波次修正的基准 HP = 31.2，速度 = 0.5，击杀奖励 = 6，漏血 = 1。
CampaignTotals totals = DifficultyCalculator.CalculateCampaignTotals(catalog, standard);
```

ID 区分大小写：塔为 `MG/Frost/Laser/Trap`，敌人为 `Grunt/Runner/Tank/Shielded/Boss`，难度为 `Casual/Standard/Hardcore`。不存在的 ID 查询抛出 `KeyNotFoundException`。

## 单位与字段约定

- 距离为米，移动速度为米/秒，时间为秒，射速为发/秒，持续伤害为伤害/秒。
- `slowFraction` 为减速比例：0.30 表示降低 30%；`rangeMultiplier` 为相对基础范围的倍率。
- `levels` 按 Lv1、Lv2、Lv3 排序。`upgradeCost` 为本次升级增量价格，Lv1 为 0；建造使用 `buildCost`。
- 每级字段是完整快照，不需要代码推断升级继承。Frost 各级减速均持续 2 秒，Lv3 保留 Lv2 的范围 +30%；Laser Lv2 保留穿透目标数 1。
- `pierceTargetCount` 直接记录需求中的目标数 1/1/2，不表示额外增加的目标数。
- `rangeMeters` 对 Frost 表示基础作用半径，对 Trap 表示触发半径；MG/Laser 表示射程。
- `OptionalFloat/OptionalInt` 用 `specified` 区分未配置与数值 0。仅在 `specified=true` 时读取 `value`；没有该效果的字段可保持未配置。
- 卖出返还比例为 0.7，无利息（`interestRate=0`）。当前仅提供经济参数，不执行售卖、结算或金币取整。

## 已补充的默认数值

用户授权按合理默认值补全未指定项，后续可直接在 JSON 调整：

| 配置项 | 当前值 |
| --- | --- |
| MG 射程（各级） | 2m |
| MG Lv3 小溅射 | 半径 0.3m，固定伤害 1 |
| Frost 基础作用半径 | 1m；Lv2/Lv3 为基础的 1.3 倍 |
| Laser 射程（各级） | 4m |
| Trap 触发半径（各级） | 0.5m |
| Trap Lv3 缓速 | 30%，持续 2s |
| Shielded 基础护盾 | 18HP，随难度 HP 倍率缩放 |
| Boss 周期召唤 | 首次 5s，之后每 7.5s 召唤 2 个 Grunt |

当前参数以 JSON 为准；Runner 的蛇形、Trap 的一次性和 Boss 的召唤行为由对应运行时模块执行。

## 2026-10-29 大幅降低输出与波次防御成长

MG 每发伤害 2/3/4（原 5/8/11），Laser DPS 3.5/6/9（原 9/15/24），Trap 单次 7/12/18（原 18/30/45），Frost Lv3 DPS 0.5（原 1.5）。造价、升级费、射速、范围、减速和穿透数量保持原值。

`ApplyAtWave` 从基础配置计算：HP 与护盾乘 `难度HP倍率 × (1 + (波次-1) × 0.35)`；2026-10-29 后续调整中，前 3 波再乘 `0.25`（配置 `earlyWaveCount=3` / `earlyWaveHpMultiplier=0.25`；休闲开局反馈后从 0.9 进一步降低），且移动速度额外乘 `0.65`（`earlyWaveSpeedMultiplier`），第 4 波起完全沿用上版数值。护甲为 `(波次-1) × 10`。护甲不跨局累积，召唤物使用当前波次；第 10 波之后按第 10 波封顶。实际伤害为 `原始伤害 × 100 / (100 + 护甲)`，护甲在护盾吸收之前只计算一次，破盾溢出不重复减伤。比例减伤同样适用于持续激光和冰墙微伤。

| 波次 | HP / 护盾倍率（另乘难度） | 护甲 | 减伤 |
| --- | --- | --- | --- |
| 1 | 0.25 | 0 | 0% |
| 4 | 2.05 | 30 | 23.08% |
| 7 | 3.10 | 60 | 37.50% |
| 10 | 4.15 | 90 | 47.37% |

出怪仍间隔 1s，速度仍每波增加基础值的 1.5%，仅前 3 波再乘 0.65。以上为本次参数目标，实际整局难度仍需真机试玩反馈。

## 难度规则

| 难度 ID | 显示名 | HP 倍率 | 初始金币 | 速度倍率 | 漏血倍率 |
| --- | --- | --- | --- | --- | --- |
| Casual | 休闲 | 1.0 | 180 | 0.9 | 0.5 |
| Standard | 标准 | 1.3 | 150 | 1.0 | 1.0 |
| Hardcore | 硬核 | 1.7 | 120 | 1.15 | 1.5 |

HP、速度、漏血分别直接乘对应倍率；护盾乘 HP 倍率；击杀奖励不变。所有结果保留小数，不取整，例如休闲 Grunt 漏血 0.5，硬核 Grunt 漏血 1.5。初始金币来自难度定义，不由敌人倍率函数修改。倍率函数不依赖 Unity 生命周期、随机数、全局状态或文件 IO，并拒绝非法数值及浮点溢出。

## 基准统计口径

十波共 93 个预定敌人：Grunt 54、Runner 24、Tank 3、Shielded 11、Boss 1。当前基础本体血量 3012，击杀奖励 873G，基础漏血总额 114。该基准不计护盾，也不计运行时数量未知的 Boss 召唤物。

| 难度 | 预定敌人本体血量 | 击杀奖励 | 全部漏过时的漏血总额 |
| --- | --- | --- | --- |
| 休闲 | 3012 | 873G | 57 |
| 标准 | 3915.6 | 873G | 114 |
| 硬核 | 5120.4 | 873G | 171 |

## 测试与验收

Unity 菜单：`RoomSiege > Run Data EditMode Tests`。也可在 Test Runner 中选择 `RoomSiege.Data.EditModeTests` 的 EditMode 测试。

菜单执行后写入项目根目录的 `TestResults/RoomSiege.Data.EditMode.xml`。测试读取实际交付的 JSON，覆盖解析与往返、全部塔数值、敌人属性、十波组成、三难度、15 组敌人×难度倍率、护盾缩放、输入不被修改、基准汇总、无效配置和非法数值。

2026-10-26 在 Unity 2022.3.62f3c1 中实际执行：47 通过，0 失败，0 跳过。
