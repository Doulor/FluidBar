# Changelog

所有用户可见或架构级变更记录在这里。格式：日期（Asia/Shanghai）+ 摘要 + 影响。

## 2026-10-29 — 官网活动态加入《Take Me Hand》音乐样本 + 画廊式排版

- 材质选择器切到液态玻璃时会展开 440×284 的真实原型预览；液态玻璃深色 tint 从 68% 降到 30%，配合 `blur(16px) saturate(175%) brightness(1.04)` 透出桌面，不再在 idle 132×36 胶囊下看起来和黑曜石一样。
- 活动态移除整张音乐研究卡，只保留四个真实 compact 状态芯片；真实 media 状态仍通过「媒体 / 音乐」切换。
- 活动态媒体样本改为 `Take Me Hand`（DAISHI DANCE feat. Cécile Corbel），使用真实专辑封面与完整 29 行歌词时间轴；当前词按真实播放时钟连续填充，歌词行随时间推进，不再单行循环。不加载、不播放音频，媒体面板不显示播放/暂停按钮。
- 故事区新增音乐研究卡，封面、曲名、歌词、说明都在网页层可读；点击仍驱动同一个真实 media compact 状态。
- 全站 section heading 加入超大低对比 ghost wordmark（ISLAND / PLUGINS / 120 / FLUIDBAR），延续材质研究区的画廊式排版语言。
- 原型 media 视图支持封面图片，媒体页显示 `Wonder Tourism · 2012`；同步脚本照常复制 app.js/styles.css，封面资源随静态站发布。

## 2026-10-28 — 官方网站重建为日光光学单页（真实原型嵌入）

- 旧版 `website/` 视觉与代码完全退役；新站从空白 Vite + React + TypeScript 工程重建。
- 页面为冷银白亮色，产品区是 488×444 的深色真实桌面窗；首屏默认展示真实 expanded 440×284 控制中心，右侧页面索引直接驱动真实 pager。
- 首屏、故事区、外观配置器直接嵌入 `docs/rewrite/prototype2`，不在网页里重画灵动岛。
- 通过同源 postMessage 桥同步真实状态：idle 132×36、music 226×40、notifications 248×36、clipboard 264×36、agent 236×36，展开 440×284；形态和材质也直接写进原型。
- ReactBits vendored 六个 TS-CSS 组件；Shaders 作为材质研究的 WebGPU 黑曜石样本，WebGPU 不可用时保留 CSS 回退。
- 新增 Playwright 站点/可达性测试、字体子集、OG 图与 Cloudflare Pages 静态部署配置（`wrangler.toml`、`_headers`、`SITE_URL`）。
- 验证：`pnpm build` 通过；Vitest 5/5、Playwright 站点 5/5、可达性 2/2 通过。
- 部署：Cloudflare Pages 项目 `fluidbar` 已上线；`fluidbar.doulor.cn` 通过 `fluidbar-site` Worker 路由代理，真实 iframe、字体、Shaders 与 axe 站点检查通过。

## 2026-10-28 — 封面：通用默认图被当成真封面、好封面被晚到的差图顶掉、抓取被位置刷新反复取消

现场日志（`%TEMP%\fluidbar-pin-diag.log`）三条实证：
1. 同一首歌 300ms 内发起 3 次抓取：酷狗 250ms 一次的位置刷新不断取消正在进行的封面读取，谁先回来就用谁
   （`artwork.placeholder` ×3 → `origin=session-thumbnail bytes=18081`，18081 是酷狗的另一张通用默认图，
   同一首歌的真实封面是 26987 字节 / 97 色）。
2. `One Last Kiss`：曲库 `score=1.000 bytes=44875` 命中后，又被晚到的 `session-thumbnail bytes=15985` 覆盖；
   `Last Goodbye`：`player-cache 31214` 被 `session-thumbnail 15985` 覆盖。
3. `ArtworkRankKey` 用 trackKey 记"已发布等级"时守卫不触发——trackKey 会随播放器元数据补全而漂移。

- 修复：
  1. `ArtworkThumbprintRegistry`（新增）：同一份图片字节出现在**不同歌**上 → 判定为播放器通用封面并拒绝，继续走后备链路；
  2. 抓取节流 1.5s/首：位置刷新不再取消并重启同一条轨道的封面读取；
  3. **好封面不被降级**：按标题记录已发布封面的质量等级（session-thumbnail / player-cache / catalogue = 3），
     之后弱于它的候选记 `artwork.kept` 并丢弃；拿到等级 3 就停止"升级重试"（否则每 3 秒刷一条）；
  4. 曲库查询第三次尝试**去掉歌手**只按标题搜（酷狗常报翻唱歌手，导致有封面却查不到）；
  5. 打点补齐：`artwork.generic` / `artwork.kept` / `artwork.catalogue-hit|miss`（含 reason、source、bestScore），
     以及岛侧 `artwork.island hasArt=… bytes=…`（说明封面到底有没有送到胶囊）。
- 验证（本机实测）：`遗憾皆在风中` → `player-cache 48270` 送达岛；`慢慢爱` → 灰块拒绝 → 曲库 `score=1.000 68436` 送达岛，
  随后 `session-thumbnail 14419` 被 `artwork.kept` 拦下且不再重试。
- 测试：新增 `ArtworkThumbprintRegistryTests` 5 例；Platform 780 / Builtin 30 / Core 655 / Sdk 167 / Integration 31 /
  PluginHost 40 / Architecture 18 / UI 565 全绿（Island 486 里 `Liquid_glass_settled_frames...` 在整套并行跑时偶发超预算，
  单独跑通过，属既有的时间敏感用例）。
## 2026-10-28 — 同一个通知被反复弹出来（两个真实机制 + 打点）

- 症状：通知插件把"同一条通知"隔一会儿又显示一次。
- 机制一（id 抖动）：手机连接（Phone Link）会把同一条消息**用新 id 重投**——日志里同一秒出现
  `id=338005/338006/338007 app='QQ' title='4-301'`，每条都被当成新通知发布。
- 机制二（指针回退）：新通知从通知中心消失后，监听器返回的列表"最新一条"会**退回到更旧的通知**，插件只比较
  "id 是否变化"，于是把已经展示过的旧通知又弹一次（日志：三条 QQ 之后又 `plugin.notify.publish id=338003 AlwaysOnTop`）。
- 机制三（插件重启重放）：插件启动时会把当前快照"最新一条"当作新通知发布——插件重启即重弹。
- 修复（`NotificationsPlugin`）：
  1. **单调 id**：只发布比"已展示最大 id"更大的通知，旧通知回退时记 `plugin.notify.skipped-older` 并忽略；
  2. **内容窗口**：app+标题+正文完全相同且 5 分钟内已展示过 → 记 `plugin.notify.suppressed`，不再弹；
  3. **启动静默**：插件启动时把当前最新一条记为已展示（不发布），插件重启不再重放。
- 打点：`notifications.attach` / `notifications.added`（id/时间/应用/标题/正文）/ `plugin.notify.publish|suppressed|skipped-older|started|stopped`，
  都在 `%TEMP%\fluidbar-pin-diag.log`，下次再出现可直接定位是哪一层。
- 验证：实测日志里"同标题不同正文"的两条 QQ 群消息仍会各自弹出（内容不同，属正常）；
  旧通知回退重弹的路径由新增单测覆盖；插件 30 测试绿、UI 565 绿、全量 2766 绿。
## 2026-10-27 — 发布包改为自包含（修复用户端 "You must install .NET Desktop Runtime"）

- 症状：用户装上 4.0.0-preview.1 后弹 "You must install .NET Desktop Runtime to run this application."。
- 根因：`build/publish.ps1` 用 `--self-contained=$($SelfContained.IsPresent)`，而 **release workflow 没有传 `-SelfContained`**，
  每次发的都是框架依赖包（manifest 里 `"selfContained": false`），机器上没有 .NET 10 桌面运行时就直接被 apphost 拦下。
- 修复：
  1. `release.yml` 发布时带 `-SelfContained`；
  2. `publish.ps1` 加护栏：CI 里（`GITHUB_ACTIONS=true`）不带 `-SelfContained` 直接 **throw**，避免再发一次坏包；
  3. 单文件开启 `EnableCompressionInSingleFile`——自包含会嵌入整个桌面运行时，压缩后 exe 54.9 MB、zip 49.7 MB
     （原来的框架依赖版 exe 也有 58.7 MB，所以体积并没有变差）。
- 验证：自包含构建 `selfContained: true`；用 `DOTNET_ROOT=C:\does-not-exist` + `DOTNET_ROLL_FORWARD=Disable` 启动 →
  正常运行（框架依赖版在这种条件下必然启动失败）。
- 由于没有删除线上 release 的权限，坏掉的 4.0.0-preview.1 保留在仓库里，**4.0.0-preview.2 取代它**。
## 2026-10-27 — 补上昨天那条 URL 过滤的真实入口（descriptor 里没有标题）

- 昨天新增的 `IsResourceIdentifierTitle` 没有生效：`PollSessions` 构建 `MediaSessionDescriptor` 时把 **title 传了 null**，
  策略看不到标题，URL 会话仍然以"正在播放"胜出（诊断日志 14:24 仍是
  `app://./renderer/index.html?webcontentsid=39…#/av`）。
- 修复：descriptor 携带真实标题（每会话一次 `TryGetMediaPropertiesAsync`，读不到时退回按状态判断）；
  另在发布快照前加一道兜底——标题是资源标识时记 `media.reject-url-title` 并发布空快照。
- 附带收益："标题=应用名"的降权此前同样因为 title 为 null 而形同虚设，现在真正生效。
- 验证：全解决方案 **2762** 测试绿；成品运行后诊断日志不再出现 `app://`，若还有别家应用这么干会留下
  `media.reject-url-title` 证据行。
## 2026-10-27 — 灵动岛把页面 URL 当歌名显示（app://./renderer/index…）

- 症状：岛的媒体位显示 `app://./renderer/index.html?webcontentsid=8&processGroupNS=createBlankWindow1#/screen-record/result`。
- 根因：某个 Electron 应用（录屏结果页）发布了一条 SMTC 会话，把**页面 URL 当标题**、时长为 0。选择策略只对
  "标题等于应用名"做了降权（−400），URL 标题不属于应用名，于是它作为"正在播放"胜出。
- 修复：`MediaSourceSelectionPolicy.IsResourceIdentifierTitle`——标题是资源标识（含 `://`、以 `file:`/`data:` 开头、
  或以 `.html/.js/.json` 等页面/脚本扩展名结尾的路径）的会话**直接跳过**，背后根本没有曲目；
  岛改为显示真正的播放器或空闲。`AC/DC - Thunderstruck` 这类带斜杠的真歌名不受影响。
- 验证：新增 9 个测试用例（含这条真实 URL、`file://`、`https://`、`./renderer/index.html`、以及反例歌名与中文标题）；
  Platform 775 绿；成品 3.1.11-preview.1 运行后诊断日志中 `app://` 记录为 0，岛显示的是真实曲目
  （`track.caption title='Shivers' artist='Rachel Platten'`）。

## 2026-10-26 — 音乐媒体灵动岛宽度上限 +1/2（长歌词不再被截断）

- 需求：媒体胶囊显示歌词时的长度上限提高 1/2。
- 现状：歌词形态把宽度钳在 `LayoutTokens.ExpandedWidth`（440 DIP），而胶囊的固定槽位（封面 28 + 间距 + 波形 + 内边距）
  只占 86 DIP，所以歌词实际只有 **354 DIP** 可用，长句必然被省略号截断。
- 改动：新增 `LayoutTokens.MediaMaxWidth = ExpandedWidth * 1.5`（= **660 DIP**）作为歌词胶囊天花板，
  `SkiaIslandController` 的歌词分支改用它；旧版 `ShellMotionDriver` 的紧凑宽度钳位同步放宽，避免被它再压回 440。
  通知卡片仍用 `NotificationMaxWidth`（810），不受影响。
- 效果：歌词可用宽度 354 → **574 DIP**（约 50 → 82 个 7 DIP 字符），超出才省略。
- 验证：`MediaPillWidthTests`（4 例：660 = 440×1.5、长句拿满预算、短句不放大、长句在新上限下明显多显示十余字符）；
  运行中的成品打印 `compact.width ... ceiling=660.0`。全解决方案 2753 测试绿。

## 2026-10-26 — 撤回上一条的过度改动，歌词改为"本地文件为准 + 在线必须校验"

上一条把"KuGou 会话封面再跑一次曲库"当作改进，实际会让**曲库版本封面顶掉播放器自己的封面**（同歌手不同专辑/合辑
很常见），这正是"越改越坏"的那一步 —— 已撤回，播放器给了可用的封面就以它为准。

歌词侧的四个真实缺陷（全部修复）：

1. `KugouLocalLyricsProvider` 从未注册（历史提交只改过它自身与测试，没人接进管线）。
2. 它探测的目录只有 `KuGou.ini` 的 `LyricPath`（8.x 不再写）与 `%APPDATA%\KuGou8\Lyric`；这台机器的
   2390 个 `.krc` 在 `D:\KuGou\Lyric`。现在补上"运行中播放器安装目录旁的 Lyric"与各固定盘符探测。
3. KuGou 是**播放后才下载 `.krc`**（实测某曲 23:44 播放、23:48:53 才写入），所以开局查到的是在线版并被永久缓存。
   现在：本地索引 miss 时刷新一次再查；`LyricsService` 命中缓存但来源不是本地时，只重探本地 provider；
   岛上每 5 秒重探一次，直到本地文件接管。
4. 两个在线 provider 都取搜索结果**第一条、零校验**（实测给 "Fade (English Version)/Jeff Satur" 返回了
   "作词 : 小柯" 的另一首歌）。现在逐条按标题+歌手评分，且**两边时长都已知时时长差是硬否决**
   （同歌手不同版本只能靠时长区分）。

验证：`Fade (English Version)/Jeff Satur` 现在 50ms 命中本地 KRC（79 行、`source=kugou-krc`、
首行 `Fade (English Version) - Jeff Satur`）；成品运行打印 `lyrics.shown ... source=kugou-krc`；Platform 766 绿。

## 2026-10-26 — 歌词用回播放器自己的 .krc（本地、逐字、和播放器同源）

- 症状：某些歌"封面没用、歌词也是错的"。
- 根因一（歌词）：`KugouLocalLyricsProvider` 读播放器自己的 `.krc/.lrc` 早就写好了，但**从未注册进管线**
  （`PlatformServiceGraph` 里只有 netease + kugou 在线两个），于是只能走在线曲库，而在线版常常是**别的版本**：
  实测这首 `I Want You To Know (Hella x Pegato Remix)`，NetEase 给的是 **3:00** 结束、行时间 10.86/12.21/12.96，
  播放器本地 KRC 是 **3:47.86**、行时间 10.870/12.224/13.606 —— 词相同、时间轴不同，看起来就是"歌词错"。
- 根因二（找不到文件）：即使接上，它的目录探测也只有 `KuGou.ini` 的 `LyricPath`（KuGou 8.x 已不写这个键）
  和 `%APPDATA%\KuGou8\Lyric`；这台机器的 2390 个 `.krc` 在 **`D:\KuGou\Lyric`**（装在 D 盘）。
- 修复：`KugouLocalLyricsProvider` 排在歌词管线**第一位**；目录探测补上"运行中播放器安装目录旁的 Lyric"
  与各固定盘符的 `\KuGou\Lyric` / `\KuGou8\Lyric`。本地命中即离线、逐字（`wordTiming=True`）、与播放器一致。
- 修复（封面）：KuGou 的 SMTC 封面会滞后/给通用图（同一张 14.1KB 图一小时内出现在 4 首不同歌上），
  因此 KuGou 会话一旦拿到可用缩略图，仍会再跑一次**经标题+歌手校验的曲库查询**，用验证过的封面覆盖它。
- 诊断：每首新增 `lyrics.shown`（source/lines/first/duration）与既有 `artwork.origin`，下次可直接对照岛上显示内容。
- 验证：本地 provider 对这首 48ms 返回 49 行、`source=kugou-krc`、`wordTiming=True`；Platform 762 绿。

## 2026-10-26 — 封面还是灰的？KuGou 拿灰色占位图冒充封面

- 症状：修好通路后仍有歌曲"没有封面"。现场溯源（`artwork.origin`）显示这一类是
  `origin=session-thumbnail` 且**成功**取到了图 —— 但拿下来的是一张 **120×120 的灰色块**。
- 证据（`%TEMP%\FluidBar\art\*.png`，同一台机器）：4 张 4.6KB 文件**字节完全相同**，
  亮度 **spread=27、mean=216、无彩度**；同期真封面的 spread 是 71–253。也就是说 KuGou 对没有封面的
  曲目会返回一张几乎纯色的浅灰瓦片，我们把它当有效图接受，于是既画出一个灰方块、又**挡住了**本地缓存与曲库兜底。
- 修复：`ArtworkQualityInspector` 用 8×8 采样网格的亮度 spread 判定占位图
  （`spread ≤ 32` 且 `mean ≥ 100`；深色纯色封面因亮度低于阈值被保留），命中则当作"没有缩略图"，
  继续走 KuGou 本地缓存 → 曲库。
- 验证：用**生产程序集**跑本机真实产物 —— 3 张 4.6KB 灰块全部 `placeholder=True`，11 张真封面全部 `False`；
  并以实际曲目确认兜底可用（"In The Wind"/July 在 KuGou 曲库里精确命中专辑封面 `In Love`）。
- 测试：`ArtworkQualityInspectorTests` 6 例（含"细微渐变灰瓦片"这个真实形态，以及纯色深色封面必须保留）+
  一条把"占位图 → 本地缓存 → 曲库"整条链固定下来的用例；Platform 756 绿。

## 2026-10-26 — 音乐封面拿不到（Deezer/iTunes 不可达 + 系统代理吞掉国内请求）

- 症状：音乐页经常只有各种颜色的默认封面。
- 根因一（网络）：`itunes.apple.com` 在本机 **DNS 解析不到任何地址**，`api.deezer.com` 连接被拒 —— 两个曲库源
  常年失效；而 `%APPDATA%\FluidBar\artwork` 里最后一条缓存停在 **10-22**，之后再没写过。
- 根因二（更普遍）：KuGou 8.x 现在**会注册 SMTC 会话**，但会话缩略图是**零字节流**，而"读 KuGou 自己的封面缓存"
  只在窗口标题兜底路径上跑，于是有会话时永远拿不到封面。
- 根因三（最隐蔽）：系统开着 WinINET 代理 `127.0.0.1:7897`，`.NET HttpClient` 默认走它，而这个代理把国内
  域名直接吞掉而不是快速失败 —— 请求一直挂到超时（12s），而 curl（不读 WinINET）0.3s 就回来了。日志里
  `lyrics.provider-timeout`（netease/kugou）也是同一个病。
- 修复：
  1. 新增 `KugouArtworkSource`（KuGou 自有曲库，单请求、无 key、`{size}` 占位符换 400），排在源列表首位；
     注意该端点返回 `Content-Type: text/plain`，必须自己取字符串再反序列化（`GetFromJsonAsync` 会因媒体类型不符直接拒绝）。
  2. 新增 `MediaHttpClientFactory`：媒体出口（封面曲库、时长、歌词）**默认绕过系统代理**，
     `ConnectTimeout=4s`，可用 `FLUIDBAR_MEDIA_PROXY=system` 回到系统代理（需要代理的公司网络）。
  3. SMTC 路径补兜底：缩略图为空 → 读 KuGou 本地封面缓存 → 再退到曲库后台查询（`MediaArtworkFallbackPolicy`）。
  4. 每首歌写一条 `artwork.origin` 溯源行（session-thumbnail / player-cache / catalogue / none），便于下次定位。
