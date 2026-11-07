# RoomSiege Enemies

本次只增加敌人模块及与现有塔/金币/生命的必要接入。不实现 Waves 调度，不自动开始波次，不生成用于验收的关卡。按用户要求未运行测试、构建或模拟器验证。

## 五类敌人与数值

直接复用当前 TowerHost.Catalog（来自 Step1 的运行时配置副本），通过 DifficultyCalculator 应用难度的 HP、速度、护盾和漏血倍率。当前标准配置基础值：Grunt 24HP / 0.5m/s / 6G / 漏血1；Runner 15HP / 1m/s / 8G / 漏血1；Tank 120HP / 0.3m/s / 25G / 漏血3；Shielded 36HP + 18护盾 / 0.45m/s / 12G / 漏血2；Boss 600HP / 0.25m/s / 150G / 漏血5。

- Grunt：入口宽度内三列交错生成，沿通向固定核心的可行通路推进，普通塔和家具前会绕行。
- Runner：沿同一可行通路蛇形前进；仅在前方线段允许通行时加入摆动，不穿越塔或房间边界。Trap 和高处桌面塔不阻挡地面通路。
- Tank：较大剪影，配置的高血低速。
- Shielded：先扣护盾再扣本体，溢出伤害只结算一次。激光对护盾默认 ×2（EnemyController 序列化字段可调），对本体保持正常伤害。例如剩 5 护盾承受 10 激光伤害，消耗 2.5 原始伤害破盾，剩余 7.5 扣本体。
- Boss：低速大剪影，按配置每 10 秒在自身附近请求召唤 2 个 Grunt。召唤物也占用 20 个活动槽、服从 FIFO 队列，也按 Grunt 配置给击杀金。

## 状态与生命周期

EnemyController 自动挂到 RoomSiege_Start，预热 20 个可复用 EnemyActor。只有 WAVE 状态、部署格有效且未暂停时才移动、召唤、生成、承受伤害。BUILD 中保持敌人冻结。死亡立即隐藏、关闭碰撞与索敌注册，发出一次击杀事件并给 GameManager 加金币；回收推迟到 LateUpdate，避免同帧激光/陷阱遍历引用被复用。

从 GridModel.SpawnPoints 生成，向安全区推进；敌人体型接触 0.8m 安全区边界即漏过，用扫掠线段判断，避免低帧率越过安全区。生命扣除使用配置 leakDamage 的难度结果，保留此前约定的小数（休闲 Grunt 0.5、硬核 Grunt 1.5）。GameManager.Lives 改为 float 并提供 LivesChanged；归零置 IsFailed、进入 RESULT、发 GameFailed，冻结战斗并清空活动及待生成敌人，不发击杀金。清场或扫描模式切换同样不奖励、不扣生命；扫描暂时空快照仅暂停。

最多 20 个正式活动敌人，超出按请求顺序排队，每 1 秒释放一个（配置可调）。请求按整组压缩保存，不因一次提交大量数量创建同等数量 GameObject。死亡和漏过归还池，重用时重新计算本波血量/护盾/护甲，清掉缓速、召唤计时、集火和命中高亮。慢速来源独立计时，最强基础减速加每个额外来源 10 个百分点，上限 80%；紧急冰冻单独为 100%。Towers 的开发测试桩是独立测试对象，不属于正式敌人池。

2026-10-29：每波增加基础 HP/护盾的 35%、护甲 10 点，第一波不加。所有塔伤害先乘 `100/(100+护甲)` 再进入护盾和本体，激光对护盾仍保持 ×2；破盾后的伤害不重复计算护甲。召唤物沿用当前波次成长。0.8s 出场阶段仍受保护。

## 后续 Waves 接口

2026-10-27 更新：正式 Waves 已接入本接口。导航通过 `GridModel.WalkMap` 与 `TowerPlacement.Routes` 共用一份反向距离场，只有房间或阻路塔变化时重建。部署会检查全部入口和场上存活敌人的可达性，拒绝完全封路。导航按最大敌人体型预留余量；狭窄真实空间可能需要调整核心或改用沙盘。运行期间锁定房间，返回菜单后才能重扫。

命名空间 `RoomSiege.Enemies`，程序集 `RoomSiege.Enemies`：

```csharp
var enemies = EnemyController.Instance;
// 等待 IsReady；入口索引必须来自当前 GridModel。
bool accepted = enemies.Enqueue("Grunt", count: 6, entranceIndex: 0);
// 波次开始由后续 Waves 模块控制。
GameManager.Instance.SetState(GameState.WAVE);
// ActiveCount / PendingCount / IsDrained 可供 Waves 判断结束。
enemies.Killed += kill => { /* 金币已结算；不要重复加币 */ };
enemies.Leaked += leak => { /* 生命已结算 */ };
enemies.ClearEnemies(); // 不发金币、不扣血
```

扫描尚未有入口时队列等待；非法 ID/数量/入口索引、未初始化或已失败时 Enqueue 返回 false。本模块不自动从10波配置生成敌人，避免越过 Enemies 范围。

## 视觉与接入改动

敌人是低饱和半透明胶囊剪影，按种类区分尺寸与轻微色调；Shielded 有薄透明护盾轮廓；命中世界坐标附近短时显示亮点，眼动集火复用塔的0.5秒规则并显示脉冲环。所有外观均代码生成，无外部美术依赖。

EnemyActor 实现 ITowerTarget / ITowerHitReceiver / ITowerFocusVisual。Towers 新增目标注册与伤害类型接口，实际敌人与既有测试桩共用同一索敌列表；激光伤害类型用于护盾克制，命中点用于局部高亮。GameManager 增加漏血、生命变化、失败事件；Enemies 内 EnemyLifeDisplay 更新原右栏生命和失败文字。既有塔建造/升级、扫描、波次占位布局不做扩展。
