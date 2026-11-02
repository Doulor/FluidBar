import { ArrowUpRight } from '@phosphor-icons/react';
import { MaterialArtwork } from '../ui/MaterialArtwork';
import { GITHUB, LICENSE, RELEASE_NOTES } from '../lib/links';
import { RELEASE } from '../data/release';
import './performance.css';

export function Performance() {
  return (
    <section id="performance" className="performance section" aria-labelledby="performance-title">
      <div className="site-shell">
        <div className="quality-layout">
          <div className="quality-copy"><h2 id="performance-title" className="section-title">常驻的东西，<br />更值得认真做。</h2><p className="section-intro">空闲时停止动画回调，缓存有明确上限。<br />从渲染到插件生命周期，为长时间陪伴桌面而设计。</p>
            <dl className="quality-stats" aria-label="产品质量与开源信息"><div><dt>空闲动画回调</dt><dd>0<span>帧</span></dd></div><div><dt>{RELEASE.version} 发布回归通过</dt><dd>{RELEASE.tests.toLocaleString('zh-CN')}<span>项</span></dd></div><div><dt>开放源代码</dt><dd>MIT</dd></div></dl>
            <p className="quality-source">测试数据来自 {RELEASE.version} 发行回归，空闲回调为渲染设计约束。</p>
            <div className="quality-links"><a className="text-link" href={GITHUB} target="_blank" rel="noopener noreferrer">看看源代码<ArrowUpRight size={15} aria-hidden="true" /></a><a href={RELEASE_NOTES} target="_blank" rel="noopener noreferrer">发布验收</a><a href={LICENSE} target="_blank" rel="noopener noreferrer">MIT 许可</a></div>
          </div>
          <MaterialArtwork />
        </div>
      </div>
    </section>
  );
}
