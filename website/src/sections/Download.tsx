import { ArrowUpRight, WindowsLogo } from '@phosphor-icons/react';
import { BrandMark } from '../ui/BrandMark';
import { ISSUES, RELEASES } from '../lib/links';
import { RELEASE } from '../data/release';
import { DownloadStats } from './DownloadStats';
import './download.css';

export function Download() {
  return (
    <section id="download" className="download section" aria-labelledby="download-title">
      <div className="site-shell">
        <div className="download-panel">
          <div className="download-copy"><BrandMark size={48} /><p className="download-release">FluidBar {RELEASE.version} · 正式版</p><h2 id="download-title" className="section-title">给桌面，<br />留一座小岛。</h2><p className="download-description">免费下载，开放源代码。<br />从今天起，让常用的小事更顺手。</p></div>
          <div className="download-package"><div><WindowsLogo size={32} weight="light" aria-hidden="true" /><h3>为 Windows 而来。</h3><p>Windows 10 / 11 · x64</p></div>
            <a className="button download-primary" href={RELEASE.download}><WindowsLogo size={18} aria-hidden="true" />下载 Windows 版</a>
            <p className="download-package__meta">ZIP · {RELEASE.size} · 已包含 .NET 运行时</p>
            <div className="download-package__links"><a href={RELEASE.url} target="_blank" rel="noopener noreferrer">发布记录<ArrowUpRight size={12} aria-hidden="true" /></a><a href={RELEASES} target="_blank" rel="noopener noreferrer">历史版本<ArrowUpRight size={12} aria-hidden="true" /></a></div>
          </div>
        </div>
        <DownloadStats />
        <ol className="install-steps" aria-label="安装步骤">
          <li><span>1</span><div><h3>下载并完整解压</h3><p>将 ZIP 解压到独立文件夹。</p></div></li>
          <li><span>2</span><div><h3>启动 FluidBar.exe</h3><p>运行时已包含，打开即可使用。</p></div></li>
          <li><span>3</span><div><h3>调整成你的习惯</h3><p>从系统托盘进入设置，选择形态与材质。</p></div></li>
        </ol>
        <div className="download-faq"><div className="faq-heading"><h3 id="faq-title">还有些问题？</h3><a className="text-link" href={ISSUES} target="_blank" rel="noopener noreferrer">反馈问题<ArrowUpRight size={14} aria-hidden="true" /></a></div>
          <div className="faq-list" aria-labelledby="faq-title">
            <details><summary>支持哪些系统？需要安装 .NET 吗？</summary><p>支持 Windows 10 / 11 x64。{RELEASE.version} 正式版发布包已包含 .NET 运行时，无需单独安装。</p></details>
            <details><summary>下载后如何启动、设置和退出？</summary><p>完整解压 ZIP 后运行 FluidBar.exe。设置和退出入口位于 Windows 系统托盘；如果没有看到图标，请检查托盘折叠区。</p></details>
            <details><summary>如何核对下载文件？</summary><p>当前发布包尚未进行 Windows 代码签名。可以使用发布清单中的 SHA-256 核对 ZIP 文件：</p><code className="download-hash">{RELEASE.sha256}</code><a href={`${RELEASE.download.slice(0, RELEASE.download.lastIndexOf('/') + 1)}publish-${RELEASE.version}.json`} target="_blank" rel="noopener noreferrer">查看发布清单 ↗</a></details>
            <details><summary>网页演示会读取我的电脑数据吗？</summary><p>不会。演示使用同源交互原型和示例数据，不读取你的媒体、通知或剪贴板，也不播放音频。网页材质是 CSS 视觉近似，软件使用 Skia 绘制，最新界面以正式版为准。</p></details>
            <details><summary>插件如何安装？</summary><p>打开「设置 → 插件市场」，选择需要的插件后安装。倒计时与文件暂存属于可选插件，默认不安装；安装、更新和回滚均可在应用内完成。</p></details>
            <details><summary>遇到问题如何反馈？</summary><p>请在 GitHub Issues 附上 FluidBar 版本、Windows 版本、显示缩放比例和复现步骤。截图前请遮住通知、剪贴板等私人内容。</p></details>
          </div>
        </div>
      </div>
    </section>
  );
}
