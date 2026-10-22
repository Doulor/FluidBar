# Process loopback spike（研究中，勿接入主程序）

目标：用 `ActivateAudioInterfaceAsync` + `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK` 抓 KuGou 的音频帧，
得到帧级精确的播放时钟（替代 30 秒粒度的 KuGou.ini）。

## 已经打通的（2026-10-22 07:40）

1. `ActivateAudioInterfaceAsync` 的 activationParams 必须是 **PROPVARIANT(VT_BLOB)** 包住
   `AUDIOCLIENT_ACTIVATION_PARAMS{1, pid, 0}` —— 否则返回 `0x80070057`；
2. `IAudioClient.GetService` 必须声明为 `(ref Guid, out IntPtr)`（用 `out object` 会崩溃）；
3. `IAudioClient.Initialize(shared, LOOPBACK|AUTOCONVERTPCM|SRC_DEFAULT_QUALITY, 200ms, 0, 48k/2ch/16bit)`
   → **S_OK**；
4. `GetService(IAudioCaptureClient)` → **S_OK**；
5. 采集端改用**裸 vtable 调用**（`Marshal.GetDelegateForFunctionPointer`），避免 RCW 崩溃。

## 当前卡点

**GetBuffer 连续 5 秒返回 0 帧**（此时 KuGou 确实在播放，主程序日志显示 0:17 → 0:23 递增）。

可能原因（按优先级）：
1. 进程回环需要 **事件驱动模式**（`AUDCLNT_STREAMFLAGS_EVENTCALLBACK` + `SetEventHandle`），
   纯轮询可能拿不到数据；
2. KuGou 的音频渲染在**子进程**里，需要确认 `PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE` 生效；
3. 目标进程 ID 应取**音频会话所属进程**（可用现有的 `GetProcessPeak` 反查）。

## 下一步

1. 改成事件驱动（创建 event，`SetEventHandle`，`WaitForSingleObject` 后再 Drain）；
2. 打印 `GetNextPacketSize` / `GetBuffer` 的 HRESULT 与 flags；
3. 若仍为 0，改为枚举音频会话找到真正的渲染进程 PID 再试。

## 成功后的接入方式

- 20 ms 拉一次帧数 → `audioMs`；`位置 = 锚点 + audioMs`；换曲/seek 重锚；
- 先做成可选开关 + **影子运行**（只记录不显示），连续 30 分钟误差 < 0.2 s 再切主路径。

## 2026-10-22 08:05 追记

- 事件驱动模式（AUDCLNT_STREAMFLAGS_EVENTCALLBACK + SetEventHandle）**同样 0 帧**；
- 逐个 KuGou 进程（99424 / 16820 / 69168 / 87076 / 96688 / 97304）测试，**全部 0 帧**；
- 设备路径 `VAD\Process_Loopback` 确认正确；
- 改用 `GetMixFormat` 的格式初始化时，`Initialize` 前抛 `NotImplementedException`（接口声明或 RCW 问题）。

**最可能的原因**：播放器使用 **WASAPI 独占模式**（或音频渲染在非目标进程），此时进程回环拿不到数据。
验证方法：用 `IAudioSessionManager2` 读取该会话的 `AudioSessionState` 与端点即可确认；
若是独占模式，则这条路在酷狗上直接走不通，需要换回"ini + 播放时长"的方案。
