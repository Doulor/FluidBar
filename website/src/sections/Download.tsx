import { ArrowUpRight, GithubLogo } from '@phosphor-icons/react';
import { BrandMark } from '../ui/BrandMark';
import { GITHUB, ISSUES, RELEASES } from '../lib/links';
import { RELEASE } from '../data/release';
import './download.css';

export function Download() {
  return (
    <section id="download" className="download" aria-labelledby="download-title">
      <div className="site-shell">
        <div className="download-panel">
          <div className="download-mark" aria-hidden="true">
            <BrandMark size={74} />
            <span className="download-mark__orbit" />
          </div>

          <div className="download-copy">
            <p className="section-kicker">05 / DOWNLOAD</p>
            <h2 id="download-title">
              现在，
              <br />
              把岛带走。
            </h2>
            <p>
              Windows 10 / 11 x64。免费、开源。当前为开发预览版，网页交互为示例，最新界面以软件为准。
            </p>
          </div>

          <div className="download-actions">
            <p className="download-version">v{RELEASE.version}<br /><time dateTime={RELEASE.date}>{RELEASE.date}</time> · 开发预览版</p>
            <a className="download-primary" href={RELEASE.download} target="_blank" rel="noopener noreferrer">
              下载预览版 · ZIP
              <ArrowUpRight size={17} aria-hidden="true" />
            </a>
            <a className="download-secondary" href={GITHUB} target="_blank" rel="noopener noreferrer">
              <GithubLogo size={16} aria-hidden="true" />
              查看源代码
            </a>
            <a className="download-link" href={RELEASE.url} target="_blank" rel="noopener noreferrer">
              本版本发布记录
            </a>
          </div>

          <div className="download-meta">
            <span>
              <strong>系统</strong> Windows 10 / 11 x64
            </span>
            <span>
              <strong>运行时</strong> 已包含，无需另装 .NET
            </span>
            <span>
              <strong>下载</strong> {RELEASE.size} · MIT
            </span>
          </div>
        </div>
        <ol className="install-steps" aria-label="安装步骤">
          <li><span>01</span><strong>下载压缩包</strong><p>选择上方 ZIP；其他历史版本可在 <a href={RELEASES} target="_blank" rel="noopener noreferrer">GitHub Releases</a> 找到。</p></li>
          <li><span>02</span><strong>解压后启动</strong><p>完整解压到独立文件夹，运行 FluidBar.exe。此版本已包含运行时。</p></li>
          <li><span>03</span><strong>按习惯调整</strong><p>从系统托盘打开设置，选择形态、材质和启用的插件。</p></li>
        </ol>
        <div className="download-faq" aria-labelledby="faq-title">
          <div><p className="section-kicker">BEFORE YOU START</p><h3 id="faq-title">开始之前</h3><a href={ISSUES} target="_blank" rel="noopener noreferrer">反馈问题 ↗</a></div>
          <div className="faq-list">
            <details><summary>支持哪些系统？需要安装 .NET 吗？</summary><p>支持 Windows 10 / 11 x64。上方 4.0.0-preview.2 发布包已包含 .NET 运行时，无需单独安装；其他版本请以对应发布说明为准。</p></details>
            <details><summary>下载后如何启动和退出？</summary><p>先完整解压 ZIP，再运行 FluidBar.exe。设置和退出入口位于 Windows 系统托盘；如果没有看到图标，请检查托盘折叠区。</p></details>
            <details><summary>预览版有签名吗？如何核对下载文件？</summary><p>此版本尚未进行 Windows 代码签名。可使用发布清单中的 SHA-256 核对 ZIP：</p><code className="download-hash">{RELEASE.sha256}</code><a href={`${RELEASE.download.slice(0, RELEASE.download.lastIndexOf('/') + 1)}publish-${RELEASE.version}.json`} target="_blank" rel="noopener noreferrer">查看发布清单 ↗</a></details>
            <details><summary>网页演示和实际软件完全一致吗？</summary><p>网页使用可交互原型和示例数据，不读取你的媒体、通知或剪贴板，也不播放音频。网页材质是 CSS 视觉近似，软件使用 Skia 绘制，实际界面随版本更新。</p></details>
            <details><summary>遇到问题应该提供什么信息？</summary><p>请在 GitHub Issues 附上 FluidBar 版本、Windows 版本、显示缩放比例，以及复现步骤。截图前请遮住通知、剪贴板等私人内容。</p></details>
          </div>
        </div>
      </div>
    </section>
  );
}
