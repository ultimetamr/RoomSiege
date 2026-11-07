# RoomSiege Towers

本模块读取既有 Data 配置与 ScanController.CurrentGrid，不实现 Enemies/、Waves/ 或正式关卡。测试桩位于 Towers/，不掉金币、不扣生命、不执行寻路。

## 使用

1. 启动 RoomSiege，确认模拟沙盘或真实扫描产生部署格，点主屏“开始建造”（编辑器也可按 B）。
2. 从左栏抓取全息小图标：OpenXR 控制器 Grip 驱动 XRGrabInteractable。也可用 Pointer 拖塔卡或 Tap 塔卡选择。瞄准部署格看到预览，松开抓取，再用 Trigger/Tap 落格。
3. 蓝色预览可建；红色预览表示无效格、占用或余额不足。只在合法落格且扣款成功后生成塔；每格最多一塔。
4. Tap 已建塔，在建造页内显示管理栏：升级 Lv2、升级 Lv3、卖出、关闭；商店继续保留。Lv1 直接升 Lv3 合计两级升级价，不跳过费用。
5. “生成测试桩”在当前选中塔附近生成三个 Grunt 测试桩，其中一个短距离往返，用于陷阱穿越与减速验证。未选塔时使用首座塔附近，没有塔时使用安全区前方。点“清除测试桩”清理。
6. “取消 / 集火清除”或 Escape 取消选塔并清除集火。“启用真实眼动”在权限未授权时打开系统权限弹窗，由用户自行选择。眼动状态按钮可切换明确标注的“模拟头向注视”，供没有眼动设备的模拟器验证 0.5 秒停留；再次点击回到真实眼动。

只使用已安装 SDK/包中存在的 API。本地 PICO SDK `InteractionPack/InteractionPack_README.md` 明确说明其 3D 抓取基于 Unity XR Interaction Toolkit；本项目使用现有 OpenXR / XRI 3.3.0 控制器，未替换 CameraRig 或启用另一套输入管理器。真实眼动使用 OpenXR `EyeGazeInteraction` 的 `EyeTrackingUsages.gazePosition/gazeRotation`，检查 isTracked 后转换到世界坐标。设备没有有效眼动时不自动标记目标；模拟头向模式单独标明。

## 数值与经济

塔的造价、三级伤害、射速、范围、缓速、溅射及穿透数量来自 `Assets/Data/Config/roomsiege.balance.json`。`Runtime/Resources/RoomSiegeTowerBalance.json` 是可打包副本，由 Prepare/Build 菜单同步，修改数值请编辑 Data 源文件。

GameManager 是唯一金币来源，增加 InitializeEconomy / TrySpendCoins / AddCoins / CoinsChanged 接口。按现有难度索引 0/1/2 初始化为休闲 180、标准 150、硬核 120；同一 GameManager 生命周期只初始化一次，防止场景重载刷钱。

卖出返还实际累计支付（造价+已付升级费）的 70%，整数金币向下取整；例如 MG Lv3 累计 160G、返 112G，Frost Lv3 累计 135G、返 94G。不能重复卖出或重复扣升级费。无利息；测试桩不奖励金币。

扫描中的空 GridModel 暂停建造、升级与攻击。物理支撑仍存在时保留塔及等级，核心移动不会删除武器；只有重新扫描导致物理支撑失效时才全额退还。新建只接受当前规范格，避开 Boss 保留路线、核心本体、入口、家具与占用。0.8m 核心圆环内路线外允许建造；会堵死 Boss 最后通路的地面塔还会被可达性检查拒绝扣款，保留至少 2 格宽（0.8m）通道。导航节点和连接线均检查 Boss 半径 0.28m 加 2cm 留量，取消额外半导航格的重复膨胀；Trap 本身不阻挡寻路，但与其他塔一样不能放在显式保留的路线格。

## 行为

- 默认索敌为射程内距离安全区中心最近的活目标（水平距离）；同分按目标 ID 稳定排序。有效集火目标只在塔射程内覆盖此优先级。
- 同一目标连续有效眼动注视 0.5 秒获得脉冲集火环；转看另一目标重新计时，失去追踪不累积。已标记目标死亡或手动清除后取消集火。
- MG 按配置射速持续单体射击；Lv3 对命中点 0.3m 内其他目标附加配置溅射，不对主目标重复计算。
- Frost 对范围内所有目标刷新缓速；不同来源取最强有效缓速，到期独立恢复；Lv3 同时按秒结算微伤。
- Laser 沿瞄准方向形成连续束，按前后顺序命中束路径内的目标，最多 1/1/2 个；不会把偏离光束的目标算成穿透。
- Trap 用目标上一帧到当前帧的水平线段做穿越检测，限制高度差，触发一次伤害及 Lv3 缓速，随后消耗。默认不返金币；本局选择陷阱回收强化后按原始造价返还 30%/45%/60%，向下取整，不包含升级费，不可重复领取或卖出。
- 转向平滑；塔为约 0.28m 以内的线框结构，弹道/束为细的透明加法发光线，无大块实体遮现实。

## 验证入口

2026-10-27：普通地面塔阻挡敌人通路，部署时检查所有入口与存活敌人可达，不能堵死最后通路或把敌人围在塔内。Trap 与高处塔不阻挡地面。射线点击敌人可立即集火，眼动停留规则继续保留。`CampaignPerks` 是本局共享强化状态：机枪每阶射速×1.25、射程×0.90；寒冰连锁每阶增加死亡传播范围；陷阱回收改变消耗返金。重开清除，不修改基础配置。

Unity 菜单 `RoomSiege > Towers`：`Prepare Towers`、`Run EditMode Tests`、`Build Towers APK`。

测试菜单同时回归 Data / Scan / UI / Towers。结果写入 `TestResults/RoomSiege.Towers.EditMode.xml`。构建输出 `Builds/RoomSiege-Towers.apk`，摘要为 `Builds/towers-build-result.txt`。

Development Build 附带可选 `TowerRuntimeProbe`：仅在应用自己的 persistentDataPath 出现 `tower-acceptance.request` 文件时运行，不开网络端口。需要当前无已购塔；使用临时测试金币验证真实 GameManager 扣款、射线落格、重复拒绝、升级、贴塔面板、四类攻击/卖出，并在结束时恢复原余额与状态、清掉测试对象。结果写到同目录 `tower-acceptance-result.txt`。此检查直接调用运行时入口，不能替代真实控制器抓取和真实眼动验收。

本次对既有模块的必要接入改动：Meta/GameManager 的金币事务及程序集；启用 Android OpenXR 眼动 feature 和 PICO 眼动声明；为既有 UI 进度环补 CanvasRenderer 依赖，避免 XR UI 射线检测异常。没有新增正式敌人或波次实现。

2026-10-26 验证：EditMode 共 107/107 通过（Towers 28、Data 47、Scan 14、UI 18）；Android 构建 Succeeded，0 错误。最终 APK 实际大小 63,230,949 字节。模拟器最终版本运行检查 27/27 通过，临时金币和对象已清理。用户人工确认“全部正常（含 Grip 抓取）”，对应抓取、Tap 落格、红色禁放、升级、测试桩攻击与金币检查。真实眼动硬件与眼动权限授权流程未验收；0.5 秒停留规则有自动测试覆盖。
