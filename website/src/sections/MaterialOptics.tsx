import { useRef, type PointerEvent } from 'react';
import { ShaderOrb } from '../ui/ShaderOrb';
import './material-optics.css';

export function MaterialOptics() {
  const stageRef = useRef<HTMLDivElement>(null);
  const glassRef = useRef<HTMLDivElement>(null);

  const handlePointerMove = (event: PointerEvent<HTMLDivElement>) => {
    const stage = stageRef.current;
    if (!stage) return;
    const rect = stage.getBoundingClientRect();
    const x = ((event.clientX - rect.left) / rect.width) * 100;
    const y = ((event.clientY - rect.top) / rect.height) * 100;
    stage.style.setProperty('--mx', `${x}%`);
    stage.style.setProperty('--my', `${y}%`);

    const glass = glassRef.current;
    if (!glass) return;
    const glassRect = glass.getBoundingClientRect();
    glass.style.setProperty('--gx', `${((event.clientX - glassRect.left) / glassRect.width) * 100}%`);
    glass.style.setProperty('--gy', `${((event.clientY - glassRect.top) / glassRect.height) * 100}%`);
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
            同一枚岛的三种材质，在真实原型里由 Skia 绘制；这里是一块放大的材质研究。液态玻璃不是白色磨砂层：
            它先透出背后的桌面，再由透镜边缘、顶部高光和底部焦散把表面立起来。
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
          aria-label="材质研究：液态玻璃面板与黑曜石样本"
        >
          <div className="optics-wordmark" aria-hidden="true">
            FLUID
            <br />
            BAR
          </div>
          <div className="optics-horizon" aria-hidden="true" />

          <div ref={glassRef} className="optics-glass">
            <span className="optics-glass__sheen" aria-hidden="true" />
            <span className="optics-glass__glow" aria-hidden="true" />
            <span className="optics-glass__edge" aria-hidden="true" />
            <span className="optics-glass__caustic" aria-hidden="true" />
            <span className="optics-glass__label mono">LIQUID GLASS · OPTICAL LAYERS</span>
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
