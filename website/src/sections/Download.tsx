import { ArrowUpRight, GithubLogo } from '@phosphor-icons/react';
import { BrandMark } from '../ui/BrandMark';
import { GITHUB, RELEASE_NOTES, RELEASES } from '../lib/links';
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

          <div className="download-copy" data-ghost="FLUIDBAR">
            <p className="section-kicker">05 / DOWNLOAD</p>
            <h2 id="download-title">
              现在，
              <br />
              把岛带走。
            </h2>
            <p>
              Windows 10 / 11 x64。免费、开源，当前为开发预览版，功能和材质仍在收紧。
            </p>
          </div>

          <div className="download-actions">
            <a className="download-primary" href={RELEASES} target="_blank" rel="noopener noreferrer">
              下载预览版
              <ArrowUpRight size={17} aria-hidden="true" />
            </a>
            <a className="download-secondary" href={GITHUB} target="_blank" rel="noopener noreferrer">
              <GithubLogo size={16} aria-hidden="true" />
              查看源代码
            </a>
            <a className="download-link" href={RELEASE_NOTES} target="_blank" rel="noopener noreferrer">
              阅读本次发布说明
            </a>
          </div>

          <div className="download-meta">
            <span>
              <strong>系统</strong> Windows 10 / 11 x64
            </span>
            <span>
              <strong>运行时</strong> .NET 10 Desktop Runtime
            </span>
            <span>
              <strong>许可</strong> MIT
            </span>
          </div>
        </div>
      </div>
    </section>
  );
}