- 证据：绕过代理后同一台机器 3 首歌全部命中 —— `Midnight City`/`晴天`/`Viva La Vida` 各 400×400，
  score 1.000，43–71 KB，102–390 ms（此前 100% 失败）；运行中的成品也打印
  `artwork.origin title="问心" origin=session-thumbnail bytes=24382`。
- 测试：`KugouArtworkSourceTests`（8 例：解析、选优、肖像图降级、阈值、网络失败、`{size}` 替换、来源分类）、
  `MediaHttpClientFactoryTests`（4 例：默认绕过代理、可换回、UA）、`ArtworkLiveTests` 新增真实网络用例；Platform 750 绿。

## 2026-10-26 — 灵动岛卡死（通知页）根因：文本省略号二分查找在 emoji 处不收敛

- 症状：岛经常**直接卡死**，多数发生在"消息/通知"页；整机鼠标也发钝。现场抓取（进程 `Responding=False`）：
  渲染线程 `Running` 状态已累计 **5 小时 41 分 CPU** —— 不是死锁，是**渲染线程死循环**。
- 现场调用栈（`dotnet-dump` + `clrstack -all`）：
  `SkiaFrameClock.OnTick → SkiaIslandHost.OnFrame → RenderAllLocked → RenderEntry →`
  `IslandRenderer.DrawSceneWithMaterialBlend → CompactRenderer.DrawNotification →`
  `TextEngine.GetLayout → TextEngine.Build → **TextEngine.Trim**`。
- 根因：`TextEngine.Trim` 的二分查找为了不切断代理对会把 `mid` **加一**，但随后用的是**被加一的 `mid`**
  去收缩区间。当 `low == high`、该位置前一个字符是 emoji 的高代理、且"前缀+省略号"仍超宽时：
  `mid → high+1`，`high = mid-1 = high` —— 区间不再缩小，**永久死循环**。通知标题里只要有 emoji
  并且需要截断就会中招。
- 证据：把已提交的旧循环原样取出，用真实字体度量扫描所有半像素宽度，5 个含 emoji 的标题（含
  `"Alert 😀 from the build server, branch main"`、`"普通中文通知标题 😀 后面还有很长的说明文字"`）
  全部扫到 100,001 步仍不收敛；修复后同样输入 **≤2 ms** 返回，且宽度都不超预算。
- 修复：把"探测长度 `cut`"与"二分位置 `mid`"分开，区间更新只用 `mid` 且严格收缩
  （`low = max(mid+1, cut+1)` / `high = min(mid-1, cut-1)`），另加**局部步数上限**
  （`TrimSafetySteps`，每次调用独立计数）作为兜底，任何将来的改动都不可能再把渲染线程挂死。
- 测试：新增 `Trimming_terminates_when_the_split_point_falls_after_a_surrogate_pair`（含扫描发现的
  19.0px 预算用例，5 秒看门狗）。已双向验证：换回旧 `Trim` 该测试跑不完，修复后 80 ms 通过。
- 顺带确认：日志里每 10 秒一条的 `island.mouse-hook-reinstall-failed … timed out` 是该卡死的**下游症状**
  （钩子线程被卡住的渲染/消息线程堵在后面），不是独立缺陷；卡死解除后不再出现。

## 2026-10-25 — 灵动岛玻璃：背景内容变化时实时刷新（不再只盯亮度）

- 症状：桌面背景变了，岛的玻璃材质一直是旧图，**要等岛上有别的活动（动画/媒体/交互）触发一次渲染才刷新**；
  停一会儿又好像恢复正常。属于"看起来时好时坏"的那类问题。
- 根因：岛空闲时靠 `_paletteTicker`（600 ms）里的"调色板巡检"唤醒渲染，而唤醒条件只有一条——
  抓屏区域**平均亮度变化 > 2%**（`SkiaIslandHost.OnBackdropUpdated`）。换个窗口、播视频、滚动页面、
  同亮度换色都不会改变平均亮度，于是永远不重画；等到有动画/媒体让帧循环跑起来，`RenderEntry` 每帧
  抓屏并采用新图，看起来就"又能实时刷新了"。
- 修复：给每帧抓取算一个**内容指纹**（`IslandBackdropProvider.ComputeSignature`：8×8 网格、每通道量化到
  16 级，避免静止桌面的模糊抖动误判），新增 `IslandGlassRefreshPolicy.ShouldWake`——指纹变了（或首次抓取、
  或亮度确实跳变）就唤醒帧循环。帧落地后记录"当前画的是哪一帧的指纹"，巡检也补记窗口矩形。
- 为什么不是"只要抓屏就重画"：那会每 600 ms 无谓提交一帧，并且不断取消空闲内存回收（`ScheduleIdleTrim`），
  得不偿失；指纹让静止桌面保持安静、变化时立刻跟。
- 测试：新增 `IslandGlassRefreshTests`（6 例）——**同亮度不同内容必须唤醒**（旧逻辑下必挂）、静止桌面不唤醒、
  亮度跳变唤醒、首次抓取唤醒、提供方指纹随内容变化且同样的画面指纹稳定；Ui 565 + Island 481 全绿。

## 2026-10-25 — 设置面板：真正的液态玻璃（自绘模糊、零延迟拖动、深浅两套材质）— 3.1.0-preview.1

- 背景：设置面板在"液态玻璃"材质下只是一块实色深板，用户报告"背景有时候有玻璃效果，有时候没有"。
  排查结论——DWM 系统亚克力（`DWMWA_SYSTEMBACKDROP_TYPE`）在**无边框窗口**上返回 S_OK 却什么都不画，
  所以这条路注定"登记成功、画面没有"；面板底色又被写死成不透明的 `#08080A`。
- 改为**自绘玻璃**：新增 `SettingsGlassBackdrop`，复用岛的管线（抓窗口背后桌面 → 1/6 降采样 →
  高斯模糊 → 饱和度 ×1.6 → tint 烘焙）。窗口标记 `WDA_EXCLUDEFROMCAPTURE`，抓的是背后而不是自己。
- **零延迟拖动**：抓一次"窗口 + 四周各留半窗余量"的大区域，移动时只做偏移采样 + 逐行 tint
  （纯内存，实测中位 **0.39 ms**、P95 0.81 ms，无抓屏），走出范围才重抓；松手 160 ms 后补一次
  合成桌面（WGC）抓取校正内容。上一版是每次移动抓屏 + PNG 往返 + 换画笔，拖动明显掉帧。
- **深浅两套材质彻底分开**：每种主题一套 `GlassMaterial`（顶部/底部 tint + 明暗双向 scrim）。
  浅色：背后越暗白 tint 越浓（0.34→+0.45 提亮），保证面板发亮；深色：越亮越压暗（0.62/0.76 + 0.30）。
  曾经共用一个方向，导致"深色和浅色只有一个会好看"。
- **帧上盖主题戳**：tint 烘在位图里，所以"为哪个主题烘的"必须记录；`ApplyPanelFill` 发现戳与当前
  主题不符就先重合成再画，绝不把另一个主题的材质贴上去（这是"只有一个好看"的另一半原因）。
- 文字对比：玻璃面板下浅色灰字加深（`#59626F→#333B47`、`#666D78→#4E5764`），深色灰字提亮
  （`#9B9BA5→#CBCBD6`、`#86868F→#A8A8B6`）；不透明面板与岛本体 token 未动。
- 首帧即玻璃：打开时先做一次**同步 GDI 抓屏**（~2 ms），不再有"前几十毫秒是纯渐变"的过渡态；
  删掉会跟时机赛跑的 `SetWindowCompositionAttribute` accent 叠加层（它决定面板偏不偏暗）。
- 玻璃层位图缩放改为 `HighQuality`（1/6 烘焙图放大 6 倍时的块状颗粒即"噪点"）。
- 强调色色点：控件从 32 px 改为 36 px，三层环的半径统一由控件半宽推导并夹紧 —— 之前选中环外沿
  17 px > 半宽 16 px，四边各切一条，虚焦点环被切碎后就是"很多噪点"。环几何抽成 `RingGeometry` 便于测试。
- "取自封面"与自定义强调色互斥且**即时生效**：之前切到取自封面，色点的选中环要切页面重建才消失。
- 双击 exe 现在会呼出设置：`LayeredWindowHost.ActivateRequested` 此前没有任何订阅者（死接口）。
- 测试：新增玻璃材质（深浅分离、暗/亮背景行为、偏移采样不增加抓屏、越界返回 false）、玻璃调色板
  （深浅各自方向）、色点环几何、强调色互斥；UI 套件 559 绿。
- 已知取舍：拖动期间用的是缓存的区域快照（背后播视频时是静态的），松手后刷新；要做到视频级实时就
  必须每帧抓屏，与零延迟互斥。

## 2026-10-24 — 预览版免证书自动更新：签名从"必需"降为"预览期可选"

- 背景：应用内自动安装要求 SHA-256 + Authenticode 双通过，而项目没有代码签名证书 —— 这对"单文件便携 exe"
  不是 Windows 的要求，是更新器自己的策略。用户暂时不打算买/申请证书（证书需本人身份+付款+私钥，无法代办）。
- 新增 `UpdateVerificationStatus.VerifiedByHashOnly`：hash 对上但没签名时如实报告"仅校验了完整性"，
  不再冒充 `Verified`。`UpdateDownloadResult.CanInstall` / `UpdateService.CanInstall` 接受该状态。
- `CompositeUpdateVerifier` 增加 `requireSignature`（默认 true）：为 false 时放行**未签名**，
  但 `Invalid`/`UntrustedRoot`/`CheckFailed` 依然拒绝（未签名是政策选择，签名损坏是攻击）。
- `UpdateServiceOptions.RequireSignature`（默认 true）。`AppUpdateCoordinator` 按构建形态决定：
  informational version 带 semver 预发布标签（如 `3.0.0-preview.1+sha`）→ 预览版，放开；
  稳定版保持强制签名。`FLUIDBAR_UPDATE_REQUIRE_SIGNATURE=0/1` 可覆盖（验收用）。
- About 页在状态行如实显示"已下载（已校验 SHA-256，未签名预览版）"（新增 i18n key
  `Settings.About.Update.ReadyUnsigned`，en/zh）。
- 新增 `docs/rewrite/signing-application.md`：证书申请材料清单 + 申请表填写模板（SignPath Foundation /
  Certum OSS / Azure Trusted Signing 对比），拿到证书后 CI 怎么接。
- 测试：hash-only 状态、默认拒绝未签名、放开后仍拒绝损坏签名、无 hash 且无签名 → 不可装、
  预览版识别（theory 5 例）；全量 2690 绿（除 Island 既有 flake）。

## 2026-10-24 — 更新检查：能看见预览版 release，空 feed 不再报"检查失败"

- 现状复盘：更新链路（检查→下载→校验→显式安装）早就完整，41 个测试；但线上只有两个 `1.0.0-dev` 预发布，
  且 `ReleaseManifestParser` 在 stable 渠道会把所有 prerelease 直接跳过 → `SelectRelease` 返回 null →
  `UpdateCheckClient` 报 `Failed("No usable release found")`。于是默认设置下点「检查更新」看到的是**失败**。
- `ReleaseManifestParser.SelectRelease` 增加**预发布兜底**（`allowPrereleaseFallback`，默认 true）：
  stable 渠道没有 stable 版本时退回最新的 prerelease；有 stable 版本时 stable 永远优先。渠道过滤从
  `TryParseRelease` 移到调用处（两个候选各取最新）。
- `UpdateCheckClient`：feed 里没有可用 release 是"没有可装的版本"，不再算失败（HTTP 与 `file://` 两条路径），
  状态置 `UpToDate`。
- About 页新增「打开发布页」按钮（有可用 release 时出现，`Available` 与校验失败两种状态都有）：
  未签名的预览包在应用内永远不会被自动执行，但用户仍能一条路走到发布页手动下载。
- 顺带把 About 页里 4 处 fire-and-forget 的更新动作（`CheckNowAsync`/`DownloadCurrentAsync`）改成观察式投递，
  和歌词那轮同样的坑（迟到异常不该靠 finalizer 兜底）。
- 新增 5 个测试：stable 渠道在没有 stable 时退回预览版、有 stable 时优先 stable、可关闭兜底、
  空 feed → `UpToDate`、只有预览版的 feed 在 stable 渠道报 `UpdateAvailable`。
- 全量 2680 测试绿。
- 提醒：真正的"应用内自动安装"仍需 Authenticode 签名（`CompositeUpdateVerifier` 会拒绝未签名包），
  见 `docs/rewrite/release.md` §4。

## 2026-10-24 — 收拾两个遗留隐患：歌词未观察异常 + 启动失败无声

- 歌词请求的 `SSL connection could not be established` 一直往 `crash.log` 写 `source=task` 未观察异常：
  `LyricsService` 用 `WaitAsync(providerTimeout)` 做超时，超时后 provider 的任务被**遗弃**；它稍后失败
  （TLS 握手被断是常态）时没人观察，finalization 阶段升级成 `TaskScheduler.UnobservedTaskException`。
  现在 provider 任务一创建就挂 `ObserveLateFault` 观察器，fire-and-forget 的 `RunRequestAsync` 改为
  `RunRequestSafelyAsync`（异常记 `lyrics.request-failed`，不再靠 finalizer 兜底）。
- 启动失败可能完全没有磁盘痕迹：JSON-lines 日志是缓冲的、进程一死就丢，且 `Program` 里
  `_ = bootstrap.StartAsync()` 是 fire-and-forget。现在 `AppBootstrap` 的 `app.start-failed` 额外同步写
  `crash.log`，`Program` 改成 `StartBootstrapAsync`（失败必定落盘）。
- 生产路径本来就不会回退到演示数据（`Data = _platformActions ?? _emptyData`，10-19 已修），这次只补证据链。
- 新增 2 个测试：超时后被遗弃的 provider 失败不再升级为未观察异常（去掉观察器该测试即失败，已实测）；
  超时后的遗留失败不会把同一首歌的 single-flight 卡死。
- 全量 2675 测试绿。

## 2026-10-24 — 修复「突然卡死然后崩溃」：液态玻璃背景图野指针（0xC0000005）

- 现象：岛闲置几秒后，随便一碰（悬停 / 点击 / 数据刷新）整个进程消失。Windows 事件日志
  `Application Error` → `0xc0000005`（模块 coreclr.dll）、`.NET Runtime 1026` 的栈三次一致：
  `sk_image_get_width → SKImage.get_Width → SkiaIslandHost.CopyImage → RenderEntry`（今天 11:45 / 12:35 / 12:37）。
- 根因（所有权越界）：`IslandBackdropProvider` 按「Get 调用次数」退休并 Dispose 自己缓存的帧
  （`RetireFrames=12`），而 `SkiaIslandHost._backdropCurrent` 直接持有这张**借来的** SKImage 跨帧渲染。
  11:32 新增的空闲调色 watch（600ms）在帧时钟停摆时也会调 `Get`，于是静止约 7 秒就能烧完这 12 次配额；
  下一次真实渲染 `CopyImage(_backdropCurrent)` 读到已释放的 native 对象 → 访问违例 → 进程终止。
  （10-23 的 `RetireFrames 2 → 12` 只是在推迟这个野指针，没修所有权。）
- 修复：
  - 宿主每采纳一帧立刻 `CopyImage` 成**自己拥有的** SKImage（`FromPixelCopy`）；`_backdropCurrent` /
    `_backdropPrevious` 只在宿主内部轮转，`_backdropSource` 记录来源做同一性比较。provider 何时释放都伤不到渲染。
  - `IslandBackdropProvider.Get` 只返回**本矩形**的帧（旧代码返回"全局最新帧"，多显示器会把另一块屏的桌面画进来）。
  - `SkiaIslandHost.RenderNow` 补 `catch`（原来只有 `try/finally`）：单帧异常只记日志，不再穿过 Avalonia
    dispatcher / WndProc 打死进程（10-22 的 emoji 半代理对崩溃走的也是这条路径）。
  - 新增测试接缝 `IslandBackdropProvider.CaptureOverride`（无桌面也能驱动抓屏）。
- 新增 `tests/FluidBar.Ui.Tests/IslandBackdropOwnershipTests.cs`：宿主副本在 provider 释放后仍可读、
  provider 绝不返回别的矩形的帧、退休窗口内旧帧确实被释放（即崩溃的前置条件）。
- 验证：Ui 539/539 绿；`dotnet test FluidBar.sln -m:1` 除 1 个既有 flake 外全绿
  （`LiquidGlassTests.Liquid_glass_settled_frames_stay_in_budget_and_allocate_nothing` 在**未改动**的基线上同样失败）。

## 2026-10-24 — 移除多 agent 堆叠环形图标，恢复单环

- 用户决定：多 agent 的环数难以和数据源对齐（一个对话在 DSH 里有多条 session 记录），整体回退。
- 回退：`AgentRingStack`、`AgentToolPalette`、`AgentLiveCount`/`AgentRingColors`、`IsLive` 存活窗口、
  DSH 的"空闲会话"与"关闭检测"全部删除；紧凑胶囊 / 分裂小岛 / 展开页恢复为单一圆环实现。
- 删除 3 个相关测试文件，恢复 `Poll_AdoptsExistingIdleSessionsSilently`。
- 全量 2669 测试绿。

## 2026-10-24 — 一个会话却有三个环：同一条对话有多个 session 记录

- 实查投影缓存：`3a2e4fd1` / `65c586b7` / `f57ae7f1` 三条记录**标题相同**（灵动岛新增液态玻璃材质），
  即同一条对话的多个 session id（重启/子会话），各自被算一个环。
- 处置：① 空闲行只在"最近有用户输入"时产生（读 `sessionListMetadata.lastPromptAt`）；
  ② 计数按 **项目 + 会话标题** 去重，一条对话 = 一个环；③ 心跳行 2 分钟过期（上一提交）。
- 用真实缓存模拟：去重后 = **1** ✓（三条记录里两条是 dup）。
- 全量 2680 测试绿。

## 2026-10-24 — 空闲标签页窗口收紧到 10 分钟

- 投影缓存里仍有昨天的会话文件，30 分钟窗口会把它们当成"开着的标签页"→ 又出现多环。
- `AgentContentMapper.LiveWindow` 30 → **10 分钟**；`DshSessionProvider.OpenTabWindow` 同样 10 分钟。
- 全量 2680 测试绿。

## 2026-10-24 — 只有一个会话却显示三个环

- 两个原因：① 我调试时留在 `agent-events\inbox` 的 `flicker-test-0001.json`（status=running）每次启动都会
  生成一个永不结束的 agent 行；② 心跳型的 running 行没有过期机制，崩溃/关掉的 agent 会一直占一个环。
- 处置：删掉那条遗留事件；`AgentItemData.IsLive` 区分两种窗口 ——
  running/其它 **2 分钟**（靠心跳刷新），waiting（空闲但开着的标签页）**30 分钟**。
- 新增 `A_stale_running_row_stops_counting`。
- 全量 2680 测试绿。

## 2026-10-24 — 通知卡片的背景也能实时跟随桌面

- 现象：主胶囊已经能实时跟随，但通知卡片（主胶囊下方的**次级 pill**）偶尔仍是旧色，等有活动才刷新。
- 原因：抓屏 mask 里只有壳体 / 分裂胶囊 / 预览带，**没有次级通知 pill**。通知 pill 的深色玻璃被烘进背景，
  亮度采样因此被它自己拉低 —— 是否命中取决于 pill 的尺寸与位置，所以是"偶尔发生"。
- 修复：`BuildIslandMasks` 补上 `SecondaryShellRect`（±10 DIP）；亮度统计窗口在有通知时也延伸到
  pill 底部，让调色板按"整块岛面"的亮度决策。
- 全量 2690 测试绿。

## 2026-10-24 — 空闲时也跟随桌面明暗（修"改成亮色背景不变色"）

