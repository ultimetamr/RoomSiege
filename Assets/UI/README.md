# RoomSiege 世界空间 UI 框架

本次代码只新增于 `Assets/UI/`，战场、四塔位、下一波/生命/金币和波次环均为占位。未加入战斗、塔、敌人或波次逻辑。

## 场景与面板

运行已有 `RoomSiege_Start` 场景，WorldPanelHost 自动创建一个不挂在头部下的世界根物体，以及四个独立的 WorldSpace Canvas：

- 主屏：战场占位，扫描状态和现有扫描控制，面板拉近、推远、复位及暂停/恢复。
- 左侧：MG、Frost、Laser、Trap 四个塔位占位，不可建造。
- 右侧：下一波、生命、金币占位，显示 `—`，不读取或伪造战斗数据。
- 顶部：固定示意进度环，标注“占位”，不是实际波次进度。

运行时隐藏旧启动面板和旧扫描面板，销毁新 UI 时恢复原先可见的面板。仅通过 ScanController 的公开接口保留真实扫描、模拟沙盘、格叠加和入口标定；不改写 Scan 的实现。

## 跟随与复位

- 默认锚点为头部前方 1.2m，视线方向额外下俯 8°，避免硬绑定头部。
- 在 LateUpdate（执行序号 10000）读取已更新的相机位姿，正常位置跟随严格使用 `Vector3.Lerp(current, target, 0.12f)`，旋转使用 Slerp 0.12。
- 小于等于 25° 的转头变化不改变面板的世界朝向；超过 25° 后以最多 60°/秒缓慢回正，到 2° 内停止，防止阈值附近抖动。头部位置平移仍平滑跟随。
- 距离以**四面板整体的主锚点**到头部距离计。小于 0.8m 或大于 2.5m 后，从当前有效位姿开始用 1 秒 SmoothStep 缓动回到默认 1.2m 锚点；持续越界/遮挡不反复重启恢复计时器。
- 首帧在首次渲染前直接初始化到默认锚点，不从世界原点飞入。
- 每帧检查 NaN、Infinity、无效四元数以及未初始化状态。异常根位姿使用最近有效渲染位姿兜底，再缓动回正；无效头部数据沿用最近有效头部，避免 NaN 进入插值。
- 每帧检查四个子面板的启用状态、固定局部布局、尺寸及缩放，修复被关闭或缩成零的 Canvas。操作目标是整个面板组，子面板不会被分别拖散。

每帧对四面板中心和内缩四角进行非分配射线检测，忽略 Trigger、自身和 CameraRig。Unity 场景碰撞遮挡触发回正。材质使用 `ZTest Always`、关闭深度写入并支持 UGUI 字体/透明度和 XR 双眼，保证场景几何遮挡时 UI 仍可读。没有建立碰撞体的真实物体不能被物理射线检测；本框架不新增房间深度/相机感知。

## 交互

- 用控制器射线抓住/拖动主屏标题栏，可移动整个面板组；控制器前后位移会改变面板目标深度。鼠标环境支持标题栏拖动与滚轮调距。
- “拉近”把操作目标设到 0.55m，“推远”设到 2.8m，位移同样通过 Lerp 平滑进行。进入越界范围后自动恢复，便于模拟器验收。
- “复位”请求从当前位置缓动到默认锚点，不直接瞬移。
- “暂停”只切换 UI 暂停标志，不改动游戏时间或状态机。“恢复”清除此标志。CanvasGroup.alpha 用 unscaledDeltaTime 在 0.25 秒内从 1 淡到 0.3，再平滑恢复到 1。
- 外部 `Time.timeScale == 0` 或系统 OnApplicationPause 也触发淡化，恢复它们后淡入。若外部游戏仍处暂停，UI 按钮不会擅自修改该暂停源。

## 接口

命名空间：`RoomSiege.UI`；程序集：`RoomSiege.UI`。

```csharp
var ui = WorldPanelHost.Instance;
ui.Follower.SetPaused(true);    // UI 淡化，不修改 Time.timeScale
ui.Follower.SetPaused(false);
ui.Follower.ResetToDefault();   // 平滑复位
ui.Follower.MoveDistance(2.8f); // 平滑推远，越界自动恢复
ui.Follower.Manipulate(worldPosition);
ui.Follower.EndManipulation();
```

WorldPanelMotion 为独立运动/淡化计算；WorldPanelFollower 负责真实相机、Transform、CanvasGroup 与物理遮挡；WorldPanelHost 负责四面板与旧扫描界面整合；PanelDragHandle 处理 UGUI/XR 拖动；ProgressRingGraphic 仅绘制占位环。

## 构建与验证

Unity 菜单 `RoomSiege > UI`：

- `Run EditMode Tests`：结果在 `TestResults/RoomSiege.UI.EditMode.xml`。
- `Build UI APK`：输出 `Builds/RoomSiege-UI.apk`，结果在 `Builds/ui-build-result.txt`。

2026-10-26：18/18 EditMode 测试通过，Android 构建成功、错误 0。测试覆盖四面板布局、未初始化/NaN/无效四元数、25° 死区与 90° 回正、0.12 插值、30/60/90 FPS 的近远 1 秒复位、持续遮挡、真实 Collider/Trigger、暂停单调淡化及 timeScale=0 淡化。模拟器已安装启动并确认四面板正常渲染。

模拟器人工交互验收：用户操作“拉近”“推远”“暂停”“恢复”并转头超过 25° 后，确认“均平滑跟随、自动回位，淡化正常”。此结论来自用户观察，运行日志没有独立测量这些操作的回位时间。

验证结论仅针对这些测试条件与模拟器；真实 PICO 头部跟踪噪声、不同性能条件下的舒适度和控制器手感仍应在目标真机确认。
