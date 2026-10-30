import { useState } from 'react';
import { ArrowUpRight } from '@phosphor-icons/react';
import { PrototypeFrame } from '../island/PrototypeFrame';
import { usePrototype } from '../island/usePrototype';
import type { IslandMaterial, IslandMode } from '../island/protocol';
import { MaterialOptics } from './MaterialOptics';
import './island-lab.css';

export function IslandLab() {
  const { frameRef, state, setState, send } = usePrototype();
  const [mode, setMode] = useState<IslandMode>('float');
  const [material, setMaterial] = useState<IslandMaterial>('obsidian');
  return (
    <section id="appearance" className="lab chapter" aria-labelledby="lab-title">
      <div className="site-shell">
        <header className="chapter-heading">
          <div><p className="section-kicker">02 / APPEARANCE</p><h2 id="lab-title">形态与材质，<br />在同一处比较。</h2></div>
          <p>选择停靠方式，再展开比较三种材质。下方的材质研究解释表面的差异。</p>
        </header>
        <div className="lab-grid">
          <div className="lab-column">
            <div
              className="config"
            >
              <h3 className="config-title">实时预览配置</h3>

              <div className="control-group">
                <span className="control-label">形态</span>
                <div className="segmented" role="group" aria-label="岛形态">
                  {[
                    ['float', '浮岛'],
                    ['flush', '贴顶'],
                    ['dock', 'Dock'],
                  ].map(([value, label]) => (
                    <button
                      key={value}
                      type="button"
                      className={mode === value ? 'is-selected' : ''}
                      aria-pressed={mode === value}
                      onClick={() => {
                        frameRef.current?.load();
                        setMode(value as IslandMode);
                        send({ mode: value as IslandMode });
                      }}
                    >
                      {label}
                    </button>
                  ))}
                </div>
              </div>

              <div className="control-group">
                <span className="control-label">材质</span>
                <div className="segmented" role="group" aria-label="岛材质">
                  {[
                    ['obsidian', '黑曜石'],
                    ['acrylic', '亚克力'],
                    ['liquid', '液态玻璃'],
                  ].map(([value, label]) => (
                    <button
                      key={value}
                      type="button"
                      className={material === value ? 'is-selected' : ''}
                      aria-pressed={material === value}
                      onClick={() => {
                        frameRef.current?.load();
                        setMaterial(value as IslandMaterial);
                        send({
                          material: value as IslandMaterial,
                          state: 'expanded',
                          page: 0,
                        });
                      }}
                    >
                      {label}
                    </button>
                  ))}
                </div>
              </div>

              <p className="config-note">
                切换材质时展开 440×284 的预览。网页为 CSS 视觉近似，软件中的实际效果由 Skia 绘制。
              </p>
              <a className="stage-link config-study-link" href="#material-study">查看下方材质研究 ↓</a>
            </div>
          </div>
          <div className="lab-stage">
            <div className="stage-status" aria-live="polite">
              <span className="stage-status__live"><span className="live-dot" aria-hidden="true" />LIVE · 真实预览</span>
              <span className="mono">{!state.ready ? '载入真实原型…' : state.expanded ? '已展开 · 440 × 284' : `已收起 · ${state.width} × ${state.height}`}</span>
            </div>
            <PrototypeFrame ref={frameRef} title="FluidBar 真实灵动岛原型" name="fluidbar-lab"
              poster={`${import.meta.env.BASE_URL}posters/lab-expanded-daylight.png`}
              startState="expanded" lazy onState={setState} />
            <div className="lab-stage__foot"><span className="mono">示例数据</span><a href={`${import.meta.env.BASE_URL}island/embed.html`} target="_blank" rel="noreferrer" className="stage-link">打开独立演示<ArrowUpRight size={12} aria-hidden="true" /></a></div>
          </div>
        </div>
        <MaterialOptics />
      </div>
    </section>
  );
}