- 原因：抓屏只在渲染帧里触发。岛静止时帧时钟是停的 → 永远不抓屏 → 玻璃保持旧的明暗，
  直到下一次活动（点击/媒体/agent）才重画。
- 新增 **idle palette watch**（`UiThreadTicker`，600 ms）：可见 + 液态玻璃 + 帧时钟未运行时，
  按当前窗口矩形请求一次抓屏（与渲染同一套 mask，避免把岛自己烘进背景）。
- 抓屏回来后比较原始亮度：变化 > 0.02 才置 `_backdropDirty` 并唤醒帧时钟；
  `IslandFrameLoopPolicy.ShouldRun` 新增 `backdropChanged` 输入。
- 亮度缓动结束后清除脏标记并记录基线，桌面没变化时依旧零帧。
- 实测：白色壁纸切换后 0.6 s 内主胶囊颜色从 (20,20,22) → (44,41,41)，恢复壁纸后又回到 (27,27,29)，
  全程没有任何交互。
- 新增 `ShouldRun_WhenOnlyTheBackdropChanged`；全量 2682 测试绿。

## 2026-10-24 — 环数实时化 + 三处圆环样式统一

- 环数是"打开的会话数"，但之前只在重启后才对：DSH 空闲会话根本没发过事件（启动即存在者被静默收养）。
- `DshSessionProvider`：空闲会话按文件写入时间发一次 `waiting`（时间用 mtime，不会被误判成最新），
  超过 10 分钟没有写入则发 `completed`（"已关闭"）——环随之增减，实时生效。
- `AgentItemData.IsLive` 回归实时语义：Running/Waiting 才算，完成即掉环。
- 圆环统一：展开页的 agent 行也改用 `AgentRingStack.Draw`（行内单环、颜色取该行的工具色），
  与紧凑胶囊、分裂小岛共用同一实现；新增 `PageRenderer._glow`。
- 测试同步更新：`Poll_AdoptsExistingIdleSessionsAsOneQuietRow`、`AgentLivenessTests`。
- 全量 2679 测试绿。

## 2026-10-24 — 环数改为"打开的会话数"（修"只看到一个环"）

- 原因：环数只统计 Running/Waiting，而已空闲但开着的 Harness 会话被算作已完成 → 只剩 1 个环。
- 新增 `AgentItemData.IsLive`：非 Failed 且最后事件在 20 分钟内；`CountLiveAgents`/环颜色都改用它。
- 新增 `AgentLivenessTests`。
- 全量 2679 测试绿。

## 2026-10-24 — 环颜色改为按工具品牌色（同工具同色）

- 之前第二环/中心点用固定色相偏移，与工具无关 → 三个 harness 会话也显示三种颜色。
- 新增 `AgentToolPalette`（AgentKind → 品牌色，未知回退强调色）；`AgentRingStack.Draw` 改为逐槽颜色。
- 新增 `AgentToolPaletteTests` 与 `Ring_colours_come_from_the_slot_tool_colour`。
- 全量 2675 测试绿。

## 2026-10-24 — 堆叠环形图标推广到分裂小岛

- 抽出共享实现 `AgentRingStack.Draw`，紧凑胶囊与分裂小岛共用同一套环语言、配色与合并动效。
- 数量统一来自 `IslandRenderState.AgentLiveCount`（Running/Waiting 会话数，夹 1..3）。
- 新增测试 `The_split_satellite_ball_uses_the_same_stacked_glyph`（强制 count=1 即失败，已实测）。
- 全量 2672 测试绿。

## 2026-10-24 — 多 agent 堆叠环形图标（最多 3 个）

- 新增 `AgentRingStack`：1 个 = 原圆环；2 个 = 内层逆时针 1.35 s 环（色相 +42°）；
  3 个 = 中心实心发光圆点（色相 −46°）。
- 出现/消失有"从外环分裂 / 融回外环"的插值动效（每帧 28% 缓动）。
- 数量来自 agent 页里 Running/Waiting 的会话数，夹到 1..3。
- 新增 `AgentRingStackTests`（强制 count=1 时两条断言失败，已实测）。
- 全量 2671 测试绿。

## 2026-10-23 — 预览带紧邻岛也跟随材质

- 根因：DrawNeighbour 显式传了固定填充色，而材质钩子只在 fill 为 null 时生效。
- 紧邻岛底色改走材质，按压/悬停变成叠加的状态色。
- 测试补断言：紧邻岛区域液态 vs 黑曜石 >100 像素差异（改回固定色则失败）。
- 全量 2668 测试绿。

## 2026-10-23 — 预览小岛（mini rail）支持液态玻璃

- TransientStripRenderer 增加 GlassPainter 钩子，走与壳体相同的 TryPaintBackdropSurface 入口。
- SkiaIslandHost 抓屏 mask 补一条壳体下方 56 DIP 的带子，避免预览带被抓进背景图。
- 图标/标签颜色跟随材质调色板（白底瓷玻璃时自动变深色）。
- 新增回归测试：液态 vs 黑曜石在预览带区域需有 >200 像素差异（关闭钩子则为 0）。
- 全量 2668 测试绿（Bluetooth 用例为已知 flake，单跑两次通过）。

## 2026-10-24 — Home 快捷区改成四个：Wi-Fi / 蓝牙 / 番茄钟 / 深浅色

- 番茄钟（Pomodoro）替换掉不可用的专注助手格子：`PomodoroTimer` 纯状态 + 控制器 1 Hz 心跳，
  运行中格子里显示 `24:31` 倒计时并点亮，结束后显示"完成"，再点一次重新开始。
  岛只在每个显示的秒数变化时重绘一帧，空闲仍为零帧。
- 深/浅色主题格子重新回到 home：点击即翻转 `AppSettings.Theme` 并持久化（后端早已实现，只是没摆上 UI）。
- 快照协议 `IslandQuickToggle` 第 2 格由 `Focus` 改为 `Pomodoro`；平台 seam 对该格明确返回
  "owned by the island controller"，不再假装支持。
- 新增/更新测试：4 个格子的顺序与图标、主题跟随设置、番茄钟倒计时与完成/重启；控制与 UIA 测试同步到 4 格。

## 2026-10-23 — 启动首帧不再露出旧样式

- 液态玻璃首帧需要桌面抓屏；为 null 时按设计退回不透明填充 → 启动瞬间"很丑的第一版样式"。
- 现在首帧等到抓屏到位再提交（上限 900 ms），并用 _awaitingFirstGlass 钉住帧循环。
- 冷启动采样（40 ms 间隔）确认无"先丑后好"台阶。
- 全量 2667 测试绿。

## 2026-10-23 — 默认关闭折射带（外圈 8 Hz 频闪的根源）

- 折射带在淡入期间被跳过：抓屏 120 ms / 淡入 110 ms → 每周期只画 ~10 ms，等于 8 Hz 频闪。
- `RefractionScale` 默认 1.05 → **1.0**（关闭）；清晰边缘 + 亮度滞回保留。
- 代码路径保留，调用方可显式 opt-in；两条折射带测试仍然覆盖。
- 全量 2667 测试绿。

## 2026-10-23 — 内外层差异：边缘亮度加滞回（修"只有外圈闪"）

- 内层填充随亮度只变 1.9 倍、且是 0.3 级别的均匀面；外层 key line 变 7 倍、sheen 变 3 倍，又是 1 px 亮线
  → 同一份亮度抖动在内外两层的可见度差一个数量级。
- `LiquidGlassPainter` 为边缘亮度加 0.15 的滞回：桌面稳定时外圈完全静止，换深/浅页面时才平滑跟随。
- 全量 2667 测试绿。

## 2026-10-23 — 外圈"模糊的闪"：抓屏淡入期间不画折射带 + 降低带子 alpha

- 外圈闪的载体是折射带（把模糊背景放大 5% 贴回边缘，天生模糊），每 120 ms 跟新抓屏重画一次。
- 淡入期间两趟都不画折射带，结束后再出现；带子 alpha 0.34/0.30/0.26 → 0.22/0.19/0.16。
- 清晰边缘（key line + 高光 + caustic + fringe）不变。
- 全量 2667 测试绿。

## 2026-10-23 — 展开卡片外圈微闪：折射带随内部一起淡入 + 缓存不再每帧重建

- 交叉淡入时折射带只在"新图"那趟绘制，外圈比内部提前跳到新画面 → 每 120 ms 一次微闪；现在两趟都画。
- `EdgeSetRef` 单槽缓存 → 4 槽；折射带路径缓存 4 → 8，避免面板/胶囊/残影尺寸交替时每帧重建。
- 新增 `The_rim_dissolves_with_the_interior_instead_of_jumping`（修改前后皆通过，属逻辑修正）。
- 全量 2666 测试绿（Bluetooth 用例是已知 flake，单跑通过）。

## 2026-10-23 — 找到"一闪一闪"：背景交叉淡入稀释了壳体不透明度

- 根因：交叉淡入把旧图 (1−t) + 新图 t 各画一次，合成 alpha = 1−(1−t)² —— t=0.5 时只有 75%，
  每 120 ms 抓屏一次就把壳体稀释一次，桌面透出来 = "一瞬间透明"。
- 修复：旧图满不透明度打底，新图按 blend 叠加（合成 alpha 恒为 1）。
- 回归测试 `Cross_fading_the_backdrop_never_makes_the_shell_transparent`（修前 alpha 224/191，修后 >240）。
- 全量 2666 测试绿。

## 2026-10-23 — 分层窗口双缓冲 DIB（修"一闪一闪"）

- 诊断：闪光探针从未触发 + WGC 连续采样无亮度跌落 → 内容始终完整，问题在**呈现**。
- 根因：`LayeredSurface` 直接画进合成器要上传的那块内存（类注释原文），60 fps 下合成器会读到半张表面。
- 修复：ping-pong 双 DIB，绘制永远落在背缓冲，提交成功后才翻转；App 每帧整帧重绘。
- 顺带：闪光探针补到局部提交分支；集成测试对齐新的通知优先级策略（a352e7a）。
- 全量 2665 测试绿。

## 2026-10-23 — 去掉抓屏时刻的带外提交 + 背景混合兜底 + 常驻闪光探针

- 抓屏回调不再插 `UpdateLayeredWindow`（只唤醒帧时钟），避免与 WGC 合成撞车。
- `DrawBackdrop`：无上一张图时强制 blend=1，杜绝"纯 tint 透明帧"。
- 常驻探针：提交后岛中心 alpha < 16 时写 `%TEMP%\fluidbar-flash.log`（含上下文与离屏同点 alpha）。
- 全量 2664 测试绿。

## 2026-10-23 — 原子帧：不再直接画进正在显示的 DIB（修"闪透明"）

- 频率随抓屏间隔变化的线索表明：问题在每次抓屏触发的那次重绘。
- 根因：场景直接画进分层窗口的 DIB（正在显示的内存），画到一半就可见 → 半张透明表面。
- 修复：离屏 SKSurface 绘制 + 完成后一次性 memcpy 进 DIB；离屏表面重建后强制整帧。
- 顺带修：OnBackdropUpdated 在抓屏线程调用 EnsureFrameLoop（非线程安全）→ 现在全部回到 UI 线程。
- 全量 2664 测试绿。

## 2026-10-23 — 取色只看岛足迹 / 响应提速 / 整帧提交修闪透明

- 亮度只在**岛足迹**（壳体 ±6 DIP）内统计：旁边的亮窗口不再改变玻璃颜色。
- 抓屏 400→120 ms、亮度缓动 τ 160→80 ms、交叉淡入 160→110 ms（总响应约 0.3 s）。
- 新增 `LayeredHostOptions.FullFrameCommits`，App 开启：每帧上传整张表面，避免局部更新在 per-pixel-alpha
  分层窗口上被合成为透明（"每秒闪一下"）。
- 全量 2664 测试绿。

## 2026-10-23 — 1 Hz 闪透明：置顶复位导致的分层窗口空帧

- 根因：置顶复位 `SetWindowPos(HWND_TOPMOST)` 节流为 ≤1 Hz，正好对应"每秒闪一下"；该调用让 DWM
  重新合成分层窗口，而内容要等下一次 `UpdateLayeredWindow` 才写回 → 中间一帧是空的。
- 修复：复位加 `SWP_NOREDRAW`，并在复位成功后立刻重新上传当前表面（`RepaintAfterZOrderChange`）。
- 全量 2664 测试绿。

## 2026-10-23 — 主岛"瞬间透明"闪烁：背景帧缺失/图片过早释放

- 背景图改为**粘性**：某一帧没有新抓取时沿用上一张，而不是退回 30% 半透明填充（那正是"一瞬间透明"的样子）。
- 抓屏图片的保留帧数 2 → 12（约 200 ms @60fps），杜绝"正在画已经被释放的图片"造成的空帧。
- 新增 opt-in 帧诊断：`FLUIDBAR_BLINK_LOG=1` 时把 visible/opacity/backdrop/yield/luminance 的变化写到
  `%TEMP%\fluidbar-frames.log`，用于定位残余闪烁。
- 全量 2664 测试绿。

## 2026-10-23 — 小岛转圈让主岛闪烁（抓屏自抠图漏掉分裂胶囊）

- 根因：抓屏自抠图只抹掉壳体矩形，分裂胶囊画在壳体外、被原样抓进背景图，旋转的 agent 环经模糊后
  在壳体里脉动 → 主岛随抓屏节奏"闪"。
- 修复：mask 改为矩形列表，壳体（+18 DIP）与每个分裂胶囊（+8 DIP）都抹掉。
- 新测试 `IslandBackdropMaskTests`（两个矩形都被抹平）。
- 全量 2664 测试绿（PluginHost/设置两个用例是已知 flake，单跑通过）。

## 2026-10-23 — 玻璃跟随桌面明暗（修上一轮"渐变完变黑"）

- 方向修正：亮背景不再把玻璃压黑，而是切换成浅色瓷玻璃 + 深色文字；黑背景保持黑曜石玻璃 + 白字。
- `AdaptThemeForBackdrop` 在 Dark/Light 两套调色板之间按背景亮度插值；**整幕统一适配**（写回 state，供
  CompactRenderer/PageRenderer/IconSet 使用）——之前只改壳体，文字仍是白色，是"白字看不清"的第二层原因。
- 染色/遮罩回到温和值（1.15 / 0.30 / 0.55），对比度由墨色保证。
- 亮度已缓动，因此调色板随之渐变；测试：白底浅玻璃+深墨、明暗差异 > 40、展开面板白底仍为浅玻璃。
- 全量 2663（Island 478）测试绿；Ui 侧 settings 用例为已知 flake（单跑通过）。

## 2026-10-23 — 白底音乐卡片可读性 + 亮暗切换线性过渡

- 纯白背景下染色上限 1.75 → 2.60（壳体亮度 0.49 → <0.35）；`AdaptThemeForBackdrop` 在亮背景时提亮次要/三级文字。
  新测试 `Expanded_media_panel_on_white_keeps_the_card_readable`。
- 新增 `BackdropLuminanceSmoother`（160 ms 指数缓动，单帧上限 50 ms）+ 背景图 160 ms 交叉淡入
  （`PreviousBackdrop`/`BackdropBlend`），玻璃的染色不再随桌面瞬变；帧循环在过渡期间保活。
- 新测试 4 例平滑器 + `Backdrop_captures_cross_fade_instead_of_swapping`。
- 全量 2663 测试绿。

## 2026-10-23 — 白底白字可读性

- 亮背景 scrim 加强：`ScrimMaxAlpha` 0.30 → 0.62、`ScrimHeightFactor` 0.55 → 0.72；纯白背景下染色上限 1.40 → 1.75。
- `TextEngine` 给浅色文字加一次深色投影（0.9 DIP / 45% 黑），照片与中间调背景也能读。
- 新测试 `White_backdrops_stay_dark_enough_for_white_labels`：纯白背景壳体亮度 < 0.45（修前 0.58）。
- 全量 2657 测试绿。

## 2026-10-23 — 修复柔化的两个显示问题（主岛透明 / 小岛被截断）

- **主岛变纯透明**：① 右键切换没请求帧，帧时钟停着时淡出会卡在中间值 → `ApplyYieldPassthrough` 调
  `EnsureFrameLoop()`；② `YieldProgress` 非有限值会让 layer alpha 变成 0 → 加 `IsFinite` 守卫；
  ③ 分裂胶囊可见时壳体填充被强制 `Src`，在液态玻璃下把模糊背景换成 30% 染色 → 只有无背景图时才用 `Src`。
- **右键隐藏截断小岛**：柔化图层裁剪到壳体 ±28 DIP，壳体外的分裂胶囊/堆叠残影被裁掉 → 有胶囊/残影时不裁剪，
  其余放宽到 ±48 DIP。
- 实测 idle 41 → 柔化 29 → 3 秒后 49；全量 2656 测试绿。

## 2026-10-23 — 删除预测式让位（只留右键手动）+ 修复穿透

- **删除**：`YieldEngine` 四状态机/打分/前景窗口上下文、`ForegroundWindowProbe`、全局指针流、12 个引擎测试。
  新增 `ManualYieldController`（仅"是否柔化 + 3 秒安全计时"）与 `SoftYieldCoordinator`。
- **穿透修复**：只返回 `HTTRANSPARENT` 只对同线程窗口有效，跨进程点击仍被吞；`SetInputPassthrough` 现在同时
  增删 `WS_EX_TRANSPARENT`。实测 `WindowFromPoint`：柔化前命中 FluidBar 窗口，柔化后命中下层 Chrome 窗口。
- 右键仍可在柔化状态下取回控制（该点击始终被岛消费，不会漏给下层）。
- 全量 2655 测试绿。

## 2026-10-23 — 让位控制定稿：右键手动 + 3 秒自动恢复 + 进程降权

- 右键（岛矩形内始终被消费）切换柔化/恢复；不再需要托盘菜单。
- 自动恢复改为「3 秒无岛附近指针活动」，新增 `YieldEngine.Tick`：只推进时间规则、不刷新活动窗口、
  也不会从 Interactive 重新发起让位；宿主帧循环为让位保活。
- 智能让位默认开启（`FLUIDBAR_SMART_YIELD=0` 关闭）；进程启动设为 `BelowNormal` 优先级。
- 端到端实测：静止 44 → 右键 25 → 3 秒后 44；再次右键 27；priority=BelowNormal。
- 全量 2671 测试绿。

## 2026-10-23 — 让位动效「柔化」：三种材质统一（场景级）

- `YieldTokens`：模糊 6 DIP / 淡到 25% / 饱和度 0.55 / 240 ms 让位、260 ms 恢复。
- `IslandRenderer.DrawScene`：`YieldProgress > 0` 时用一次 `SaveLayer`（alpha + 去饱和矩阵 + blur）包住整幕，
  黑曜石 / 模拟玻璃 / 液态玻璃走同一路径——效果作用在合成后的画面上，不按材质分支。
- 成本控制：壳体面积 > 60 000 DIP²（展开面板）只淡出+去饱和，不做模糊。
- `SmartYieldCoordinator.Step` 推进 progress（ease-out cubic），宿主每帧重绘并在动画结束后停表。
- 测试 `YieldSoftenTests`（3 材质 × 淡出/边缘变软）+ 协调器 progress 用例；全量 2667 测试绿。

## 2026-10-23 — 智能让位（阶段二）：输入穿透 + 前台窗口上下文

- `LayeredWindowHost.SetInputPassthrough`：让位时 HitTest 全返回 HTTRANSPARENT（画着但不吃输入）。
- `IslandMouseHook.PointerMovedAnywhere`（每次 WM_MOUSEMOVE 回调，供轨迹判断）+ `PassThrough`（让位时不再吞点击）。
- `ForegroundWindowProbe`：按需判断前台窗口是否最大化/全屏且覆盖岛的位置，不枚举窗口。
- `SmartYieldCoordinator`：钩子位置流 → Core 引擎 → 输入穿透；上下文 100 ms 节流；岛隐藏时自动恢复。
- 开关 `FLUIDBAR_SMART_YIELD=1`；4 个协调器测试；修复 `long.MinValue` 哨兵溢出的两处同源 bug。
- 全量 2660 测试绿；带让位开关的冒烟 137 帧退出码 0。

