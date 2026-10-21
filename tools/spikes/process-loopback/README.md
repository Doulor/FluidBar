# Process loopback spike（未完成，勿接入主程序）

目的：验证能否用 `ActivateAudioInterfaceAsync` + `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK`
按进程抓取 KuGou 的音频帧，从而得到帧级精确的播放时钟（替代 30 秒粒度的 KuGou.ini）。

## 当前状态（2026-10-22 07:1x）

- 项目可编译，能定位目标进程（KuGou pid 99424）；
- `ActivateAudioInterfaceAsync` 第一次返回 `0x80070057`（E_INVALIDARG）——原因是 activationParams
  必须是 **PROPVARIANT(VT_BLOB)** 包住 `AUDIOCLIENT_ACTIVATION_PARAMS`，已修正；
- 修正后可进入激活流程，但**在 `IAudioClient.Initialize` 附近崩溃**（进程无输出退出），
  说明 `IAudioClient` 的 vtable 声明或 `GetService` 的签名仍有误，需要逐项对照 SDK 头文件；
- 因此**尚未产出可信的帧计数**，主程序未接入。

## 下一步

1. 用 `IMarshal`/`ComImport` 的标准声明替换手写的 `IAudioClient`，或用 CsWinRT 生成；
2. 先只调用 `GetMixFormat` 打印格式，确认 vtable 正确性；
3. 再 `Initialize`（共享模式 + LOOPBACK + 2 s 缓冲）→ `GetService(IAudioCaptureClient)`；
4. 连续 5 秒统计 `frames / 48000` 与墙上时间之比，播放时应接近 1.000，暂停时应停止增长。

## 成功后的接入方式（主程序）

- 后台线程按 20 ms 拉取帧数，得到 `audioMs`；
- `位置 = 锚点 + audioMs`，换曲/seek 重新锚定；
- 先在设置里做成可选开关并**影子运行**（只记录不显示），连续 30 分钟误差 < 0.2 s 再切主路径。
