import { useRef, useState, type PointerEvent } from 'react';
import { ArrowUpRight } from '@phosphor-icons/react';
import './material-optics.css';

const MATERIALS = [
  {
    id: 'obsidian',
    name: '黑曜石',
    english: 'Obsidian',
    detail: '深色实底，以细腻的边缘反光勾出轮廓。',
    property: '深色实底',
  },
  {
    id: 'acrylic',
    name: '亚克力',
    english: 'Acrylic',
    detail: '柔化桌面，让背景的色彩透过一层薄雾。',
    property: '柔和雾面',
  },
  {
    id: 'liquid',
    name: '液态玻璃',
    english: 'Liquid glass',
    detail: '保留背景的明暗，用通透的表面和亮边呈现厚度。',
    property: '通透亮边',
  },
] as const;

type Material = (typeof MATERIALS)[number]['id'];

export function MaterialOptics() {
  const [selected, setSelected] = useState<Material>('liquid');
  const stageRef = useRef<HTMLDivElement>(null);
  const active = MATERIALS.find(material => material.id === selected)!;

  const moveLight = (event: PointerEvent<HTMLDivElement>) => {
    if (event.pointerType !== 'mouse' || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const stage = stageRef.current;
    if (!stage) return;
    const rect = stage.getBoundingClientRect();
    stage.style.setProperty('--light-x', `${((event.clientX - rect.left) / rect.width) * 100}%`);
    stage.style.setProperty('--light-y', `${((event.clientY - rect.top) / rect.height) * 100}%`);
  };

  const resetLight = () => {
    stageRef.current?.style.removeProperty('--light-x');
    stageRef.current?.style.removeProperty('--light-y');
  };

  return (
    <section className="optics" aria-labelledby="optics-title">
      <div className="site-shell optics-grid">
        <div className="optics-copy">
          <p className="section-kicker">MATERIAL STUDY</p>
          <h2 id="optics-title">深色有层次，<br />透明有边界。</h2>
          <p className="optics-intro">
            黑曜石的深、亚克力的柔、液态玻璃的透。三种表面，让同一枚岛与桌面自然相融。
          </p>
          <div className="optics-properties" aria-label="三种材质特性">
            {MATERIALS.map(material => (
              <div key={material.id}>
                <span className={`optics-material-dot optics-material-dot--${material.id}`} aria-hidden="true" />
                <span>{material.name}</span>
                <span>{material.property}</span>
              </div>
            ))}
          </div>
          <a className="text-link optics-link" href="#appearance">
            在真实小岛中比较
            <ArrowUpRight size={14} aria-hidden="true" />
          </a>
          <p className="optics-note">此处为 CSS 材质研究，软件中的实际效果由 Skia 绘制。</p>
        </div>

        <figure className="optics-figure">
          <div
            ref={stageRef}
            className="optics-stage"
            data-study-material={selected}
            onPointerMove={moveLight}
            onPointerLeave={resetLight}
          >
            <div className="optics-stage-heading" aria-hidden="true">
              <span>FluidBar <span className="optics-stage-heading__divider">/</span> Surfaces</span>
              <span>01 — 03</span>
            </div>

            <div className="optics-scene" role="img" aria-label="银白画布上的三种材质：深色黑曜石、雾面亚克力与透出背景的液态玻璃叠层">
              <div className="optics-backdrop" aria-hidden="true">
                <span className="optics-backdrop__ribs" />
                <span className="optics-backdrop__band" />
                <span className="optics-backdrop__line" />
              </div>
              {MATERIALS.map((material, index) => (
                <div key={material.id} className={`optics-plate optics-plate--${material.id}`} aria-hidden="true">
                  <span className="optics-plate__edge" />
                  <span className="optics-plate__reflection" />
                  <span className="optics-plate__identity">
                    <span className="optics-plate__number">0{index + 1}</span>
                    <strong>{material.name}</strong>
                    <span className="optics-plate__english">{material.english}</span>
                  </span>
                  {material.id === 'liquid' ? <span className="optics-plate__engraving">FluidBar</span> : null}
                </div>
              ))}
            </div>

            <div className="optics-study-controls" role="group" aria-label="材质研究样本">
              {MATERIALS.map((material, index) => (
                <button
                  key={material.id}
                  type="button"
                  aria-pressed={selected === material.id}
                  onClick={() => setSelected(material.id)}
                >
                  <span className="optics-study-controls__number">0{index + 1}</span>
                  {material.name}
                  <span className="sr-only">材质研究</span>
                  <span className="optics-study-controls__indicator" aria-hidden="true" />
                </button>
              ))}
            </div>
          </div>
          <figcaption className="optics-caption" aria-live="polite" aria-atomic="true">
            <span className="optics-caption__name">{active.english}</span>
            <span>{active.detail}</span>
          </figcaption>
        </figure>
      </div>
    </section>
  );
}
