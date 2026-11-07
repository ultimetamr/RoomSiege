# RoomSiege Audio/ 空间音频

实现音频模块及必要事件接入。后续真机反馈触发了无声修复与可听度回归；具体构建、测试与听感结果见设备测试报告。代码检查与 PCM 输出不能替代头显试听。

## HRTF 接入

- 使用项目已引用的本地 PICO Unity SDK 6.1.1 `XR/SpatialAudio`，没有添加新包或下载第三方声音。
- `ProjectSettings/AudioManager.asset` 选择实际原生效果名 `Pico Spatial Audio`，不是条件编译符号 `PICO_SPATIALIZER`。真实声道数设为 64，原有 Android ARM64 设置沿用。
- `RoomSiegePicoAudioContext.prefab` 引用 SDK 自带 `PXR_Audio_Spatializer_Context`，使用官方 FreeField 示例的 `SpatializerApiImpl.unity` 显式初始化后端。主摄像机添加 SDK 的 `PXR_Audio_Spatializer_AudioListener`；所有生成声源添加 `PXR_Audio_Spatializer_AudioSource`。
- 每个 AudioSource 使用 `spatialBlend=1`。单声道 PCM 经 SDK 组件提交，监听器 `OnAudioFilterRead` 输出 HRTF 双耳声音；`spatialize=false` 用于避免再次进入独立的 unity_native 后端，不代表关闭空间化。Mixer 仅保留 Attenuation，不使用另一后端的 Listener 效果。
- `SpatialSourceGain` 把源音量/静音和自定义距离曲线同步到 SDK 源增益；SDK 距离模式设为 None，避免重复衰减。关闭多普勒和房间反射。最小距离 0.45m、最大距离 12m；曲线横轴按最大距离归一化。显式设置 SDK 距离，防止 Editor OnValidate 覆盖 Unity 距离。
- 该版本 SDK 的 `ApiUnityImpl` 仍引用 `PicoSpatializer`，实际提供的同版本核心库名为 `yggdrasil`。工程 `Audio/Plugins` 为 Windows Editor 和 Android ARM64 放置相同二进制的名称兼容副本，未混用旧版 SDK；来源和范围见其 README。
- 如果启动时未选择正确空间化插件，音频模块明确报错并保持静音，避免把战场音效悄悄改为居中播放。此逻辑是运行时保护，未在本次任务中执行。

## 声源与行为

| 声音 | 世界位置与处理 |
| --- | --- |
| 敌人入场/脚步 | 从实际生成点发出第一声，随后按敌人世界位置发声；随移动速度/减速调整节拍，停止移动时不踏步 |
| Runner | 每一步将真实声源在敌人附近左右交替偏移 0.16m；不使用 2D pan 强制左右声道 |
| Tank / Boss | 更低频且较重的脚步 |
| 非激光命中 | 按 SourceTowerId 找到塔，在塔的位置发声；同塔多目标/持续命中按 80ms 限频 |
| 激光 | 持续声源位于塔的束流起点；失去目标、停火、卖塔、暂停、扫描等待或波次结束停止 |
| 波次号角 | 由 Waves 发出请求，在本波所选第一个出怪口上方 0.6m 发声 |
| 漏怪警报 | 当前安全区中心上方 0.15m；致命漏怪仍发声 |
| Boss 氛围 | 低频循环声在 Boss 附近半径 0.5m 缓慢绕行，保持 Boss 本身的方位线索 |
| UI 点击 | 从实际按钮的世界位置发出轻柔短音，暂停菜单操作仍可听见 |
| BGM | 低音量循环铺底，声源在头部前方 1.5m、上方 0.35m，仍为完整 3D/HRTF 声源 |

短音固定复用 40 个声源，警报/号角优先于密集脚步。Boss 和激光循环源各自回收复用，敌人对象池重用时按 SpawnId 重置音频状态。暂停时冻结战场短音进度与 BGM；解除暂停后继续。普通功能页只暂停战斗，BGM 继续播放，声音设置开启音乐可立即试听。所有声音由本地 C# 合成，作为当前工程的可替换基础素材。

## 声音设置

主屏底部“声音设置”展开 BGM 开/关按钮，关闭立即停止音乐。偏好保存到 PlayerPrefs `RoomSiege.Audio.MusicEnabled`，下次启动恢复；不改变战场音效。设置控件放在已有主屏 Canvas 内，继承原跟随、复位、暂停淡化。

公开接口：`RoomSiege.Audio.AudioDirector.Instance.MusicEnabled` 与 `SetMusicEnabled(bool)`。

## 必要接入改动与参考

- `EnemyController.Spawned` 通知真实入场；EnemyActor 增加只读 MoveSpeed 与 HitReceived 通知，不改伤害和经济。
- TowerView 增加静态发射/停火通知，避免 Towers 反向依赖 Audio。
- 原 WaveHorn 改为播放/停止请求，声音资源和 AudioSource 全部归 Audio/；WaveController 在开波前提供实际出怪口位置。
- 不改十波调度、攻击计算、敌人移动或经济规则。没有自动生成敌人；声音设置提供用户主动触发的顺序试听入口。

接入依据为本地 SDK 中的 `PXR_Audio_Spatializer_Context.cs`、`PXR_Audio_Spatializer_AudioSource.cs`、`PXR_Audio_Spatializer_AudioListener.cs`、`PXR_Audio_Spatializer_API.cs`、FreeField Building Block 及原生插件效果名；运行时 API 类名均取自这份已安装 SDK。


## 2026-10-28 部分声音缺失反馈后的修复

- 修正音乐暂停状态与战斗/页面暂停状态共用的问题。音乐只有先播放后才标记为暂停，恢复后若仍未播放则启动；功能页内开启 BGM 可立即听见。
- 合成素材统一峰值 0.7，避免 UI、机枪等素材在素材层和声源层重复使用过低增益。点击和悬停仍轻于战斗提示，战场声音仍按世界位置衰减。
- BGM 改为 100 BPM 八小节旋律、和弦与低音；不再只有低频正弦底音。保持低音量混音。
- 设置页增加“试听：点击 / 四塔 / 号角 / 音乐”，主动顺序播放，试听时暂时让 BGM 避让，不修改用户保存的音乐开关。
- 主摄像机 SDK Listener 之后增加 AudioOutputMeter，记录实际双耳 PCM 的块数与峰值 RMS；EditMode 进入 Play Mode 后逐项检查输出，并检查页面内开启 BGM 和关闭按钮反馈。

## 2026-10-28 真机音量回归

用户确认点击可听，但其他试听项过轻。UI 点击/悬停沿用已确认音量及旧距离曲线；战场短音和循环源增益乘 2.5（AudioSource.volume 限制到 1），BGM 从 0.18 调为 0.42。战场/BGM 近场扩为 1.5m 内全量，3m 为 0.75、6m 为 0.4、12m 归零，继续使用世界位置 HRTF。试听使用相同混音方案，页面按钮声音保持旧曲线。池化源切换用途时同步 SDK 增益使用的曲线缓存，同类重复声音不重复创建曲线。
本地输出回归加入可听信号下限，检查 UI 与非 UI 分流后有声音，不将非零信号直接当成头显听感通过。

## 冰墙音色与混音复听修正

用户反馈新版冰墙过响/刺耳。仅 Frost 命中与持续场使用 0.6 独立音源系数，涵盖实战、图鉴、应急冰冻及设置试听。持续场由 740/1480Hz（高次分量0.35）改为360/720Hz（高次分量0.12）；命中基频1100Hz改为520Hz。其他声音维持上一版。统一 EffectGain 避免试听与循环、短音混音分歧。