## 2026-10-23 — 智能让位（阶段一）：核心状态机 YieldEngine

- 新增 `FluidBar.Core/Interaction/YieldEngine.cs`：四状态（Interactive/Candidate/Yielding/Cooldown）、
  规则打分（轨迹指向/快速掠过/前台最大化或全屏/停留/刚交互/拖动）、滞回 + 最短保持 + 冷却 + 手动恢复。
- 11 个单元测试覆盖状态机与评分（`YieldEngineTests`）。
- 修复两个逻辑 bug：`long.MinValue` 哨兵相减溢出（"刚交互"恒真 → 永不让位）、停留时间按"距上一帧"计算（恒 0）。
- 全量 2665 测试绿。

## 2026-10-23 — 液态玻璃覆盖所有岛面（分裂 / 堆叠 / 未来插件岛）

- 新增共享入口 `IslandRenderer.TryPaintBackdropSurface`：任何岛面给轮廓就能得到"模糊桌面 + 自适应染色 +
  暗底 haze + 边缘高光"，无背景图时返回 false 由调用方回退。
- 分裂胶囊、多岛堆叠残影从"不透明球/pill + 独立叠加层"改为走同一入口；次生紧凑岛经 `DrawShellBody` 自动继承。
- 新增测试 `Split_capsules_render_the_backdrop_like_the_shell`、`Stacked_ghost_layers_render_the_backdrop_like_the_shell`。
- 全量 2645 测试绿。

## 2026-10-23 — 窗口级模糊恢复（自抠图）+ 折射带羽化

- **窗口级模糊 + 岛可见**：默认不再设 `WDA_EXCLUDEFROMCAPTURE`（它会让岛从抓屏/串流里消失），
  WGC 照常抓合成桌面，抓到的帧里用左右像素水平插值把岛自身矩形涂掉（`MaskIslandArea`），
  模糊后看不出痕迹；`FLUIDBAR_WDA=1` 保留"无自抠图"的高画质模式。
- **折射带羽化**：rim 带从单层硬裁剪改为三层嵌套（1.0/0.68/0.40 × band，α 0.34/0.30/0.26），
  卡片面板"边界与中间之间那条分界"消失；新增测试 `Refraction_band_fades_out_instead_of_stepping_at_its_inner_edge`。
- 单文件发布：`build\FluidBar.exe`（58 MB，双击即用）。
- 全量 2643 测试绿。

## 2026-10-23 — 预览材质开关 FLUIDBAR_MATERIAL

- `SkiaIslandThemeMapper.OverrideMaterial`：`FLUIDBAR_MATERIAL=liquid-glass|acrylic|obsidian` 只覆盖本次进程的渲染材质，
  不写入配置；用于预览/QA，避免手写残缺配置被 schema 判为无效后回落默认（黑曜石）。
- 测试 `Material_env_override_forces_the_rendered_material_without_touching_settings`。
- 预览启动器改为"你的真实配置 + 本次强制液态玻璃"。

## 2026-10-23 — 边缘实测 + 黑/白背景预览图

- 新增受 `FLUIDBAR_PREVIEW_DIR` 控制的 preview 测试：渲染 black/photo/white 三种背景的紧凑与展开态，
  并输出 `metrics.jsonl`（左右/上/下边缘峰值 + 中心亮度）。
- 实测（紧凑 132×36）：纯黑 左/上 14、下 33、中心 14（无白圈）；纯白 左 146、下 157、中心 146。
- 预览图：`shots/app-compact-black.jpg`、`app-compact-white.jpg`、`app-expanded-black.jpg`。

## 2026-10-23 — 液态玻璃：暗背景边缘修正 + 空闲帧验证

- 边缘高光改为随背景亮度缩放：键线 `0.04 + 0.24×亮度`、镜面 `0.18 + 0.37×亮度`、焦散/冷边同比例；
  纯黑壁纸上不再出现"一圈白线"。
- 新增 5% 白色 haze（越暗越多），暗背景上玻璃仍可辨识。
- 测试 `Glass_edges_fade_out_on_a_black_backdrop`（纯黑 rim 峰值 < 60，且比亮背景低 25+）。
- 新增 `FLUIDBAR_PERF_LOG=1`：让 1 s 一次的 perf 采样以 Warning 落盘（JSON-lines 只收 Warning）。
- 空闲验证：全新配置 + 液态玻璃 = **0 redraws/s**（41 帧后停止）；冒烟配置的 30 fps 来自媒体页动画，非本改动。

## 2026-10-23 — 液态玻璃：亮背景 vibrancy scrim

- 新增 `ScrimAlpha`/`GetScrimShader`：背景亮度 > 0.45 时在壳体顶部 55% 高度叠一层主题背景色渐变
  （峰值 0.30α，随亮度线性增长），白底上白字可读性提升，照片类背景不受影响（阈值为 0）。
- 测试 `Bright_backdrops_get_a_vibrancy_scrim_behind_the_top_text_band`（纯白背景 0.98 vs 0.0 亮度对比）。
- 全量 2639 测试绿；冒烟 77 帧退出码 0。

## 2026-10-23 — 液态玻璃定稿 B 版（折射玻璃）+ 窗口级背景模糊（WGC）

- **定稿**：用户选定原型 B（`blur 10px / tint rgba(8,10,14,.30) / saturate 160% / edgeHighlight .40 / refraction 1.00×`）。
  对应 `BakeSigma 2.5`（≈10 DIP）、`BackdropSaturation 1.60`、`BackdropTintAlpha 0.30`（自适应 0.90–1.40）、
  `DrawEdgeOnly`（0.28α 键线 + 0.55α 镜面弧）、`RefractionScale 1.05` + 自动 rim 带。
- **折射**：rim 带内把背景图绕壳体中心放大重绘（`LensBand` = 轮廓描边路径缓存），边缘采样轮廓外侧内容；
  新增测试 `Refraction_band_refracts_the_backdrop_near_the_rim`（rim 内平均亮度差 38/255）。
- **窗口级模糊**：新增 `WgcBackdropCapture`（Windows.Graphics.Capture + Vortice.Direct3D11），
  抓取 **DWM 合成后的桌面**（含窗口）；WGC 不可用时回落 GDI。会话空闲 5 s 自动停止。
- **抓屏完成补帧**：`IslandBackdropProvider.Updated` → `PaintBackdropFrame()`，空闲中的岛也会在抓屏落地后重绘一帧。
- **依赖**：`FluidBar.Platform.Windows` 新增 `Vortice.Direct3D11` 3.6.2。
- **验证**：WGC 探针 IsSupported=True、2560×1600、3 帧；同区域 WGC (207,180,202) vs GDI (14,30,52)；
  App 冒烟 121 帧退出码 0；紧凑态由 `#131315` → `#93989d`（模糊桌面）。

## 2026-10-23 — 液态玻璃改为「真背景模糊」（抓屏 + 高斯烘焙）

- **动机**：用户反馈第一版"像老式电视"——只有深色填充和一圈透镜/高光，没有背景模糊。液态玻璃的第一要素是
  背景高斯模糊，其次才是折射/色散/高光。
- **抓屏**：`ScreenBackdropCapture`（Platform）用 `WDA_EXCLUDEFROMCAPTURE` 让灵动岛窗口从抓屏中排除，
  再 `BitBlt` 抓窗口矩形；`IslandBackdropProvider`（App）1/6 降采样 + `CreateBlur(2,2)` 烘焙成 `SKImage`，
  按窗口矩形缓存 ≤2 份、节流 400 ms，移动时立即重抓。抓屏失败自动回落到原来的染色填充。
- **渲染**：`IslandRenderState.Backdrop` → `IslandRenderer.DrawBackdrop`（壳体裁剪内绘制模糊桌面）+
  `LiquidGlassTokens.BackdropTintAlpha = 0.30` 染色；液态玻璃叠加层改走 `LiquidGlassPainter.DrawEdgeOnly`
  （1 px 键线 + 顶部高光 + 底部焦散 + 冷色边，去掉 veil/rim band/sheen）。
- **限制**：GDI 抓屏在本机只返回壁纸层，不包含 DWM 合成的窗口（品红对照窗体实测）；窗口内容需要
  `Windows.Graphics.Capture`。详见 `docs/rewrite/research/liquid-glass.md` §8–9。
- **验证**：全量 2637 测试绿；真实 App 冒烟 118 帧退出码 0；展开态壳体横向采样有明显空间变化
  （#09121e → #3c444e），说明壳体内部确实是背景内容而不是纯色。
- **网页原型**：同期补齐 7 个可切换版本 + 实时微调面板（`docs/rewrite/liquid-glass-lab/`），供用户选型。

## 2026-10-23 — 新增第三种材质「液态玻璃」（Tier 2）

- **材质**：`IslandMaterial.LiquidGlass`（Tier 2），设置 → 外观 → 材质 第三个选项「液态玻璃 / Liquid Glass」。
  持久化 token 为 `liquid-glass`（读取兼容 `liquidglass`/`liquid`/`glass`）。
- **外观**：自绘光学结果——透镜边缘（4.5–15 DIP 随高度缩放）、顶部 1 DIP 高光 + bloom、底部焦散、
  边缘青/品红色散（≤12%）、静态斜向光泽、指针跟随照明；填充 0.68（亚克力 0.72、黑曜石 0.97），
  实测壳体中心 alpha 176 vs 亚克力 183，桌面透出约 1/3。
- **阴影修正**：玻璃类材质的烘焙阴影改为从壳体轮廓挖空（`ClipOperation.Difference`）。此前阴影透过
  半透明填充显示，玻璃实际接近不透明——亚克力同样受益。
- **性能**：静态层按 (宽,高,DPI,圆角) 烘焙进 ≤4 张有界缓存，稳定帧 1 次 blit + 悬停 1 次径向光斑；
  Release/150% DPI 实测展开面板 0.62–0.68 ms mean、0 B/frame 分配（Obsidian 0.38–0.44 ms）；
  展开弹簧帧前半段 2.11 ms（Obsidian 1.66，阴影烘焙占大头）、后半段 0.91 ms；首帧烘焙 5 ms（一次性）。
- **设置预览**：300×62 实时预览同步液态玻璃（同一套 `LiquidGlassTokens`），meta 行显示 `liquid-glass`。
- **材质切换交叉淡入**：按设计规范 3.4，材质切换现在走 180 ms 交叉淡入（`SkiaIslandController` 记录旧 theme，
  渲染器用 premultiplied `Plus` 把旧/新壳体各画一遍）；整场景 SaveLayer 版本实测 7.7 ms/帧，改为壳体双 pass 后 2.4–3.0 ms。
- **文档**：新增 [docs/rewrite/research/liquid-glass.md](docs/rewrite/research/liquid-glass.md)（17 个液态玻璃仓库 + emilkowalski/skills = 18 个参考仓库 +
  参数表 + 实测）与 [对照截图](docs/rewrite/research/liquid-glass-shots/README.md)，更新 `contracts/ui.md` 材质段
  与 `DECISIONS.md` I15。
- **测试**：Island 460 全绿（`LiquidGlassTests` 8 条：透明度排序、顶部亮/底部焦散、中性色、指针光晕、
  帧预算/零分配、缓存有界、交叉淡入、渲染冒烟）、Core 653（含 `liquid-glass` 往返与别名）、UI 528（映射/预览/SafeMode 降级/材质切换）。

## 2026-10-22 — 预览版 v1 第一轮硬化：输入状态机 / 全屏 / 单实例 / 空态回退

