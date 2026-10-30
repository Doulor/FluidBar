import { useState } from 'react';
import { CountUp } from '../ui/reactbits';
import { MaterialArtwork } from '../ui/MaterialArtwork';
import { ARCHITECTURE, PERFORMANCE_STATS } from '../data/specs';
import './performance.css';

export function Performance() {
  const [activeLayer, setActiveLayer] = useState(ARCHITECTURE[0].id);
  const active = ARCHITECTURE.find((layer) => layer.id === activeLayer) ?? ARCHITECTURE[0];

  return (
    <section id="performance" className="performance" aria-labelledby="performance-title">
      <div className="site-shell">
        <div className="performance-head">
          <div className="section-heading">
            <p className="section-kicker">04 / PERFORMANCE</p>
            <h2 id="performance-title">
              丝滑不是形容词，
              <br />
              是预算。
            </h2>
          </div>
          <p>
            固定窗口、合成层动画、有界缓存。每一帧都有明确预算，每一个数字都来自实测。
          </p>
        </div>

        <div className="stat-grid" aria-label="实测性能数字">
          {PERFORMANCE_STATS.map((stat) => (
            <div className="stat" key={stat.label}>
              <div className="stat__value">
                <CountUp
                  to={stat.value}
                  decimals={stat.decimals ?? 0}
                  duration={1.5}
                  easing="easeOut"
                  trigger="view"
                />
                <span className="stat__suffix">{stat.suffix}</span>
              </div>
              <div className="stat__label">{stat.label}</div>
              <div className="stat__note">{stat.note}</div>
            </div>
          ))}
        </div>

        <div className="architecture">
          <MaterialArtwork />

          <div className="architecture-copy">
            <p className="section-kicker">LAYERS</p>
            <h3>从窗口到插件，只有一条链路。</h3>
            <div className="layer-list" role="group" aria-label="架构层">
              {ARCHITECTURE.map((layer, index) => (
                <button
                  key={layer.id}
                  type="button"
                  className={`layer-row ${activeLayer === layer.id ? 'is-active' : ''}`}
                  aria-pressed={activeLayer === layer.id}
                  onMouseEnter={() => setActiveLayer(layer.id)}
                  onFocus={() => setActiveLayer(layer.id)}
                  onClick={() => setActiveLayer(layer.id)}
                >
                  <span className="layer-row__index">0{index + 1}</span>
                  <span className="layer-row__name">{layer.name}</span>
                  <span className="layer-row__short">{layer.short}</span>
                </button>
              ))}
            </div>
            <p className="layer-detail" aria-live="polite">
              {active.detail}
            </p>
            <p className="source-note">
              数据来源：docs/rewrite/STATUS.md 与 v2 发布说明。测量环境为 Windows 11、150% DPI，
              数字随版本和硬件变化。
            </p>
          </div>
        </div>
      </div>
    </section>
  );
}
