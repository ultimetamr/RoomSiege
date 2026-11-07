# Vibe Lab GitHub 云打包

RoomSiege 的 Unity 源工程和云打包提交工程分开维护：

- Unity C# 源码：https://github.com/ultimetamr/RoomSiege
- Android Gradle 提交工程：https://github.com/ultimetamr/RoomSiege-VibeLab

Vibe Lab 的当前构建命令是在 Git 仓库根目录执行 `gradle assembleRelease`。Unity 源码根目录只有 Assets、Packages、ProjectSettings，没有 Gradle settings，因此会报“does not contain a Gradle build”。不要执行 `gradle init`，也不要用空 Android 壳替代游戏。

星轨织机同样保留独立的 OrbitalLoom 与 OrbitalLoom-VibeLab 仓库，后者才是同类云打包所需的提交入口。

## 从源码更新提交工程

1. 按 BUILDING.md 完成本地依赖接入，使用 Unity 6000.6.8f1。
2. 保存游戏修改并提交，保留准确的源码 Commit。执行 Unity 菜单 **RoomSiege → Export Vibe Lab Android Project**，或批处理调用 `RoomSiegeVibeLabBuild.ExportAndroid`。此入口使用 Release，不改变原有开发 APK 入口。
3. 导出位于 `Builds/VibeLab/UnityExport`；为避免覆盖已有交付，导出入口要求该目录尚不存在。更新时先将旧导出移到备份位置。
4. 使用 Unity 配套 Java17、Gradle9.3.1、NDK27.2.12479018，在导出目录执行 `:launcher:assembleRelease`，完成真实 ARM64 IL2CPP 编译。必须检查 `BUILD SUCCESSFUL` 与实际 APK，不能仅依据 Unity 退出码。
5. 在源工程根目录运行：

   ```powershell
   python Tools/Prepare-VibeLab.py --export Builds/VibeLab/UnityExport --output D:/BuildStaging/RoomSiege-VibeLab
   ```

   输出目录必须不存在。脚本检查 Release 导出、真实 ARM64 ELF、配套元数据及完整资源，移除原生编译工具/中间代码、缓存与本机路径，将 Android 包装适配到云端 Gradle 8.11.1 / AGP 8.10.1。游戏 C#、数值、资源内容保持不变。

6. 补充提交工程 README 和验证记录，使用 **Gradle8.11.1 / Java17** 在提交工程根目录执行 `assembleRelease`，检查签名、包名、图标和游戏内容。不要把只存在本机的 local.properties、APK、签名材料和日志提交。
7. 将提交工程推送至 RoomSiege-VibeLab；在平台选择这个仓库及对应完整 Commit ID。源码更新不等于提交工程自动更新。

Gradle8.11.1 的兼容版本依据为 Android 官方 AGP8.10 文档：https://developer.android.com/build/releases/past-releases/agp-8-10-0-release-notes 。如平台更新工具链，应重新核对再构建。

`Prepare-VibeLab.py` 只供维护者在已合法取得相关 SDK 的环境中生成发布提交材料；不下载或授予任何第三方 SDK 许可。源码仓库继续不包含完整 PICO SDK。