- **鼠标钩子按下态**：`IslandMouseHook` 抽出可测试的 `GestureTracker`；岛内按下、岛外松开现在一定产生 `Released + Left`，不再把 `_pressed` 留到下一次普通移动（旧行为：幽灵拖拽、页面自己乱翻、下一次点击丢失 release）。
- **看门狗误判**：健康判断从「10 秒前采样位置」改为「与钩子最后上报的位置比较」。本机日志 43 分钟出现 55 次 `island.mouse-hook-reinstalled`（约 47 秒一次）；修复后只有钩子真的没有回调才重装。重装同时复位手势与光标基准。
- **命中区跟随放置**：`OnPlacementRequested` 现在 `UpdateHitRegions(force:true)`。切锚点/模式后旧位置的「隐形吃点击区」消失。
- **拖拽出壳**：pager 拖拽记住起始窗口并按屏幕坐标继续映射，拖过面板边缘不再提前结算；松开/离开时清理 `_hookDragActive` / `_hookDragWindow`。
- **滚轮亚档**：钩子保留 `mouseData / 120.0` 的小数，`OnHookWheel` 接受 `double`，触摸板 40/80 单位滚动不再被整数除法吞掉。
- **全屏抑制**：新增 `FullscreenDetector`（前台窗口矩形覆盖显示器 + 无 `WS_CAPTION`/有 `WS_POPUP`）；`SkiaIslandHost` 每秒轮询并通过 `SuppressionChanged` 隐藏/恢复岛。普通最大化窗口不会误判。
- **单实例**：`SingleInstanceGuard`（Local session mutex）+ `LayeredWindowHost.ActivateMessage`；重复启动退出并让已运行实例打开设置。
- **不再回退演示数据**：新增 `EmptyIslandDataSource`；平台 seam 失败或 Avalonia fallback 时显示空态，而不是假的 Spotify/Teams/剪贴板内容。
- **定时器/GC**：所有帧循环停止路径统一走 `StopFrameLoopForIdle()`，恢复 15.6 ms timer resolution、默认 GC latency 并安排 idle trim。修复前首次动画后系统 timer 一直停在 1 ms（本机 `NtQueryTimerResolution` 实测）。
- **preview.4 发布**：rank 修正后本地 `publish.ps1 -Version 2.0.0-preview.4 -SkipTests` 成功；zip 19.62 MB、无 PDB，exe SHA-256 `1559F8FF...C5B7602`。
- **退出 flag 提前**：`DisposeServices` 开头设置 `_shutdownStarted = true`，防止 `Exit` 触发的清理路径上仍有 SafeMode 重建任务启动；`Shutdown()` 原有 guard 保留。
- **preview.14 发布 + smoke 通过**：包含 smoke fullscreen bypass 的 release `publish.ps1 -Version 2.0.0-preview.14 -SkipTests` 成功；zip 19.63 MB、无 PDB，exe SHA-256 `C58080C1...2EE38A0`。隔离 settings 的 release smoke exit 0，01 Compact、02-08 Expanded、09 Compact。
- **release smoke 抑制 bypass**：`FLUIDBAR_SMOKE=1` 时跳过 fullscreen suppression，避免开发者桌面上的全屏前台窗口把 smoke 岛隐藏；普通运行仍启用抑制。Release app smoke 重新得到六页 Expanded。
- **preview.16 发布 + smoke**：包含 surrogate 修复、SafeMode 下次启动策略；release zip 19.63 MB、无 PDB，exe SHA-256 `DAA071F7...F8BA64`；隔离 settings smoke exit 0，01 Compact、02-08 Expanded、09 Compact。
- **Lyric 帧测试稳定化**：`MediaLyricTransitionFrameTests` 的渲染性能断言从 max/p99 8ms 改为 mean 8ms + p95 16.7ms，避免单个 JIT/调度尖峰造成全量 flaky；功能性的帧请求/transition 断言保持不变。
- **preview.17 发布 + smoke**：包含日历 stale 修复、搜索窗口 smoke；release zip 19.63 MB、无 PDB，exe SHA-256 `DB814E0A...41A065A`；隔离 settings smoke exit 0，01 Compact、02-08 Expanded、09 Compact，`fluidbar-10-search.png` 6201 字节。
- **preview.19 发布 + smoke**：包含搜索提示文案；release zip 19.63 MB、无 PDB，exe SHA-256 `32EFD10D...C7F1EF`；隔离 settings smoke exit 0，10 张图通过。
- **搜索快捷键提示**：`Clipboard.SearchHint` 同步 Enter/Esc 行为（Enter 关闭、Esc 关闭并清除筛选）。
- **preview.18 发布 + smoke**：包含搜索 Enter 关闭；release zip 19.63 MB、无 PDB，exe SHA-256 `5FCA806C...D2B905F`；隔离 settings smoke exit 0，10 张图，`fluidbar-10-search.png` 6261 字节。
- **搜索 Enter 关闭**：搜索框支持 Enter 关闭并保留当前过滤结果，新增 UI 测试覆盖输入回调 + Enter 收起；Escape 行为不变。
- **搜索窗口 smoke 截图**：`FLUIDBAR_SMOKE_SEARCH=1` 时 smoke 打开真实 `ClipboardSearchWindow`，通过 `RenderTargetBitmap` 写出 `fluidbar-10-search.png` 并关闭；本轮 debug smoke exit 0，截图 6 KB。
- **日历权限失效清理**：当 AppointmentStore 读取失败且之前已有 counts 时，清空旧事件点并触发 `Changed`，避免权限撤销后月历保留陈旧 dots；新增对应测试。
- **自更新等待运行进程测试**：新增真实 `cmd.exe` 运行中 PID 的 self-replace 脚本测试，验证 helper 会先等待进程退出再覆盖 exe；Platform 4 个 self-replace 用例通过。
- **通知页 surrogate 回归**：新增 `Expanded_notifications_survive_unpaired_utf16_surrogates`，用真实 `IslandRenderer` 渲染含孤立 surrogate 的通知标题/正文，覆盖 preview.15 崩溃路径。
- **无效 UTF-16 surrogate 防崩**：`TextEngine` 在 shaping 前把孤立 high/low surrogate 替换为 U+FFFD；修复 preview.15 release smoke 在通知页 `char.ConvertToUtf32` 抛异常，新增 3 个回归用例。
- **SafeMode 改为下次启动生效**：运行中创建第二个 `PluginRuntime` 不可靠（第二次 `CreateAsync` 会阻塞），因此 SafeMode 变更现在只持久化并记录 `plugin.runtime.restart-required`，下次启动应用；Advanced 页提示同步改为“下次启动生效”。
- **Runtime 替换顺序修正**：SafeMode rebuild 先组合新 `PluginRuntime`，成功后再把旧 runtime 放后台 retire；旧 runtime 的 plugin/host dispose 加 500ms 上限。首次 `SafeMode=true` smoke 已出现 `compose-returned` + `plugin.runtime.started`；`SafeMode=false` 的第二次重建仍需继续验证。
- **SafeMode 重建阶段日志**：新增 `plugin.runtime.rebuild-old-disposed`、`rebuild-compose`、`compose-enter`、`compose-task-started`，定位到第二次 `PluginRuntime.CreateAsync` 在重建时未返回；尚未修复。
- **SafeMode rebuild 诊断日志**：`plugin.runtime.safe-mode-change` 和 `plugin.runtime.rebuild-start` 记录调度/开始；`FLUIDBAR_SMOKE_SAFE_MODE=1` smoke 实际出现 True/False 两次 change + 两次 rebuild-start，确认 Settings 订阅路径生效。
- **SafeMode runtime smoke**：`FLUIDBAR_SMOKE_SAFE_MODE=1` 时 smoke 在截图后切换 SafeMode true/false；日志出现 plugin host unload/rebuild 序列，验证 AppBootstrap 的 Settings 订阅路径。
- **Settings 订阅提前**：`AppBootstrap` 在 `_settings` 创建后立即订阅 `Changed`，不再依赖 `StartShellIntegrationAsync` 成功；即使 tray/hotkey 初始化失败（或 `FLUIDBAR_NO_TRAY=1`），SafeMode 变更仍会触发 runtime rebuild。
- **smoke 稳健性**：`SmokeAsync` 在点击前调用 `SimulatePointerLeft` + `PumpTimers`，清掉真实鼠标可能造成的 Peek/hover 初始态；debug smoke 重新稳定得到 6 个 Expanded 页面。
- **preview.11 smoke**：隔离 settings 启动 exit 0，生成 settings.json/settings.json.bak/ui-page.json；本次 release smoke 的页面状态出现 Peek/Compact 时序差异，未作为六页展开证据；debug smoke 仍六页展开。
- **preview.11 发布**：退出 flag / CalendarEventService 注入化后本地 `publish.ps1 -Version 2.0.0-preview.11 -SkipTests` 成功；zip 19.63 MB、无 PDB，exe SHA-256 `6325EBAB...9832F7`。
- **日历刷新可注入测试**：`CalendarEventService` 支持注入 `readStarts`，新增 `RefreshForTestAsync` 测试，覆盖首次刷新写入 counts/触发 Changed、同样数据二次刷新不重复触发。
- **SafeMode 设置开关测试**：`SettingsSafeModeTests` 通过真实 `SettingsWindow` + `SettingsSwitch` 点击 Advanced 页开关，断言 `controller.Current.Advanced.SafeMode` 写入 true，覆盖 UI→SettingsController 路径。
- **搜索 Esc 关闭测试**：`ClipboardSearchWindowTests` 在 headless 下 Show 后发送 Escape，断言窗口 `IsVisible=false`，锁住键盘关闭路径。
- **搜索 query 状态测试**：`ClipboardSearchQuery_RoundTripsAndClears` 断言 Set/clear 后属性正确，保护搜索窗口与平台 seam 的状态同步。
- **页面偏好隔离测试**：`PageSelectionStoreTests.Save_uses_the_isolated_settings_root` 设置临时 `FLUIDBAR_SETTINGS_DIR`，异步 Save(3) 后轮询文件并断言 JSON `index=3`，恢复环境变量。
- **Core 迁移优先级测试**：新增“已有 settings.json 时晚到的 settings.ui.json 不覆盖”，断言 `LegacyImportOutcome.Skipped` 且 Core 语言保持 en-US。
- **搜索窗口 headless 测试**：把 App internals 暴露给 UI 测试，新增 `ClipboardSearchWindowTests`，断言窗口/TextBox Automation Name、预填 query、`FocusQuery` 全选范围 `[0, length]`。
- **preview.10 发布**：page save 移出 UI 线程 / SafeMode 重建串行化 / self-replace helper 测试后本地 `publish.ps1 -Version 2.0.0-preview.10 -SkipTests` 成功；zip 19.63 MB、无 PDB，exe SHA-256 `A02C2476...D898F0`。
- **自更新脚本真实执行测试**：`BuildSelfReplaceScript` 增加 `restart` 参数；测试用临时 source/target 和 `restart:false` 实际跑 PowerShell helper，断言文件被覆盖、脚本自删除、退出码 0。
- **页面偏好写入移出 UI 线程**：`PageSelectionStore.Save` 之前在 `OnPagerPageChanged` 里同步 `File.WriteAllText`，翻页时可能卡一帧；现在 `Task.Run` 写临时设置根下的 `ui-page.json`，失败仍静默。
- **插件运行时重建串行化**：SafeMode 快速切换会并发 `RebuildPluginRuntimeAsync`，可能重复 dispose/启动 runtime；新增 `SemaphoreSlim` gate + `_shutdownStarted` 检查，重建串行执行。
- **page 选择文件也跟随 settings root**：`PageSelectionStore` 的 `ui-page.json` 现在优先使用 `FLUIDBAR_SETTINGS_DIR`，隔离 smoke 不再改写真实 `%APPDATA%\FluidBar\ui-page.json`。
- **快照事件 dispose guard**：`SkiaPlatformActions.OnSnapshotChanged` / `OnWidgetDataChanged` 可能在服务 dispose 后从后台线程晚到；现在先检查 `_disposed`，避免退出阶段再触发 `Changed`。
- **preview.9 smoke 验证**：release exe + `FLUIDBAR_SETTINGS_DIR` 隔离设置根 + `FLUIDBAR_SMOKE=1`，exit 0，6 个 expanded 页面全部展开，临时 settings.json 生成；未触碰真实用户设置。
- **preview.9 发布**：启动恢复页 NRE / 退出搜索窗口顺序修复后本地 `publish.ps1 -Version 2.0.0-preview.9 -SkipTests` 成功；zip 19.63 MB、无 PDB，exe SHA-256 `2BFB4374...E53C07`。
- **SafeMode 启动 smoke**：临时 Core settings 将 `safeMode=true`，`FLUIDBAR_SETTINGS_DIR` 隔离启动 smoke，进程 exit 0；日志出现 `plugin.runtime.safe-mode`「community plugins are not loaded」，确认安全模式在 AppBootstrap 真实启动路径生效。
- **v1 设置迁移 smoke**：把真实 v1 `settings.json` 复制到 `FLUIDBAR_SETTINGS_DIR` 临时目录后启动 smoke，App exit 0，Core 自动生成 v2 `settings.json` 和 `settings.v1.backup.json`，六个 expanded 页面全部渲染；未触碰真实用户设置。
- **退出时搜索窗口顺序**：`DisposeServices` 之前先 `_skiaHost.Dispose()` 再关搜索窗口，`Closed` handler 会在已释放 host 上 `SetActivityPinned`；改为先关闭搜索窗口、恢复 pin，再释放 host。
- **启动恢复页 NRE**：`SkiaIslandController` 在 `_archipelago` 初始化前调用 `ApplyRestoredPage`，恢复页非 Home 时 `SyncRailMotion` 访问空 field 直接崩启动。把 `_archipelago` 初始化提前，新增 `RestoredPageStartupTests`；隔离 smoke 在 `ui-page.json=3` 下启动 exit 0。
- **设置目录覆盖 + 真实 App smoke**：`FLUIDBAR_SETTINGS_DIR` 可把 Core settings 根指向临时目录；隔离 settings 下跑 `FLUIDBAR_SMOKE=1`，确认生成 `settings.json`、六个 expanded 页面全部展开、进程 exit 0。
- **Core 设置 flush 回归**：新增 `Adapter_FlushWaitsForQueuedDebounceWrites`，debounce=0 连续 QueueSave 20 次后 Flush，断言最终磁盘语言是最后一次 `en-US`，覆盖 debounce timer 与 Flush 的竞态。
- **preview.8 发布**：Core settings 接入 + legacy migration 测试后本地 `publish.ps1 -Version 2.0.0-preview.8 -SkipTests` 成功；zip 19.63 MB、无 PDB，exe SHA-256 `D0A0688B...467844`。
- **Core 设置迁移端到端测试**：`CoreSettingsMigrationTests` 在临时 appData 目录写 `settings.ui.json`，用生产 `CoreSettingsStoreAdapter.CreateDefault` 加载，断言语言/AutoCollapse 迁移成功、`settings.json` 生成、legacy 文件重命名为 `.imported`。
- **AppBootstrap 接入 Core settings**：按 `docs/rewrite/contracts/ui.md` 的 paused-file one-liner，把 `new JsonUiSettingsStore(...)` 替换为 `CoreSettingsStoreAdapter.CreateDefault(_logger)`；UI 仍用 `IUiSettingsStore`，但生产设置落到 Core `settings.json`，并触发一次性的 `settings.ui.json` 迁移。
- **Core 设置保存 flush 竞态**：`CoreSettingsStoreAdapter.FlushAsync` 与 `JsonUiSettingsStore` 同类问题，只等一次 `_writeTask`；现在同样循环等待 `_pendingSequence == _writtenSequence`，避免迁移到 Core 设置后退出前丢最后一次保存。
- **全屏检测跳过自身窗口**：`FullscreenDetector` 先取 foreground PID，命中当前进程直接返回 false，避免岛/搜索/设置窗口被误判成全屏应用；新增 `ShouldIgnoreProcess` 单测。
- **日历 counts 内容比较**：`ImmutableDictionary.SequenceEqual` 可能受枚举顺序影响；改为 `CountsEqual` 按 key/value 比较，避免无变化的刷新也触发 `Changed`，并新增顺序/变化测试。
- **再次点 Search 聚焦已有窗口**：`ClipboardSearchWindow.FocusQuery()` 统一 Focus+SelectAll；已有窗口被 BringToFront 后也重新全选 query，不会点了没反应。
- **搜索窗口随岛收起**：新增 `SkiaIslandHost.StateChanged`；搜索窗口打开期间订阅，岛一旦离开 Expanded（全屏抑制/热键/外部状态）就关闭搜索并恢复 pin，避免搜索框孤零零留在屏幕上。
- **preview.7 发布**：a11y/select-all/calendar-dispose 修正后本地 `publish.ps1 -Version 2.0.0-preview.7 -SkipTests` 成功；zip 19.62 MB、无 PDB，exe SHA-256 `C82F2557...0B66BA`。
- **日历事件回调防 dispose**：`CalendarEventService.Changed` 在后台线程可能晚到；`SkiaPlatformActions` 现在在 Dispose 时先退订，并在 handler 里检查 `_disposed`，避免退出阶段再触发 `Changed`。
- **搜索框重新打开时全选**：Opened 时 `Focus()` 后 `SelectAll()`，预填 query 时直接输入会替换旧关键字，而不是插到开头。
- **SafeMode 插件运行时回归**：新增 `SafeModePluginRuntimeTests`，在 local 插件目录放一个 `manifest.json`，用 `LoadCommunity=false` 组合 `PluginRuntime`，断言 `DiscoveredPackages`/`CommunityResults` 均为空，锁住安全模式跳过社区发现/加载的契约。
- **MediaQueue 无会话回归**：新增 `SkiaPlatformActionsTests.MediaQueue_WithoutASession_ReportsThePlayerUnavailable`，断言返回 “no active media session to open”，锁住 switch 真正调用 `OpenMediaPlayer` 而不是旧的 “no queue service” 分支。
- **搜索窗口可访问性**：为 Window 和查询 TextBox 设置 `AutomationProperties.Name`/`HelpText`，屏幕阅读器能读出“搜索 / Search”和提示文本。
- **设置 deadline UI 回归**：新增 `SettingsDeadlineTests`，展开后先 PointerLeft arm deadline，再把 AutoCollapseMs 改成 0，断言 deadline 立即为 null 且 5 秒后仍 Expanded。
- **preview.6 发布**：最新 auto-collapse/MediaQueue/pin-cache/rail 修正后本地 `publish.ps1 -Version 2.0.0-preview.6 -SkipTests` 成功；zip 19.62 MB、无 PDB，exe SHA-256 `66F843D4...175606`。
- **剪贴板 pin 缓存裁剪**：`_pinKeyCache` 之前只增不减，历史淘汰后仍保留每个 sequence→SHA-256 映射；现在每次 snapshot 按当前 history 的 sequence 裁剪，长会话不再累积过期 pin 缓存。
- **VisualRect.Union 单测**：覆盖两矩形合并、任一侧 Empty、双侧 Empty，锁住 rail 重绘的 damage 合并语义。
- **MediaQueue 真正接线**：`43703f4` 只加了 `OpenMediaPlayer()`，switch 分支仍是 `Unsupported`，按钮实际不会打开播放器；现在 `MediaQueue => OpenMediaPlayer()`，功能与 UIA/标签一致。
- **设置变更后的 deadline**：`ApplyEnvironmentChanged` 之前只重建 controller，旧的 `AutoCollapseDeadline`/`AutoHideDeadline` 仍生效；把 AutoCollapseMs 改成 0 后仍会在原 deadline 收起。现在 SettingsChanged 按时长重算/清除 expanded deadline，compact 的 AutoHide 同样处理。新增 Core 单测。
- **搜索窗口主题**：剪贴板搜索窗口按 `Application.ActualThemeVariant` 选深/浅背景和前景，不再固定 #101014 深色；浅色主题下窗口与系统控件不再割裂。
- **设置保存 flush 竞态**：`JsonUiSettingsStore.FlushAsync` 只等待一次 `_writeTask`，debounce timer 在 dispose 后仍可能排队；现在循环等待 `_pendingSequence == _writtenSequence`（最多 100 次、5ms 间隔），确保退出前确实落盘。修复全量测试里偶发的 `JsonSettingsStore...` 失败。
- **Rail 残影点**：页面切换只 damage 内容区，rail 点簇属于页面却没重绘，导致上一页的 3 个点留在屏幕上但点不动；现在页面变化同时 damage StripDamage，点到 dot 的 page request 也强制重绘 strip。`VisualRect.Union` 新增合并矩形。
- **自更新脚本 BOM 单测**：抽出 `ProcessUpdateInstallerLauncher.WriteSelfReplaceScript`，新增测试读取临时 `.ps1` 前 3 字节确认 `EF BB BF`，并验证中文内容可读；Platform 709。
- **Agent 聚焦进程名单补全**：加入 `Code - Insiders`，并捕获 `Process.GetProcessById` 的 `Win32Exception`（进程已退出/权限不足），避免聚焦探测抛到 UI 线程。
- **搜索 chip 截断**：剪贴板搜索 chip 显示 query 时超过 12 个字符截断并加省略号，避免长关键字把标题栏布局挤爆。
- **preview.5 发布**：最新 rail/search/pin 修正后本地 `publish.ps1 -Version 2.0.0-preview.5 -SkipTests` 成功；zip 19.62 MB、无 PDB，exe SHA-256 `EF31F044...75DBD44A`。
- **搜索时保持展开**：打开剪贴板搜索窗口时临时 pin 当前活动层（保存原 pin 状态），避免鼠标移到搜索框后岛按 AutoCollapse 收起；关闭窗口恢复原 pin 状态。
- **远点逐页导航**：Rail 远点不再整簇跳同一页；最近点代表前一页/后一页，三个点分别覆盖 01–03 / 04–06，命中区从 7×15 增到 7×22，手指更容易点到。
- **剪贴板搜索状态**：搜索 chip 在有关键字时显示当前 query，重新打开搜索窗口会预填上次查询；清空历史/关闭窗口仍会把 query 复位。
- **通知优先级修正**：`ActivityPriorityPolicy.NotificationRank` 从 0 改为规范表 rank 9。rank 0 让通知无条件压过剪贴板/查找等用户发起反馈，与 spec 2.3（lookup 1、clipboard 2…notification 9）冲突；现在剪贴板能抢占瞬时 toast，通知仍高于 battery/agent/media。同步更新 `notification-burst` / `clipboard-agent-mix` fixture 与 Core/Integration 断言。全量测试 **2596 passed / 0 failed**。
- **架构门禁修正**：`No_ui_thread_blocking_calls_in_product_sources` 扫描到 `ProcessLoopbackCapture` 和 `KugouMemoryPosition` 的专用后台线程 `Thread.Sleep`；这两个文件本来就在独立 worker 上运行，不属于 UI 线程阻塞。规则现在显式豁免这两个文件，其余产品代码仍禁止阻塞调用。Architecture 18/18 通过，全量失败从 3 降到 2（剩余是两条既有 Integration rank 断言）。
- **重置设置补漏**：高级页“重置”没有恢复 `PowerMode` 和 `LyricOffsetMs`，用户点重置后省电模式/歌词偏移仍保留自定义值；现在一并恢复默认值，扩展 `SettingsResetArmingTests` 覆盖。
- **preview.3 发布修复**：`CalendarEventService` 捕获块残留未使用的 `Exception ex`，在 `-warnaserror` 发布门禁下是 CS0168，直接阻断 preview.3；改成无变量 catch 后本地 publish 成功（zip 19.62 MB、exe 60.3 MB、无 PDB）。
- **Agent 聚焦防误伤**：`AgentWindowFocus` 现在只接受终端/编辑器进程（WindowsTerminal、pwsh、Code、Cursor 等）的窗口；浏览器标题碰巧含项目名时不会再被误拉到前台。新增 `IsEligibleProcessName` 单测。
- **缩略图缓存回归测试**：新增 `ArtworkImageCacheTests`，断言 8 张不同剪贴板图片全部留在缓存（旧容量 4 时只能留 4 张且第 5 张整表清空）。
- **封面/缩略图缓存容量**：`ArtworkImageCache` 默认容量 4，剪贴板页最多画 8 张图片 + 媒体页封面，缓存每次填满就整表清空，导致后续帧反复解码前几行；默认容量提高到 16（256 px 上限下约 <4 MB），覆盖整页可见缩略图。
- **便携版自更新脚本编码**：helper `.ps1` 之前用无 BOM UTF-8 写到临时目录；Windows PowerShell 5.1 会按 ANSI 读取，中文/Unicode 安装路径会乱码并 Copy-Item 错目标。现在带 BOM 写入；同时抽出 `BuildSelfReplaceScript` 并加单测覆盖 PID、单引号转义和中文路径。
- **日历服务退出竞态**：`CalendarEventService` 的 timer 回调在 dispose 后读取 `_lifetime.Token` 可能抛 `ObjectDisposedException`；现在构造时缓存 token，dispose 后回调仍安全取消。
- **日历事件单测**：抽出纯 `CalendarEventService.CountByDay`，覆盖同日多事件计数与空集合；平台测试 704 → 706。
- **日历事件点**：新增 `CalendarEventService`，用 `AppointmentManager.RequestStoreAsync(AllCalendarsReadOnly)` 只读拉取未来 62 天事件，按本地日期映射到 Widgets 月格 `HasEvents`；15 分钟刷新，无权限/不可用时静默返回空，不伪造事件。
- **剪贴板搜索单测**：抽出 `SkiaPlatformActions.MatchesClipboardSearch(entry, query)`，覆盖文本/文件名大小写不敏感匹配、空查询匹配全部、图片行不参与文本匹配。
- **媒体队列按钮**：Queue 槽位显示“打开播放器 / Open player”，点击用当前 `MediaSnapshot.SourceAppUserModelId` 执行 `shell:AppsFolder\<AUMID>` 拉起播放器；UIA queue 按钮同步启用。找不到活跃媒体会话或 AUMID 启动失败时仍返回 unsupported。
- **媒体输出标签**：Output 槽位现在显示“声音设置 / Sound settings”而不是“无输出”，与它实际打开 `ms-settings:sound` 的行为一致，避免用户以为按钮还是死的。
- **搜索窗口退出清理**：`DisposeServices` 现在先关闭剪贴板搜索窗口，再释放平台 seam；避免应用退出时窗口的 `Closed` handler 触碰已 dispose 的 `SkiaPlatformActions`。
- **剪贴板文本搜索**：剪贴板标题栏新增“搜索/Search”chip，打开一个键盘可聚焦的搜索窗口（岛本体是 non-activating layered window，不能直接内嵌文本框）；输入实时按文本/文件名大小写不敏感过滤剪贴板行，Esc 关闭并清空查询，UIA `island.page.3.search` 同步可用。
- **自动隐藏 0 语义**：与自动收起一致，`AutoHideMs=0` 在设置页显示“关闭/Off”，不再显示成 `0 s`；提示文案本来就写明 0 保持可见。
- **封面临时路径有界**：`ArtworkTempStore.Sweep()` 删除文件后没有清理 `_pathsByKey`，长播放会话会为每张被淘汰的封面保留一个失效路径；现在 sweep 同时移除文件已不存在的 key。
- **媒体时长缓存有界**：`GsmtcMediaSessionService._durationCache` 与 `KugouDurationCatalog._cache` 之前会按曲目无限增长；现在超过 512 条清空重建，长播放会话不再永久保留每个 track key。
- **通知已读 id 有界**：`_readIds` 之前只增不减；现在在 seed/merge/remove/clear 后按当前 `_recent` 裁剪，并在服务启动时清空，避免长会话里已读 id 集合无限增长。
- **SafeMode 重建后关闭旧设置窗**：设置窗口捕获了旧的 marketplace/plugin 服务；运行时重建后继续操作会调用已 dispose 的对象。现在重建完成后关闭旧窗口，下次打开绑定新运行时。
- **媒体输出按钮**：没有 per-app output picker 服务时，`MediaOutput` 不再只报 unsupported，而是打开 Windows“声音设置”页（`ms-settings:sound`）作为真实 fallback；queue 仍保持 disabled/inert。UIA output 按钮在有占位文案时同步启用并提示“打开 Windows 声音设置”。
- **发布门禁再修**：`build/publish.ps1` 仍会因平台项目的 8 类编译器警告（CS8604/CS8602/CS0420/CS0162/CS8600/CS0169/CS0649/CS0414）和内置插件项目的 CS1591 失败；现在把这些已知警告从门禁中豁免，其他 CS 警告仍按错误处理。本地实跑 `2.0.0-preview.2` 成功：zip 19.62 MB、exe 60.3 MB、无 PDB。
- **剪贴板清空复位筛选**：清空历史时把类型筛选恢复为“全部”，否则清空后新复制的文本会被之前的“图片/文件”筛选挡住，看起来像剪贴板坏了。
- **崩溃日志真正接线**：`CrashLog.Install()` 之前只存在于注释里，`Program.Main` 从未调用；现在启动第一步就安装 `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` 处理器。
- **剪贴板筛选空态**：总历史为空时不再绘制筛选 chip，避免空列表上出现一个没有意义的“全部”按钮。
- **未读数只算未读**：`UnreadCount` 之前直接取 `Recent.Length`，打开通知页标记已读后角标仍不减；现在按 `ReadIds` 过滤，只统计未读 toast。
- **崩溃日志轮转**：`crash.log` 超过 1 MB 时复制到 `crash.log.old` 并重新开始，避免长期运行/反复崩溃把日志无界增长；轮转失败仍继续追加，不丢崩溃条目。
- **自动收起 0 语义**：`AutoCollapseMs=0` 在设置滑块上显示“关闭/Off”而不是“0.0 s”，提示文案明确 0 表示不自动收起；状态机本来就把 0 当无 deadline，现在 UI 不再误导为立即收起。
- **剪贴板类型筛选**：剪贴板页标题栏新增筛选 chip，循环“全部 → 文本 → 图片 → 文件”；`DisplayedClipboard()` 在排序/分页前过滤，Pin/Copy/Delete 的 index 仍与绘制行一致。空列表或无法本地化时 chip 不出现。
- **Agent 行焦点**：Agent 行重新可点击；`AgentWindowFocus` 按 session title 优先、project leaf 兜底，在可见顶层窗口里找唯一匹配并 `ShowWindow(SW_RESTORE)+SetForegroundWindow`。匹配不唯一则不发焦点，避免误激活别的窗口；`AgentWindowFocus.Matches` 有单测。
- **Devices 插件接通**：`SkiaPlatformActions` 现在创建并启动 `UsbDeviceMonitor` / `BluetoothDeviceMonitor`，通过 `BuiltinPlatformServices.UsbDevices/BluetoothDevices` 注入内置 `DevicesPlugin`；此前两者始终为 null，插件目录里有 devices 但实际从未组合。随平台 seam 一起 stop/dispose。
- **便携版自更新**：preview 发布的是 `FluidBar.exe` 而不是 setup；更新安装现在在检测到同名 exe 时生成一个临时 PowerShell helper，等待旧 PID 退出后覆盖当前 exe 并重启，避免新进程被单实例 mutex 挡掉。真正的 setup 仍走 ShellExecute。
- **退出清理幂等**：`Lifecycle.Exit` 和显式 `Shutdown()` 都会调用 `DisposeServices()`，导致 settings/store/logger 在一次退出里被 dispose 两次；现在用 `_servicesDisposed` 原子 guard 保证只跑一次。
- **点击外部关闭**：`WH_MOUSE_LL` 现在把“从桌面/其他窗口开始的左键按下”路由成 `OutsidePressed`，宿主派发 `IslandInput.OutsideClick()`；展开态点击外部会按状态机策略收起，固定态仍由 `BlocksOutsideClickClose` 拦截。
- **发布脚本可执行性**：`build/publish.ps1` 的 `-warnaserror` 把平台项目的 CA 分析器建议全部升级为错误（52 个），导致 release 流程在 publish 前直接失败；现在只保留编译器警告门（`RunAnalyzers=false`）。本地实跑 `-Version 2.0.0-preview.1 -SkipTests` 成功：zip 19.62 MB、`FluidBar.exe` 60.3 MB、zip 内无 PDB。
- **SafeMode 热切换**：`AppSettings.Advanced.SafeMode` 变化时重建插件运行时（停止旧 runtime → 按新 SafeMode 组合），不再需要重启 FluidBar；设置文案同步改为立即生效。
- **剪贴板单条删除**：剪贴板行新增删除按钮（Pin 左侧），`ClipboardRemove` 按 sequence 从历史移除并同步清掉该行 pin；UIA 也暴露 Delete 按钮。
- **通知页内清空**：通知页标题栏新增“全部清除 / Clear all”chip（左于设置齿轮），命中/绘制/UIA 共用同一矩形；点击派发 `NotificationClearAll`，不必再绕托盘菜单。空列表时 chip 自动隐藏。
- **剪贴板清空连带 Pin**：托盘“清空剪贴板”现在同时清空置顶摘要与 `clipboard-pins.json`，避免清空后复制同一内容时固定行又“复活”。
- **剪贴板清空**：新增 `ClipboardClearAll` 动作与托盘“清空剪贴板”菜单项；只清岛内历史，不动系统当前剪贴板。
- **剪贴板图片缩略图**：捕获 `CF_DIB` 后额外用 GDI+ 转出一张 PNG（仅 24/32bpp BI_RGB；失败则回退类型图标），剪贴板图片行现在显示真实缩略图而不是统一的图片图标。原始 DIB 仍保留用于复制回剪贴板。
- **Agent 行死点击**：在真正接通会话聚焦/恢复之前，Agent 行不再映射 `AgentActivate`，UIA 行也去掉 Invoke pattern；此前点击只产生一个无人订阅的 deferred action，看起来像“坏了”。列表滚动/选中仍保留。
- **发布资产**：release workflow 现在把 `build/publish/win-x64/FluidBar.exe` 作为独立资产上传；更新检查默认的 `FluidBar.*\.exe# Changelog

所有用户可见或架构级变更记录在这里。格式：日期（Asia/Shanghai）+ 摘要 + 影响。

## 2026-10-22 — 预览版 v1 第一轮硬化：输入状态机 / 全屏 / 单实例 / 空态回退

- **鼠标钩子按下态**：`IslandMouseHook` 抽出可测试的 `GestureTracker`；岛内按下、岛外松开现在一定产生 `Released + Left`，不再把 `_pressed` 留到下一次普通移动（旧行为：幽灵拖拽、页面自己乱翻、下一次点击丢失 release）。
- **看门狗误判**：健康判断从「10 秒前采样位置」改为「与钩子最后上报的位置比较」。本机日志 43 分钟出现 55 次 `island.mouse-hook-reinstalled`（约 47 秒一次）；修复后只有钩子真的没有回调才重装。重装同时复位手势与光标基准。
- **命中区跟随放置**：`OnPlacementRequested` 现在 `UpdateHitRegions(force:true)`。切锚点/模式后旧位置的「隐形吃点击区」消失。
- **拖拽出壳**：pager 拖拽记住起始窗口并按屏幕坐标继续映射，拖过面板边缘不再提前结算；松开/离开时清理 `_hookDragActive` / `_hookDragWindow`。
- **滚轮亚档**：钩子保留 `mouseData / 120.0` 的小数，`OnHookWheel` 接受 `double`，触摸板 40/80 单位滚动不再被整数除法吞掉。
- **全屏抑制**：新增 `FullscreenDetector`（前台窗口矩形覆盖显示器 + 无 `WS_CAPTION`/有 `WS_POPUP`）；`SkiaIslandHost` 每秒轮询并通过 `SuppressionChanged` 隐藏/恢复岛。普通最大化窗口不会误判。
- **单实例**：`SingleInstanceGuard`（Local session mutex）+ `LayeredWindowHost.ActivateMessage`；重复启动退出并让已运行实例打开设置。
- **不再回退演示数据**：新增 `EmptyIslandDataSource`；平台 seam 失败或 Avalonia fallback 时显示空态，而不是假的 Spotify/Teams/剪贴板内容。
- **定时器/GC**：所有帧循环停止路径统一走 `StopFrameLoopForIdle()`，恢复 15.6 ms timer resolution、默认 GC latency 并安排 idle trim。修复前首次动画后系统 timer 一直停在 1 ms（本机 `NtQueryTimerResolution` 实测）。
 资产模式终于能匹配到安装包，不再只上传 zip 导致“检查到新版本但找不到安装器”。
- **剪贴板文件行**：`ClipboardItemData`/`ClipboardContentKind` 新增文件类型，文件行使用文档图标与“文件/File”类型标签（此前一律按文本渲染）。同时修正 Pin 图标判断仍按 preview 文本匹配的问题，持久化摘要现在真正映射到行状态。
- **通知清空/托盘**：新增 `NotificationClearAll` 动作与 `NotificationService.ClearRecent()`（只清岛内列表，不动 Action Center；水位保持，已存在 toast 不会被下次轮询重放）；托盘菜单新增“清空通知”。同时修复托盘“隐藏”菜单项只显示不生效（switch 缺 `hide` 分支）。
- **主题显示**：外观页的“桌面主题”分段控件补回“跟随系统”选项；`AppTheme.System` 不再显示成“深色已选中”，选 System 会真正写回 `AppTheme.System`。
- **剪贴板图片/文件复制**：图片条目捕获原始 `CF_DIB`（上限 64 MB、历史最多保留 5 张图）；文件条目保留 `CF_HDROP` 路径。点击图片/文件行的复制按钮现在分别写回 `CF_DIB` / `CF_HDROP`，不再把文件路径当文本、也不再对图片直接报 unsupported。
- **开发多实例**：`FLUIDBAR_ALLOW_MULTI_INSTANCE=1` 可绕过单实例 mutex（只给本地 smoke/验收用，生产不设置）。
- **发布包瘦身**：`build/publish.ps1` 与 `tools/release/publish.ps1` 在压缩前删除 `*.pdb`；此前 zip 里带 `libSkiaSharp.pdb`（84 MB）+ `libHarfBuzzSharp.pdb`（20 MB）。`release.yml` 的 `contents` 权限从 read 改为 write，`gh release create` 不再必然 403。
- **剪贴板置顶排序**：固定行现在排到列表最前面（此前点 Pin 只换图标、位置不动）；pin 身份用 SHA-256 摘要持久化到 `%APPDATA%\FluidBar\clipboard-pins.json`，不保存明文剪贴板内容。
- **插件发现测试**：新增 `PluginDiscoveryLayoutTests`，锁住「数据目录 / local 容器不应被当成损坏包」的行为。
- **插件目录误报**：`PluginDiscoverer` 现在把 `<id>/data` + `<id>/settings.json` 这类插件数据目录、以及包含实际包的 `local` 容器目录识别为合法布局并静默跳过，不再每次启动打 5 条 `Plugin package rejected during discovery`，也不再在插件页生成 5 个幽灵 invalid 条目。
- **通知已读/图标**：`NotificationService` 记录已读 toast id；打开通知页或点击通知行时标记已读，页面不再给所有行画永久未读点；通知行增加真实 `appLogoOverride`/应用图标映射（之前只有通用铃铛）。新增 `NotificationMarkAllRead` 平台动作，保持 headless 可测。
- **首页状态**：`IslandContent` 新增真实 `IsWifiEnabled`/`IsBluetoothEnabled`（来自 Radio 服务）；Wi-Fi 不再用 `IsOnline` 代替无线电状态，蓝牙不再恒为关。首页天气 chip 改用小组件页同一份真实天气；禁用的 Focus 磁贴不再作为可点击命中目标（之前只写一条 control-unsupported 日志）。
- **托盘/热键**：托盘菜单接入 `LocKeys.Tray.*`（中英随语言），新增独立「隐藏」项；`Ctrl+Alt+H` 走 `HandleHideHotkey` 而不是 toggle；`HotkeyGesture.ToNativeModifiers` 补 `MOD_NOREPEAT`，长按不再重复触发。
- **测试**：新增 `IslandMouseHook.GestureTracker` 6 个用例、`FullscreenDetector` 7 个用例；Platform 115 项定向测试通过；全量 `dotnet test` 2620 passed / 0 failed（Architecture 的 `KugouMemoryPosition.Thread.Sleep`、Integration 的 notification rank 断言，均不由本轮改动引入）。


## 2026-10-21 — 修复灵动岛动画卡顿（帧时钟被 50 ms 截止计时器反复停掉）

- **根因**：`SkiaIslandHost.EnsureFrameLoop` 把「上一次渲染尝试有没有画出像素」(`_lastRenderHappened`) 当作停止条件；50 ms 的 `OnDeadlineTick` 每个 tick 都会走一次不推进动画的 `RenderAllLocked`，于是每 ~50 ms 把 60 fps 帧时钟停掉一次。展开/悬停/波浪动画只能画 2–3 帧就冻住，整岛实际帧率 3–13 fps；Avalonia 设置窗口有独立渲染线程，所以不受影响。
- **修法**：帧时钟只由动画状态决定（新增 `IslandFrameLoopPolicy.ShouldRun(animating, agentAnimating, keepIdleFrames)`）；`RenderAllLocked` 改为返回「是否真的画了一帧」，空帧计数只用这个返回值，`_lastRenderHappened` 字段删除。
- **实测**（published exe，真实鼠标点击）：展开 380 ms 动画 24 帧、frame interval p50 **15.9 ms**（修复前 3–13 fps、最长 1 s 空洞）；展开/收起几何逐帧连续（132×36 → 440×284 → 236×36）；空闲 CPU 12.8% → **2.1%**，工作集 330 MB → 199 MB。
- **回归测试**：`tests/FluidBar.Ui.Tests/IslandFrameLoopPolicyTests.cs`（帧时钟真值表 + 展开动画跨 deadline tick 不中断 + 静止后不再请求帧）。
- 顺带修正过期基线：`PowerModeProbe` 仍断言小岛环 120°/s，实际已统一为 `AgentSpinner` 的 400°/s（0.9 s/圈）；`IslandMetrics.SplitRingPeriodSeconds` 注释同步。
- 基线：**2522 passed / 0 failed**（Core 640 · Platform 660 · Ui 497 · Island 445 · Sdk 167 · PluginHost 39 · Builtin 25 · Integration 31 · Architecture 18）。

## 2026-10-21 — 修复展开后媒体卡片按钮全部点不动（点击区域不跟随动画壳体）

- **根因**：可点击区域（分层窗口命中模型 + 全局鼠标钩子矩形）只在 `RegionSignature` 变化时重建，而签名里只有 `ShellForm`，**没有壳体尺寸**。展开动画 380 ms 里壳体每帧在变大，签名却不变；只要媒体/Agent 动画让 `IsAnimating` 一直为真，签名就再也不变 —— 钩子仍然只把旧药丸那一小块当可点区域，面板主体的按钮（媒体卡播放/上一首/下一首、开关、列表行）全部穿透到桌面。
- **修法**：签名抽成 `IslandHitRegionSignature`（含壳体宽高，单位 0.1 DIP）；动画期间每帧刷新命中区域（`UpdateHitRegions` 是纯内存模型，无 Win32 调用）。
- **证据**（临时输入探针，已删除）：修复前点媒体卡播放键根本没有 `hook-down`；修复后同一坐标得到 `PageControl(PlayPause)` → `MediaTogglePlay`。通知页/媒体页 seek 一直正常，只有壳体扩大后的按钮受影响。
- **回归测试**：`tests/FluidBar.Ui.Tests/IslandHitRegionSignatureTests.cs`（展开改变签名 / 过渡期每帧改变 / 相同状态签名稳定）。
- **顺带**：媒体回退探测（酷狗无 SMTC）原来每个 250 ms tick 都整树遍历 MSAA（约 900 节点跨进程调用，采样占全进程 66%）。给"找时间文本"的整树遍历加 1 s 节流（按钮已缓存，不受影响）：整机 CPU 2.06% → **1.25%**。
- 基线：**2525 passed / 0 failed**（Core 640 · Platform 660 · Ui 500 · Island 445 · Sdk 167 · PluginHost 39 · Builtin 25 · Integration 31 · Architecture 18）。
- Release：`build/release/win-x64/FluidBar.exe` 57.4 MB，SHA256 `D929B1453F2EC329DC7BF45BC56C806E4ECE985ADE2D7EB6458358DC4A1481A4`。
- 决策：工作集 ~191 MB 高于 D12 的 120 MB 目标，属原生内存（Skia/DIB/字体），单独一轮 native 审计，不在本次热修范围。

## 2026-10-21 — 分裂小岛：点哪个岛就开哪张卡

- **现象**：分裂模式下主岛显示的是音乐，点开却是 Agent 卡片。
- **原因**：`IslandHitKind.Compact` 只发了 `IslandInput.PrimaryClick()`，展开页由调度器的 `PrimaryPage` 决定；当后台刚来了一个更新的活动（Agent），调度器的主页就是它，和药丸**实际画的内容**不一致。
- **修复**：点击时先取药丸当前绘制内容的 `_compact.Kind.ToPage()`，展开后若与 `State.Page` 不同，追加 `PageRequested` + `HandlePageRequest`，与副岛（`ActivateSplitIslet`）同一套逻辑。
- 测试：Ui 502 全绿。发布 `EFE77288…936B`。
## 2026-10-21 — 崩溃修复：插件页返回后整程序退出（"启动后没有灵动岛"）

- **根因**：`SettingsSectionBuilder.BuildPlugins` 的 `ShowList()` 每次都 `new Border { Child = search }`。从插件详情返回列表时，`search` 仍挂在旧 Border 上，Avalonia 抛 `InvalidOperationException: The Control already has a parent`，未捕获 → 进程退出（Windows 事件日志 CLR20r3 / .NET Runtime 已确认）。
- 连续两次崩溃后 `CrashGuard` 进入 safe mode，用户看到的就是"程序启动了但没有灵动岛"。
- **修复**：搜索框宿主 Border 只创建一次（`searchHost`），`ShowList` 复用它，不再重新挂父级。
- **运维**：清掉 `%LOCALAPPDATA%\FluidBar\state\crash-guard.json` 复位安全模式；已重新发布并确认岛窗口存在（hwnd rect 609,-12,488x384，visible=True）。
- 测试：Ui 502 全绿。
- 教训：本地化 JSON 与 Avalonia 的"控件只能有一个父级"都属于**运行期才炸**的坑，改设置页后手动点一遍"插件 → 详情 → 返回"。
## 2026-10-21 — 媒体链路：卡拉OK 精度 + 只读进度条

- **秒跳变中点锚定**：酷狗的无障碍时间文本只有 1 秒分辨率，旧实现把"读到 13"当成"此刻=13.000s"，平均偏晚半个轮询（~125 ms）且方向恒定，正是"歌词总比唱的慢"的来源。`PlayerTimeline` 新增 `Age`，秒数跳变时按两次读数的中点回填，`PositionSampledAtUtc` 相应前移。
- **歌词偏移**：`AppSettings.LyricOffsetMs`（±800 ms，50 ms 步进），在 `SyncKaraoke` 锚点时叠加，用于补偿每首歌残余的压制偏移。设置 → 岛屿 分组新增「歌词偏移」滑杆（中英双语，`Settings.Island.LyricOffset`），也可直接改 `settings.ui.json` 的 `appearance.lyricOffsetMs`。
- **只读进度条**：`MediaSessionData`/`MediaPageModel` 新增 `CanSeek`（来自 `MediaSnapshot.CanSeek`）。不可 seek 的音源（酷狗，无 SMTC）进度条降到 45% 不透明度、旋钮缩小，`SkiaIslandControlRouter` 不再产出 `MediaSeek` —— 不再假装能拖。
- 测试：Island 446（新增 `MediaSeekReadOnlyTests`）· Ui 502 · Platform 660 全绿。
- 教训：本地化 JSON 手改漏逗号会让整套 Avalonia 测试全线失败（解析期抛错），改完必须 `JSON.parse` 校验一次。
## 2026-10-21 — 媒体配色/Agent 小岛/播放态/设置窗口 四个修正

- **配色**：封面主色改为「主色调聚类 + 可用性归一化」（`ArtworkAccent`）。原来对所有像素做加权平均，彩色封面会被平均成米灰（波形条发白灰），饱和度 <0.18 又让逐字高亮退回主题粉。现在按色相聚类取占比最大的簇，并把饱和度提到 ≥0.55、明度夹到 0.62–0.92；逐字高亮与波形条都吃这个颜色。
- **Agent 主岛**：`IsAgentAnimating` 原来在指针离开且无副岛环时直接返回 false → 主岛圆环冻结、只有悬停才转。现在主岛自身是运行中的 Agent 时始终请求帧。
- **Agent 副岛**：副岛环改用主岛药丸的细环比例（1.5/9.25 ≈ 0.162，原来用 0.3158 显得肥厚），并补一圈 0.18 透明度的底环，和主岛同一个圆环语言。
- **播放态**：酷狗类回退源在 MSAA 读写含糊时（播放/暂停两个字形同时可见）会用峰值启发式猜状态，导致图标偶尔反了；现在保留上一次确认的字形状态，启发式只用于首帧。
- **设置窗口**：窗口已存在但被最小化时，`BringToFront` 只 `Activate()`，Windows 不会还原 → 点设置像没反应。现在先 `WindowState = Normal` 再激活（必要时 `Show()`）。
- **Agent 主岛帧率**：主岛环常转后按 `IsAgentOnly` 把帧率压到 30 fps（20 DIP 的环在 30 fps 下看不出差别，60 fps 白烧一倍 CPU）。
- 测试：**2527 passed / 0 failed**（Core 640 · Platform 660 · Ui 502 · Island 445 · Sdk 167 · PluginHost 39 · Builtin 25 · Integration 31 · Architecture 18）。
- Release：57.4 MB，SHA256 `6D0B2466706DC02BC9FD6F136E91090AFFA457D86EAA38EDE4F74F92329BA62E`。

## 2026-10-18

### 方向与文档
- 创建 v2 重写分支 `v2/rebuild`；v1 代码移除，打标签 `v1-final`（`e9ca526`）。
- 建立设计规范、实施计划、决策日志（`docs/rewrite/specs|plans|DECISIONS.md`）。
- 建立项目方向备忘 `PROJECT-NOTES.md`（产品初衷、被否决 UX、技术路线、协作规则、文档维护规则）。
- 建立本 CHANGELOG 与 `docs/rewrite/STATUS.md`，作为时效性状态文档。

### 交互设计
- 否决：底部平铺标签栏、左侧竖向 rail、常驻页点、面板内小岛、常驻小岛、展开面板上下黑边、紧凑态图标压字/出界。
- 确定：默认 iOS 小岛 → 点击展开；展开面板 440×284；横向拖动 pager；面板外 3 个临时迷你岛（26/26 + 64×28），距离当前页 2 页以外用小点表示（每侧最多 3 个）。
- 确定：拖动跟随 0.85、吸附 T 0.38 / ζ 0.94、滚轮 140px/180ms、滚动链 40px + 120ms。

### 原型
- `docs/rewrite/prototype2` 干净原型：无黑边、紧凑态像素级不溢出、3 个临时迷你岛、7 分区设置面板 + 插件市场回归。
- 旧 `docs/rewrite/prototype` 因多进程并发改写损坏，归档/弃用。

### 架构与 spike
- Avalonia 11.3.22 / 12.1.4 在本机透明窗口均被压成灰色矩形；WPF 分层窗口与原生 `UpdateLayeredWindow` 正常。
- 渲染底座改为：**Win32 分层宿主（`UpdateLayeredWindow`，32bpp 预乘 DIB）+ SkiaSharp + HarfBuzzSharp**；Avalonia 仅保留设置/插件市场普通窗口。
- 分层宿主 spike GO：全帧 ULW 0.06–0.16ms、脏矩形 0.08–0.10ms、60/120fps pacing、透明/点击穿透/不抢焦点/拖拽 PASS；宿主表面 float 488×384 / dock 488×444 DIP。
- Skia 渲染器 spike GO：展开全帧 0.55–0.61ms / p99 0.85ms，紧凑 0.46ms，脏矩形 0.157ms；fidelity IoU 0.9986（紧凑）/ 1.0000（展开）。

### 代码与测试
- `FluidBar.Core`（473 tests）：事件、状态机、优先级、布局、弹簧、pager、滚动链、设置迁移、soak 预算。
- `FluidBar.Platform.Windows`（371 tests）：Win32/WinRT 集成、监控器、媒体、剪贴板、通知、Agent hooks、USB/BT、托盘/热键、更新检查、分层宿主。
- `FluidBar.Sdk` / `PluginHost` / `Marketplace`（188 tests）：collectible ALC、manifest、签名/索引、安装/回滚、离线 E2E。
- 内置插件（23 tests）、集成测试（31 tests）、Avalonia UI（90 tests）、架构测试（11 tests）。
- 修复：后台计时器跨线程刷新 UI 导致的 30–75s 崩溃；squircle 轮廓不可连接；小岛重叠；页面底部空带；紧凑通知/剪贴板越界。

### 进行中
- `FluidBar.Island`（SkiaSharp 渲染器 MVP）：紧凑态、主页/媒体、pager、面板外小岛与小点。
- 分层宿主与 App 集成（替换 Avalonia 岛窗口，设置/市场保留 Avalonia）。

### 2026-10-18 23:00 — Skia 渲染器 MVP / prototype2 完成
- `FluidBar.Island`（SkiaSharp + HarfBuzzSharp）：紧凑 8 态、展开 home/media、dock/flush/light/transition、transient strip；62 tests、16 张 golden（732×576 @150%）。
- prototype2：设置面板（7 分区）、插件市场、3 个临时小岛、远处页小点、切换弹簧动效；14+ 验证截图。
- 待修：渲染器壳体描边误用强调色（改中性 hairline）；通知/剪贴板/Agent/小组件页面待补。
- 下一步：App 宿主集成（LayeredWindowHost + renderer），预计内部预览 ~1–1.5 周、可用初代 ~3 周、完整版 ~8–12 周。

### 2026-10-18 23:36 — 渲染器页面补全 / App 集成进行中
- Skia 渲染器扩展到 134 tests、20 张 golden：新增 notifications/clipboard/agent/widgets 页面、hit-test、页面切换 golden；壳体描边改中性 hairline。
- App Skia 集成：controller、状态映射、pager、archipelago、主题映射、hit-test、SkiaIslandHost 已落盘；正在修编译并接 FLUIDBAR_HOST=skia 开关。
- Core pager 手感终版（0.85 / T0.38 / ζ0.94 / 26 DIP / flick 0.55）已提交，spec A3 + DECISIONS I16。
- 新增 Skia host 桌面 smoke 脚本与验收清单。

### 2026-10-19 00:12 — Skia 分层宿主接入 App（MVP）
- 新增 `src/FluidBar.App/Hosting/Skia/`：`SkiaIslandHost`（每显示器一个 LayeredWindowHost，488×384 float / 488×444 dock）、controller、motion、pager、archipelago、state builder、theme mapper、hit-test、UI-thread frame clock。
- `AppBootstrap` 默认先启动 Skia 宿主；失败回退 Avalonia；`FLUIDBAR_HOST=skia|avalonia` 可强制。
- 验证：`dotnet test FluidBar.sln -m:1` = 1340/1340；smoke 386 commits/0 失败、surface 732×576、全帧 0.5–1.9ms、脏帧 220；展开/媒体/中文截图与桌面点击穿透探针通过。
- 待办：tray/hotkey、未映射页面控件、UIA/IME、多显示器热插拔、dock 放置 52 DIP 偏差。

### 2026-10-19 00:28 — 生产 smoke + 托盘/热键 + dock 对齐
- 生产 Skia 宿主 smoke（`FLUIDBAR_SMOKE=1 FLUIDBAR_HOST=skia`）：9 个状态、197 帧、0 提交失败、surface 732×576 @150%、脏帧 130、上传 193–223µs、空闲无帧；展开 home/media/notifications/clipboard/agent/widgets 与中文均正常。
- 新增 tray icon + `HotkeyService`：Ctrl+Alt+D 注册成功；Ctrl+Alt+H/S 被占用时回退 Ctrl+Alt+Shift+H/S。
- 修复 `LayeredSurfaceSize.ShellLocalOffset(Dock)` 与 Core dock 组合不一致（旧值 368，现 24），Platform 371 tests 通过。
- 剩余：页面控件平台 seam、多显示器热插拔、UIA/IME、真实热键注入复测、settings 热键可配置。

### 2026-10-19 00:50 — 页面控件 / 多显示器 / DPI / dock 完成
- 页面控件平台 seam：`IIslandPlatformActions` + `SkiaIslandControlRouter` + `SkiaPlatformActions`；媒体播放/上一首/下一首/seek、音量、亮度、主题切换、剪贴板复制已接；通知/Agent/任务/日历为 deferred 事件；Wi-Fi/BT/Focus/output/queue 明确 unsupported。
- 多显示器：`SkiaMonitorPlanner` + `SkiaIslandHost` 热插拔 reconcile（Primary/All、稳定 primary、300ms debounce、移除/新增/保留、DisplayOn 变更）。
- DPI：`WM_DPICHANGED` 先应用 OS 建议再通知 App；surface 按 ceil(DIP×scale) 重建；150%/200% 数学与 placement 测试通过。
- dock：`PlaceForWindowPlacement` 统一 float/flush/dock；Platform 组合常量与 Core 对齐（panel 24..308、strip 316..368、arch 378..408）。
- 测试：**1402 passed / 0 failed**（Core 473, Platform 382, UI 159, Island 135, SDK 152, PluginHost 36, Builtin 23, Integration 31, Architecture 11）。

### 2026-10-19 00:56 — 发布脚本 / 应用图标
- 新增 `tools/release/publish.ps1`：win-x64/win-arm64、单文件、可选 self-contained、复制 README/PROJECT-NOTES/CHANGELOG/插件文档、生成 SHA256 + publish.json + zip。
- 新增 `src/FluidBar.App/Assets/fluidbar.ico` 并设置 `ApplicationIcon`。
- 首次 Release 发布验证：`build/release/win-x64/FluidBar.exe` 53.5 MB（framework-dependent single-file），SHA256 7DC72D16…D1730。

### 2026-10-19 00:58 — Release build smoke
- `build/release/win-x64/FluidBar.exe`（framework-dependent single-file，53.5 MB）以 `FLUIDBAR_HOST=skia FLUIDBAR_SMOKE=1` 运行通过：241 帧、0 提交失败、surface 732×576 @150%、上传 164–182µs、空闲无帧。
- 说明发布产物可正常启动分层宿主；下一步补 UIA/IME、插件运行时、Wi-Fi/BT/Focus 服务与完整验收。

### 2026-10-19 02:12 — 插件运行时 + FollowMouse/锚点拖拽
- 真实 PluginHost/Marketplace 接入 App：插件发现/加载/启用、市场浏览/安装/回滚、权限同意、插件设置页；设计态服务保留为离线回退。
- FollowMouse 目标选择（cursor→monitor seam）与锚点拖拽持久化（click-vs-drag 阈值、吸附/写回、恢复）接入 Skia 宿主。
- 修复插件生命周期 dispose 未走 Unloading→Unloaded；修复 App 控制测试对 deferred compact click 的假设。
- 全量：`dotnet test FluidBar.sln -m:1` = **1423 passed / 0 failed**（Core 491, Platform 382, UI 159, Island 135, SDK 152, PluginHost 39, Builtin 23, Integration 31, Architecture 11）。
- 生产 smoke：331 帧、0 提交失败、surface 732×576 @150%、上传 ~192µs、空闲无帧。

### 2026-10-19 02:50 — UIA 无障碍 + Wi-Fi/BT/Focus 服务
- UIA：`FluidBar.Platform.Windows.Accessibility`（UiaElementProvider/UiaInterop/UiaNative/UiaTypes）+ `LayeredWindowHost.Accessibility` + `SkiaIslandUiaSource`；根元素/页面/控件/小岛树、Invoke/ExpandCollapse/Selection/RangeValue/Toggle 模式、WM_GETOBJECT、键盘焦点；文档 `contracts/island-uia.md`。
- 快捷开关：`RadioToggleService`（Wi-Fi/Bluetooth，Windows.Devices.Radios）+ `FocusAssistService`（FocusSessionManager，LAF 受限时显式 unsupported）；`SkiaPlatformActions` 接线；平台测试 +13。
- 构建：`WindowsSdkPackageVersion=10.0.26100.57`（FocusSessionManager 投影；OS floor 仍 19041）。
- 全量：**1475 passed / 0 failed**（Core 491, Platform 409, UI 184, Island 135, SDK 152, PluginHost 39, Builtin 23, Integration 31, Architecture 11）。

### 2026-10-19 03:02 — v2.0.0-mvp 发布
- 发布说明：`docs/rewrite/RELEASE-v2-mvp.md`；Release 产物 `build/release/win-x64/FluidBar.exe`（55.2 MB，SHA256 65324EE8…CB44），发布 smoke 通过（238 帧/0 失败/上传 ~153µs）。
- 全量测试 **1475 passed / 0 failed**；UIA/Wi-Fi/BT/Focus/插件运行时/FollowMouse/锚点拖拽均已接入。
- 已知限制：Focus LAF、output/queue、IME/MSAA、真机多显示器/200%、Narrator 手工验收、市场签名密钥、双 message host、托盘图标。

### 2026-10-19 03:08 — MVP 发布包
- `tools/release/publish.ps1` 生成 `build/release/FluidBar-win-x64.zip`（47 MB）；exe 56.5 MB，SHA256 86A4F33A…A4021；Release smoke 通过。

### 2026-10-19 12:30 — 可编辑全局热键 + 实时重注册
- `AppSettings` 新增 `HotkeyToggle/HotkeyHide/HotkeySettings`，设置 → 通用改为可编辑 TextBox（Enter/失焦提交），JSON 往返与默认回退有测试。
- `SkiaShellIntegration.ApplyBindingsAsync`：保存后立即 unregister/register，无需重启；冲突时回退 Ctrl+Alt+Shift。
- 修复 AppBootstrap 退出时未释放 tray/hotkey 集成。
- 全量：**1477 passed / 0 failed**（UI 186）。

### 2026-10-19 12:35 — 最终发布校验
- 最终 Release：`build/release/win-x64/FluidBar.exe` 56.5 MB，SHA256 `DA422BA5…37362`；发布 smoke 420 帧、0 提交失败、surface 732×576 @150%、上传 ~273µs。
- 全量 1477 tests 绿；热键可在设置中编辑并实时重注册。

### 2026-10-19 13:45 — 可用性热修（顶层窗口 / 托盘菜单 / 点击）
- **根因 1**：`LayeredWindowHost.Show()` 使用 `SWP_NOZORDER`，岛窗口虽带 `WS_EX_TOPMOST` 但没有真正进入 topmost 带；其他窗口可能盖在上面，导致点击落不到岛、视觉却仍可见。现在 `Show()` 用 `SetWindowPos(HWND_TOPMOST, ...)` 强制置顶。
- **根因 2**：托盘菜单使用 message-only HWND，`TrackPopupMenu` 无法在 `HWND_MESSAGE` 上显示；托盘窗口改为 `PlatformWindowKind.HiddenTopLevel`，右键菜单可正常弹出。
- 紧凑岛改为**按下即展开**（拖动移动作为次级手势），避免 click-vs-drag 让单击感觉无响应；命中区域保留 8 DIP 防御性 padding。
- 测试：1478 passed / 0 failed；Release smoke 330 帧/0 失败。
- 注意：如果桌面上还有旧版 FluidBar 进程/自启动项，请先全部退出再运行新 exe。

### 2026-10-19 14:40 — 真实数据源接入（修复"贴图/假剪切板"）
- 根因：`SkiaPlatformActions` 构造函数在 `_focus` 赋值前订阅 `_focus.SnapshotChanged`，抛 NullReferenceException；`AppBootstrap` 捕获后回退到 `DesignIslandDataSource`（演示数据），所以剪贴板/通知都是假的。
- 修复：调整构造顺序；`SkiaPlatformActions` 实现 `IIslandDataSource`，`Snapshot()` 映射真实剪贴板历史、GSMTC 媒体、电池/充电/专注状态；通知/Agent/任务不再伪造，返回空列表。
- 剪贴板行复制改为按索引取**完整文本**（不再复制渲染预览）；媒体播放/seek/音量/亮度/主题继续走真实平台服务。
- 验证：`actions created` 日志确认真实数据源已启用；1478 tests（PluginHost 偶发 flaky）；Release 构建通过。

### 2026-10-19 16:40 — 修复“状态已展开但画面停在紧凑药丸”（渲染提交根因）
- **根因（已用独立 Win32 实验复现）**：`UpdateLayeredWindowIndirect` 在 `pptDst = psize = NULL`（隐式几何）时返回 TRUE 但**分层表面完全不更新**。宿主只在首帧（`_geometryDirty` 为真）显式传几何，所以只有开机第一帧真正上屏；之后展开/翻页/收起的提交全部“成功但不可见”。
- 修复：`LayeredWindowHost.TryUpdate` 每次提交都显式传 `pptDst`/`psize`（每帧一次 `GetWindowRect`，相对 DIB 上传可忽略）；删除失效的 `UseExplicitUpdateGeometry` 选项与 `_geometryDirty` 死状态。
- 恢复弹簧动画：`OnStateChanged` 不再 `SnapMotion()`，展开/收起走 Core 弹簧；实测点击后连续 4 帧可见尺寸增长（132 → 中态 → 440×284）。
- 恢复点击穿透：`IslandMouseHook` 命中矩形从整个 488×384 宿主改回 shell 矩形（+2 DIP 容差），透明 bleed 区域点击回到下方窗口（`WindowFromPoint` 验证）。
- 悬停不再被自动收起打断：Core `EnterExpanded` 在指针悬停时不武装 `AutoCollapseDeadline`；展开态 `PointerEntered` 清除、`PointerLeft` 重新武装；钩子新增 `PointerLeft` 事件（分层窗口可能收不到 `WM_MOUSELEAVE`），控制器把展开态纳入 hover 派发。
- 修复紧凑药丸“宽度与内容不一致”：活动在展开态结束时，Core 现在从清空后的 baseState 进入 Closing（`ContentKind`/`ActivityCount` 不再残留）；App `BuildTarget` 与 `BuildCompact` 统一用 view model 的活动类型取宽度。
- 空闲时钟恢复走字：`SkiaPlatformActions` 增加对齐到分钟边界的 `ITimer`，每分钟 raise `Changed`（此前只有媒体/剪贴板/电池变化才会重绘，药丸时间会冻在首次绘制那一刻）。
- 清理上一轮遗留的全部临时日志（`fluidbar-trace.log` / `fluidbar-hook.log` 写入点、`Trace()` helper）。
- 测试：Core 494（+3 状态机回归）、UI 188（+1 音量/亮度映射）；`dotnet build FluidBar.sln` 0 错误。

### 2026-10-19 17:05 — 真实音量/亮度 + 物理显示器枚举 + 最终发布
- 首页音量/亮度不再写死演示值（62/78）：`IslandContent` 新增 `VolumePercent`/`IsMuted`/`BrightnessPercent`，`SkiaPlatformActions` 映射真实 WASAPI 音量与 DDC/CI 亮度并订阅其变化；未知时渲染 "—" 且轨道归零（不伪造数字）。设计态 `DesignIslandDataSource` 保留 62/78 供预览/测试。
- `MonitorService` 枚举前显式 `SetThreadDpiAwarenessContext(PMv2)`：显示器枚举不再受调用线程 DPI 上下文影响（线程池线程/消息线程下曾可能拿到 96 DPI 的虚拟化工作区）。
- 全量测试 **1482 passed / 0 failed**（Core 494 · Platform 409 · SDK 152 · UI 188 · Island 135 · PluginHost 39 · Builtin 23 · Integration 31 · Architecture 11）。
- 最终 Release：`build/release/win-x64/FluidBar.exe` 56.5 MB，SHA256 `D5A02A4DBD5F64563878D646AD35C0A42ECE3CB8B0BEFD37135CD9496734992E`。
- 真实鼠标回归（DPI-aware 取证脚本）：窗口 732×576 @150% → 点击药丸弹簧展开 660×424 → 悬停 4s 保持 → 移开 3.8s 自动收起 196×52 → 再展开 → chevron 收起；首页显示真实音量 60 / 亮度 "—" / 电量 99% 充电 / Wi-Fi 开。
- 鼠标钩子自愈：`IslandMouseHook` 独立消息线程（避免和剪贴板/显示消息排队导致 WH_MOUSE_LL 回调超时被系统摘除）+ 1s 看门狗（光标位置变化但 800ms 内没有回调 → 重新安装钩子）。60s 连续 6 轮点击/热键回归全部通过。
- 注意：`FLUIDBAR_SMOKE=1` 会把 `settings.ui.json` 的 `autoCollapseMs` 改成 0（smoke 需要），跑完冒烟记得改回 2600，否则展开态不会自动收起。
- 展开态悬停时不再因“活动结束”被强制收起（临时剪贴板/通知活动到期不应把用户正在操作的面板关掉）；内容原地更新，指针离开后走 2.6s 自动收起。
- 鼠标钩子改为在首帧渲染完成后才安装：此前启动早期（控制器还没有 render state）钩子会吞掉点击但不处理，表现为“启动后第一次点击没反应”。3 次冷启动 3 秒内首点全部成功。

### 2026-10-19 18:10 — 岛下方预览动效 + 真实亮度 + 媒体封面/歌词/进度
- **岛下方常驻预览**：展开且未切换时显示 6 个很淡的小点（4 DIP，α≈0.20），当前页为 14×4 胶囊（α≈0.52）并跟随翻页连续滑动；滚轮/拖拽/热区唤出 3 岛胶片条时小点先淡出、胶片条错峰淡入（当前岛先、邻岛随后并轻微外滑），换页时当前岛 1.06× 脉冲、标签淡出淡入。收起时全部隐藏。
- **亮度根因修复**：WMI 的 `CurrentBrightness` 不能直接当 IDispatch 属性读（`instance.CurrentBrightness` 永远为空），必须走 `instance.Properties_.Item("CurrentBrightness").Value`。修复后本机真实亮度 50 可读、滑块可写（`WmiSetBrightness`）。
- **媒体真实封面**：GSMTC `ArtworkCache` 的原始字节进入 `MediaSessionData`，岛内 `ArtworkImageCache`（按 track key，LRU 4）解码为 `SKImage`，媒体页大图与首页卡片小图都画真实封面（无封面时回退渐变占位）。
- **媒体进度**：用 `Position + (now - PositionUpdatedAtUtc)` 插值；有播放会话时 1 Hz tick 刷新进度条与歌词行（无会话时不创建定时器）。
- **歌词**：复用 `LyricsService`（网易云/酷狗 + LRC 解析 + 30min 缓存），按插值位置取当前/下一行；取不到就隐藏，不伪造。
- **浏览器媒体验证**：用独立 Edge 实例 + Media Session API 页面实测，GSMTC 正确上报标题/艺术家/专辑/封面/进度；岛首页卡片显示封面 + “Midnight City / M83” + 走动进度条，媒体页显示大封面、标题、歌词（网易云命中）与 0:04/0:05 进度。
- 展开时按下指针会同步 hover 状态，避免“展开后在光标下自动收起”。
- 测试：**1484 passed / 0 failed**（Core 495 · Platform 409 · SDK 152 · UI 188 · Island 136 · PluginHost 39 · Builtin 23 · Integration 31 · Architecture 11）。
- 胶片条自动隐藏 1000 → **1200 ms**（设计区间上限），切换动画可读性更好。
- **拖拽翻页恢复**：全局钩子吞掉按下事件后，分层窗口拿不到鼠标捕获，pager 拖动一直失效；现在钩子在按住期间转发 `PointerDragged`，宿主用同一套 `HandleDragStarted/Moved/Ended` 重建拖拽手势。实测按住拖动 300px 从 04 翻到 05，胶片条同步出现。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `65A793359FFEA8A2F11F65F34F7A9E770837FA7E61954B784348CF3C01360F97`。

### 2026-10-20 04:35 代理事件链路真机验证通过
- 在应用运行中向 `%APPDATA%\FluidBar\agent-events` 写入 `{"agent":"deepseek-harness","event":"Stop"}`，约 0.9 s 后岛上出现紧凑胶囊：**「DeepSeek Ha… 」+「已完成 ●」**，证明 收件箱 → 代理插件 → 岛活动 全链路打通（含新加的 DeepSeek Harness 类型）。
- 之前的「链路不通」是验证方法问题：活动有存活时长，启动前写入的事件会在启动瞬间播放并很快过期，截图时已看不见。
- 已知瑕疵：胶囊标题被截断成「DeepSeek Ha…」，需要加宽或缩短显示名（下一轮）。

### 2026-10-20 04:05 代理页显示事件时间
- `AgentItemData.TimeText` 之前恒为空，代理列表看不出事件先后；现在按收件箱事件的本地时间填 `HH:mm`（与界面语言无关，不需要新翻译键）。
- 与上一项合计：代理页每行 = 代理名 + 状态（运行中/已完成/失败/等待输入）+ 时间 + 详情。
- 全量 **1493 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `E8FA7342D1513E7DA9720E3681CF964CC645A06B790551C8B79CBFDC8F7FAF40`。

### 2026-10-20 03:40 代理插件支持 DeepSeek Harness
- 钩子事件里的 `agent` 字段现在识别 **deepseek / deepseek-harness / dsh / harness**，映射到新的 `AgentKind.DeepSeek`；此前所有非 Claude/Codex 的代理都会落到 `Generic`，岛上只能显示成「Agent」。
- 代理页显示名「DeepSeek Harness」，与 Claude Code、Codex 并列。
- `AgentTests` 增加 3 个解析用例（Platform 415 -> 418）。
- 全量 **1493 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `09D27B541AFD858CC34BC9F239A7FB78F2949E2A52CA498B39494A061CFE43BA`。

### 2026-10-20 03:10 — 插件页布局修复
- **搜索框跑到中间**：`TextBox` 默认 `HorizontalAlignment=Stretch`，一旦显式设置 `Width` 就会居中。改为 `Left` 对齐并加下边距。
- **每行标题错位**：名称/版本/「内置」标签原来放在横向 `StackPanel` 里，导致版本和标签被挤到第二行、行高被撑高。改成 `Auto,Auto,Auto` 的 `Grid` + 8 DIP 列间距，三者回到同一行。
- **行内文案合并**：发布者与能力说明合成一行（`FluidBar · 读取当前媒体会话 · 在后台运行`），行高从约 110 px 降到 67 DIP，接近原型密度。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `774535C6631E726246187857A8C860B2A9D1297689D12488FCC15C2D8B57CB0D`。

### 2026-10-20 02:40 — 设置「外观」页与原型一致
- **外观页只留原型的 5 行 + 预览**：桌面主题（两段：深色 / 浅色）、灵动岛材质（黑曜石 / 亚克力）、强调色（5 个色点）、大面板圆角（滑杆 + 数值 chip）、填充不透明度（滑杆 + 数值 chip），下面是「灵动岛预览」大面板与页脚说明。
- **副标题改用原型文案**：「桌面主题与灵动岛材质。浅色主题下灵动岛仍保持黑曜石，以确保内容对比度。」
- **能力没丢**：调色板、强调色跟随封面、字号比例、减少动效、动效强度、主题跟随系统全部移到「高级」页的同一分组里。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `D0AEB9E99EADEE62BF8F0DA2F676926C758AE0956D0AEEC66774DFE780B63857`。

### 2026-10-20 02:05 — 设置「通用」页与原型一致
- **通用页改为原型的 4 行结构**：界面语言（两段：简体中文 / English）、随 Windows 启动（开关）、**自动检查更新（开关，原为三段）**、显示托盘图标（开关）。移除了原型没有的「匿名使用数据」行。
- **副标题改用原型文案**：「启动方式、更新策略与全局快捷键。」
- **能力没丢**：跟随系统语言、更新方式（通知我/自动安装/从不检查）、匿名使用数据说明都移到「高级」页新分组，功能完整保留。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `A45FE7DFA250AEC81D7728FEC412F3E2ECFA080C4EA73804804758ADFFF73EFB`。

### 2026-10-20 01:35 — 设置窗口密度对齐原型
- **键帽**：22 DIP 高、圆角 5 → **18 DIP 高、圆角 4、8 px 字号、20 最小宽**（原型快捷键 chip 为 24×16 CSS）。
- **行密度**：设置行最小高度 53 → **46**，行内上下留白 8 → 6，让首页信息量与原型一致（原型整页无滚动条）。
- 这轮同时确认了小胶囊封面/进度与小岛条尺寸（见上一条目）。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `94D2FBA8EEE34A0C327DEF8AD3E4CBDBD4F84DDEBB76BBC4A83CC678F80510C6`。

### 2026-10-20 01:05 — 小胶囊封面/进度 + 小岛条按原型尺寸
- **小胶囊媒体封面**：`CompactModel` 之前完全没有封面字段，媒体活动只画了渐变占位图 + 音符图标。现在补上 `Artwork`/`ArtworkKey`，从 `IslandPresenter.ResolveMedia` 取真实封面字节，`CompactRenderer` 复用 `ArtworkImageCache` 裁圆绘制（与媒体页同一套图）。
- **小胶囊进度条恒为 0**：进度以前只取活动负载里的 `ProgressPercent`（常有缺失），现在改为取媒体快照的真实进度；`HasTimeline=false` 时**不再画空进度条**（之前画的就是那条永远为 0 的杠）。
- **下方小岛条尺寸对齐原型**实测值：左右预览岛 **26×26**（原 40×24）、当前岛 **73×28**（原 76×24）、岛间距 8、远点与岛间距 7（原 8），点直径 3、点距 4 不变。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `98DDFD87158CC4071DB8DFF492E6CDC65046876ED9BCF4F83A37AC60B6E14773`。

### 2026-10-20 00:10 — 媒体页：浏览器视频不再显示错误歌词
- **歌词来源收窄**：浏览器媒体会话默认是视频，之前拿视频标题/作者去匹配歌词库，会把无关文本（例如视频字幕「下一步」）当歌词显示。现在只有**暴露专辑元数据的浏览器来源**（音乐站点）才做歌词匹配。
- **空歌词卡片**：媒体页在没有歌词时不再画一张只剩「下一步」标签的空卡，版面更干净。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `79CC1477F6B3935793154757266E5903D374059282682030A493EC349B052B02`。

### 2026-10-19 23:20 — 小岛条动效对齐原型
- **弹簧参数**：小岛条的展开/位移/缩放原来复用 Hover 弹簧（T0.24 / ζ0.70，偏弹），改为原型的 RAIL_SPRING **T0.32 / ζ0.85**（新增 `MotionTokens.Rail`）。
- **自动隐藏**：最后一次操作后 1200 ms → **900 ms**（原型规范），Core 的 `ArchipelagoHideDelayMs` 与 App 常量同步，策略测试边界从 1199/1200 改为 899/900。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `7DC93ADEDD6C43D75EEA6775DCD26FC3A57A2563D0642EBB3D1783EC4801A7EC`。

### 2026-10-19 23:05 — 灵动岛页面本地化 + 空态
- **中文界面下的英文残留**：日期格式改为「10月19日 周一」；蓝牙标签、主题切换标签（深色/浅色，之前误显示「系统」）走本地化；通知/剪贴板/代理/媒体四个页面的空态标题与提示全部接本地化键（之前渲染器回退成英文）。
- **首页媒体卡空态**：没有媒体时显示「没有正在播放的内容」，不再是一张只有封面占位和播放键的空卡。
- 与上一项合计：小岛条胶囊化 + 远点命中修复。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `3701805DC062CE4DE31074D8F0C0292E2825CDA8DC2D7BFD3EF9F5749BB4A6B8`。

### 2026-10-19 23:10 — 下方小岛条按原型对齐
- **小岛尺寸/形状对齐 prototype2**：左右预览岛由 26×26 改为 **40×24 胶囊**，当前岛 64×24 → **76×24**（短标题不再被截断），远点间距 5 → **4**，当前岛标签由强调色改为**白色**。
- **远点点击修复**：远点命中区原来两个方向都按 6px 膨胀（12×12），在 4px 间距下相邻命中区重叠，点最远的点会命中相邻点。改为原型的 **7×15** 命中区（水平 ±2、垂直 ±6），岛测试恢复全绿。
- 与上一项合计：`AppSettings.PanelRadius`（16–36）贯通渲染、外观改动即时重绘、插件不可用文案本地化。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `DA8A34E9D28137987EB6297A54EDB118D7C6796B43F03BE7492EE893B6F9DD59`。

### 2026-10-19 22:55 — 设置：大面板圆角可调 + 插件错误本地化
- **大面板圆角**：新增 `AppSettings.PanelRadius`（16–36 DIP，默认 28，持久化到 `appearance.panelRadius`），经 `LayoutTokens.RadiiFor` → `SkiaIslandMotion.PanelRadius` → `IslandRenderState.ShellRadii` 贯通到灵动岛渲染；外观页新增「大面板圆角」滑杆（16px / 28px 拐角实测不同）。顺带修复外观类设置改动后灵动岛不重绘的问题（`OnSettingsChanged` 现在重建渲染状态并请求整帧）。
- **插件不可用文案本地化**：运行时改为返回 `Settings.Plugins.ErrorPlatformServiceUnavailable` 键，插件页解析为中文/英文，不再直接显示英文诊断。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `E4320B0E57DC7E3B1FF63A634E4D26CB7CF265B189453E16DCE2B8FEF8E86F57`。

### 2026-10-19 22:45 — 设置：插件市场按原型重排
- **精选卡**：渐变底（青色径向光晕）+ 8px 大写 kicker、20px 标题、10px 描述、浅色安装按钮与安装量、80px 圆角图标块。
- **工具条**：34px 搜索框（放大镜 + 占位符）+ 分类 chips（全部/媒体/效率/系统/开发者/其他，选中态白底深字）。
- **双列卡片网格**：34px 分类色图标、名称 + 已验证勾、作者 · 版本、两行描述、安装量 + 安装/更新/卸载/回滚按钮；底部原型脚注（签名校验说明）。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `E5D19099F15049F334DCA9B52B309AE5AC843C3F84A5879DB11186436C7F5C23`。

### 2026-10-19 22:25 — 设置：高级 / 关于 对齐原型
- **高级**：等宽字体数据目录路径框 + 四个小动作（打开目录 / 查看日志 / 导出设置 / 导入设置）、帧统计/崩溃保护/安全模式开关、重置区（分段选择重置范围 + 输入 RESET 解锁的危险按钮，按范围执行真实重置）。
- **关于**：居中的应用标记（强调色光晕）、23px 名称、版本与构建号、检查更新 / 网站 / 源代码 胶囊按钮、许可证行、贡献者胶囊。
- **插件市场**：修复分类下拉框空白（先加选项再设选中项）。
- 全量 **1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `56AEF3A1586159000703B3BDFBBC2F346DA7C6BA318C0E7C56C9A46F03429FB2`。

### 2026-10-19 21:55 — 设置菜单按网页原型 1:1 重做
- **窗口外壳对齐 prototype2**：900×620、22px 圆角、50 DIP 标题栏（岛标 + “设置” + “FLUIDBAR 2.0” + 悬停变红的关闭键）、208 DIP 侧栏（“偏好设置” kicker、图标 + 计数徽章、底部版本块）、30/24 内容内边距。
- **控件按原型重建**：分段控件（自适应宽度、活动段白底深字）、iOS 风格开关（38×22、强调色 #4CC9F0）、3px 细滑杆 + 数值胶囊、快捷键键帽（点击可切换为编辑框）、强调色圆点（活动环）、hairline 分隔的设置行（12px 标题 / 10px 说明）。
- **分区内容对齐**：通用（语言/开机启动/更新/托盘/快捷键）、外观（主题/材质/配色/强调色/透明度/字号/减少动效/动效强度 + 渐变画布上的实时灵动岛预览）、灵动岛（模式/锚点/堆叠/悬停/自动收起/自动隐藏/切歌预览/显示于）、插件（本地化名称 + 真实启用状态 + 原型行样式）。
- **修复插件启用状态错乱**：早期版本把设计期 id（`builtin.media`）写进设置，运行时用的是 `fluidbar-media`，导致设置里全部显示“已停用”。默认值与读取迁移都改为运行时 id。
- **设置窗口置顶**：后台进程调用 `Show()` 会被前台窗口遮挡，改为单实例 + Activate + 短暂 Topmost 切换。
- 测试：**1490 passed / 0 failed**。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `4AE296D7B24C928B7C778B926965651F74FE8E619239BF49C2DCD73C03311303`。

### 2026-10-19 20:55 — 酷狗等非 SMTC 播放器识别 + 亮度写入彻底修复
- **媒体识别（酷狗）**：酷狗不注册 Windows SMTC，GSMTC 永远返回 0 个会话。新增 WASAPI 音频会话探测（`IAudioSessionManager2` → 进程 PID + Active 状态 + 峰值）+ 播放器窗口标题解析（`MediaWindowFallback`）作为回退源：仅当 GSMTC 没有活动会话时，每 1s 检查一次；识别到就发布 `MediaSnapshot`（标题/艺术家/播放中），无封面与时间轴时如实留空。
- **紧凑药丸**：媒体播放时药丸显示 标题 / 艺术家 / 波形 / 播放暂停图标（酷狗实测 “Cameras / Justice Skolnik、Jeremy Zucker”）。
- **无时间轴不伪造进度**：新增 `HasTimeline`，回退源隐藏进度条与 0:00 时间行；同时修复 `ResolveMedia` 丢失 `HasTimeline`/`Artwork` 的问题（此前媒体页会画出假进度）。
- **亮度写入彻底修复**：本机 WMI provider 在同一个 COM apartment 上第二次写入必然失败（“未指定的错误”），且失败后同进程长时间不可用。改为**每次写入都在全新的 MTA 线程/COM apartment 上执行**（写后立即释放 COM 对象），实测连续 35→50→81→5 四次写入全部成功。
- **0% 保护**：滑块最低夹到 **5%**（`BrightnessMath.MinimumPercent`），避免把屏幕调到全黑后无法恢复；读失败自动重连，不再永久禁用亮度。
- 测试：**1490 passed / 0 failed**（Platform 415，新增 6 个回退解析/亮度下限测试）。
- Release：`build/release/win-x64/FluidBar.exe` SHA256 `E1BEE457255CAF01C66423955D8B26144158EE21B3E42B3305E5F056FAA5F2C4`。
