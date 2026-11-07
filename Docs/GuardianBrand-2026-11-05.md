# 游戏标识、守护核心与购买模式

日期：2026-11-05。

## 本次交付

- 应用图标、Android 传统／圆形／自适应图标、Unity 自定义启动画面和 VR 启动贴图均接入 RoomSiege 能量晶体盾徽；关闭默认 Unity 启动标识。主菜单增加同款徽章。
- 守护核心由旧线框方盒替换为原创实体模型：装甲底座、三支悬浮支架、晶体和分段能量环。晶体轻微悬浮旋转，环反向旋转，漏怪时晶体短暂变红。
- 核心约 0.295m 宽、0.40m 高，底部贴地，保持原有 0.16m 核心建造避让半径。模型无 Collider，不改变放塔、路线、核心固定／平滑跟随逻辑。
- 构建副本与正式源配置均设置 `unlockAllContent=false`。免费基础塔为 MG、Frost、Laser、Trap；14 种扩展武器、54 款商品皮肤、12 种一次性道具需使用游戏内核心碎片购买。
- 通过既有购买账本迁移撤销试玩解锁和试用库存；保存已赚碎片、真实购买及尚未消耗的付费库存。旧试玩阵容中未购买武器替换回基础塔，未购买皮肤停止穿戴。
- 旧金评级免费全局换色不再自动应用；保留荣誉记录，将图鉴底部入口改为皮肤商店，结算说明同步更正。
- 保留上一版 UI 文字采样和抗锯齿修复。

## 资源路径与来源

- `Assets/Visuals/Branding/RoomSiegeIcon.png`：内置 imagegen 生成的图标。
- `Assets/Visuals/Branding/RoomSiegeAdaptiveIcon.png`：内置 imagegen 基于图标编辑的自适应安全边距版本。
- `Assets/UI/Framework/Resources/Branding/RoomSiegeEmblem.png`：主菜单运行时徽章。
- `Assets/Scan/Resources/GuardianCore/GuardianChassis.obj`、`GuardianHeart.obj`、`GuardianOrbit.obj`：原创网格，共 9,444 三角形，按材质分组，可单独编辑或替换。3D 模型通过项目内网格工具制作，未使用外部 AI 3D 服务。
- `Tools/generate_guardian_core.py`：可复现模型的脚本，复用既有网格工具并为弯曲支架使用平行运输截面，避免扭曲。
- `Assets/UI/Editor/RoomSiegeBranding.cs`：导入与构建前统一设置图标、启动标识。
- `TestResults/GuardianBrand-2026-11-05/GuardianCore.png`：实际模型渲染预览。

## 图像生成提示

方式：内置 imagegen，无 CLI/API 回退。

初始图标提示：

> Use case: logo-brand. Create a premium finished square application icon for an MR tower defense game named RoomSiege. Primary subject: a compact, exquisitely designed guardian energy core, a brilliant cyan faceted crystal suspended within a dark titanium three-prong protective cradle and an angular hexagonal shield silhouette. Use the game palette cyan #4FD8FF, deep midnight navy, subtle warm gold #FFD27A, small red enemy threat accents at the outer bottom edges. Refined science fiction industrial design, readable strong silhouette at tiny app icon sizes, restrained polished emissive edges, physically shaped facets with crisp clean antialiased boundaries, rich but not noisy. Full bleed dark navy background with subtle vignette, no transparent area, no rounded app icon container; dominant single centered emblem fills 78 percent of the square with comfortable margins, low visual clutter, professional commercial game identity. No letters, no text, no watermark, no Unity symbol, no guns, no mockup. This icon will be shipped in a Unity PICO MR game. Save a high resolution square PNG.

自适应图标编辑提示：

> Edit target Image 1: preserve the exact existing cyan crystal guardian-core shield emblem, its materials and visual identity. Change only scale and surrounding background for an Android adaptive application icon. Reduce the emblem to occupy the central 62 percent of the square in both width and height, with wide uniform deep navy (#030B16) padding extending to all four outer edges. Keep all important emblem details entirely within the central safe circle. Do not add an outer rounded-square border. Crisp high-quality antialiased edges. No text or letters. Output a square opaque PNG.

## 检查

- `Regression.xml`：38/38 通过，包含购买与迁移、图标设置、核心模型、核心固定／跟随保留武器、全商店布局、UI 采样和结算提示。
- 最终模型支架截面修正后，`ModelFinal.xml`：3/3 通过；渲染预览已检查。
- 首轮发现 MaterialPropertyBlock 不能在 MonoBehaviour 字段初始化中创建，已移至模型创建阶段；后续检查通过。
- 最终 Android 构建成功，BuildResult 报告 Errors: 0。
- APK：`Builds/RoomSiege-GuardianCore-Purchase-2026-11-05.apk`，64,036,603 字节。
- SHA256：`A3748C42D3E6FB0A29654BD45391C5C9A75CFD45908C87FB708D70DB7D72179E`。
- 已覆盖安装到 PICO 真机并发起启动；`app info` 确认 `com.roomsiege.mr` 进程运行（PID 11715）。
- 用户处理系统权限后，应用回到前台并正常进入 BUILD。真机日志确认完整扫描 1/8～8/8、真实房间 278 个可放格与 1 个入口、通路检查成功。
- 启动日志保存于 `TestResults/GuardianBrand-2026-11-05/DeviceStartup.log`。设备时间与电脑时间不同，本报告日期以电脑 2026-11-05 为准。


## 真机最终启动检查

- 购买权限日志：`playtest=False, weapons=4/18, skins=0/54, supplyUnits=0, loadout=Frost,Laser,MG,Trap`。
- 已关闭试玩解锁，只有四种基础塔可用；14 种额外武器、54 款皮肤与道具需购买。保留本地 1320 核心碎片。
- UI 清晰度配置保持：`glyphDensity=3, quality=High Fidelity, pipeline=URP-HighFidelity, MSAA=4, cameraMSAA=True`。
- 当前收集的应用启动日志未检出 Exception 或 FATAL；系统权限弹窗后已恢复前台。
- 本次确认了安装、启动、购买权限、扫描与进入建造；新图标和模型在头显里的最终观感仍以用户实际体验为准。
