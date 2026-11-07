# 获取依赖、运行与构建

## 适用环境

- Windows，Unity **6000.6.8f1**，Android Build Support / SDK / NDK / OpenJDK。
- PICO Unity Integration SDK **6.1.1**，本项目使用 OpenXR **1.18.0**。
- Android 包名 `com.roomsiege.mr`，ARM64、IL2CPP、OpenGLES3，最低 API 29。
- 安装空间权限与系统扫描均由用户确认；不要用脚本默认同意权限。

仓库不包含 PICO SDK 或 Unity 的包归档。须从官方获取 SDK，并在第一次打开工程前运行 `Tools/Setup-Dependencies.ps1`。

## 接入 SDK

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Setup-Dependencies.ps1 `
  -SdkPath "D:\PICO-Unity-SDK-6.1.1\XR" `
  -UnityEditorPath "D:\Unity\6000.6.8f1\Editor"
```

`-SdkPath` 支持 SDK 的 `XR` 目录或包含 `XR` 的解压目录。`-UnityEditorPath` 支持 Editor 目录、Unity.exe 文件或版本安装目录。可先加 `-VerifyOnly` 检查输入路径，不写文件。

脚本会：

1. 检查 SDK 包名、版本、OpenXR loader 与两种空间音频库。
2. 复制官方 SDK 到本机 `Packages/com.bytedance.pico.xr`，不改动下载源目录。
3. 在本机副本修正 loader 声明的文件名；把旧虚拟显示示例的 InstanceID 调用改为 CompositionLayerManager 的原生图层 ID；如发现已知 `.meta` 合并标记则保留第一段。
4. 从当前 Unity 编辑器安装目录复制官方 Composition Layers 2.6.0 包。
5. 从同一 SDK 复制空间音频库别名到 `Assets/Audio/Plugins`；仓库保留这些库的导入 `.meta`，不发布二进制。

这些 SDK 文件被 Git 忽略，不要强制加入公共提交。PICO 调试面板的本地副本由 SDK 的编辑器初始化流程生成。

## 编辑器运行

Unity Hub 添加本仓库根目录，使用 `6000.6.8f1`。打开 `Assets/Scenes/RoomSiege_Start.unity`，点击 Play。完整菜单与战斗对象在运行时创建，因此未运行时的简化场景视图并不代表最终游戏 UI。

通过菜单 `RoomSiege/Scan/Configure MR Features` 可重新应用现有 MR 配置。切勿随意改回旧版 Unity 或移除 OpenXR/PICO 功能。

## Android 构建

正常路径：Unity 菜单 `RoomSiege → Build Android APK`，生成 `Builds/RoomSiege.apk`。

命令行路径（替换 Unity.exe 路径）：

```powershell
& "D:\Unity\6000.6.8f1\Editor\Unity.exe" -batchmode -quit `
  -projectPath "$PWD" -executeMethod RoomSiegeBootstrap.BuildAndroid `
  -logFile "$PWD\Build-Android.log"
```

构建前保存工程并避免同一工程被另一个编辑器占用。自定义图标钩子会设置普通、自适应与启动图；导出后再次覆盖旧式图标，防止 Unity 6 留下默认标志。

## Windows JDK 回环连接错误

本开发机曾在 Unity 启动的 Gradle 中遇到 `Unable to establish loopback connection` / `UnixDomainSockets.connect`。这不是游戏 C# 编译失败。Unity 已完成原生编译和 Android 工程导出后，可以使用同一生成工程完成封装：

```powershell
$editorPath = "D:\Unity\6000.6.8f1\Editor"
$projectPath = (Get-Location).Path
$previousOptions = $env:JAVA_TOOL_OPTIONS
$socketDir = Join-Path $projectPath "Temp\JavaNet"
New-Item -ItemType Directory -Path $socketDir -Force | Out-Null
try {
  $env:JAVA_TOOL_OPTIONS = "$previousOptions -Djdk.net.unixdomain.tmpdir=$($socketDir.Replace('\','/'))"
  Push-Location "$projectPath\Library\Bee\Android\Prj\IL2CPP\Gradle"
  try {
    & "$editorPath\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\java.exe" `
      -classpath "$editorPath\Data\PlaybackEngines\AndroidPlayer\Tools\gradle\lib\gradle-launcher-9.3.1.jar" `
      org.gradle.launcher.GradleMain assembleDebug --no-daemon
    if ($LASTEXITCODE -ne 0) { throw "Gradle failed: $LASTEXITCODE" }
    New-Item -ItemType Directory -Path "$projectPath\Builds" -Force | Out-Null
    Copy-Item "launcher\build\outputs\apk\debug\launcher-debug.apk" "$projectPath\Builds\RoomSiege.apk" -Force
  } finally { Pop-Location }
} finally { $env:JAVA_TOOL_OPTIONS = $previousOptions }
```

该示例假设工程所在路径无空格。若有空格，请改用无空格的短目录作为 `$socketDir`。不要为了此错误关闭安全软件或修改防火墙，也不要把旧 APK 当成本次构建结果。

## 验证范围与已知问题

- 本项目保留配置 / 难度、扫描、塔、波次、UI、存档等 EditMode 测试源码。Test Runner 运行 EditMode 测试。
- 历史测试记录只代表文档写明的版本和检查范围，不代表最新完整回归已执行。
- Unity 6 主工程打开和主菜单运行检查曾达到 Console 0 错误 / 0 警告。
- 最新真机检查已确认前台进程、输入初始化、音频输出、购买模式；房间读取曾连续返回 0 个锚点并回退沙盘，真实房间恢复尚待排查。不要把沙盘可运行当作 MR 扫描通过。
- 声明为预览版；尚无商店发布审核、所有头显型号兼容性或正式发售质量保证。
