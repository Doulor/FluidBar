import { useRef, type PointerEvent } from 'react';
import { ShaderOrb } from '../ui/ShaderOrb';
import './material-optics.css';

export function MaterialOptics() {
  const stageRef = useRef<HTMLDivElement>(null);

  const handlePointerMove = (event: PointerEvent<HTMLDivElement>) => {
    const stage = stageRef.current;
    if (!stage) return;
    const rect = stage.getBoundingClientRect();
    const x = ((event.clientX - rect.left) / rect.width) * 100;
    const y = ((event.clientY - rect.top) / rect.height) * 100;
    stage.style.setProperty('--mx', `${x}%`);
    stage.style.setProperty('--my', `${y}%`);
  };

  return (
    <section className="optics" aria-labelledby="optics-title">
      <div className="site-shell optics-grid">
        <div className="optics-copy">
          <p className="section-kicker">MATERIAL STUDY</p>
          <h2 id="optics-title">
            黑曜石，不只是黑。
            <br />
            玻璃，也不只是透明。
          </h2>
          <p>
            同一枚岛的三种材质，在真实原型里由 Skia 绘制；这里是一块放大的材质研究，用光、边缘和阴影看它们如何工作。
          </p>
          <div className="optics-legend">
            <span>
              <i className="legend-dot legend-dot--obsidian" aria-hidden="true" />
              黑曜石
            </span>
            <span>
              <i className="legend-dot legend-dot--acrylic" aria-hidden="true" />
              亚克力
            </span>
            <span>
              <i className="legend-dot legend-dot--liquid" aria-hidden="true" />
              液态玻璃
            </span>
          </div>
        </div>

        <div
          ref={stageRef}
          className="optics-stage"
          onPointerMove={handlePointerMove}
          aria-label="材质研究：放大的玻璃面板与黑曜石样本"
        >
          <div className="optics-wordmark" aria-hidden="true">
            FLUID
            <br />
            BAR
          </div>
          <div className="optics-horizon" aria-hidden="true" />

          <div className="optics-glass">
            <span className="optics-glass__sheen" aria-hidden="true" />
            <span className="optics-glass__label mono">LIQUID GLASS · 视觉近似</span>
            <span className="optics-glass__pill" aria-hidden="true" />
          </div>

          <div className="optics-obsidian">
            <span className="optics-obsidian__edge" aria-hidden="true" />
            <span className="mono">OBSIDIAN</span>
          </div>

          <ShaderOrb />

          <div className="optics-rule optics-rule--h" aria-hidden="true" />
          <div className="optics-rule optics-rule--v" aria-hidden="true" />
        </div>
      </div>
    </section>
  );
}
