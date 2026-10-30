import './material-optics.css';

const MATERIALS = [
  { id: 'obsidian', name: '黑曜石', detail: '深色实底，优先保证内容对比。' },
  { id: 'acrylic', name: '亚克力', detail: '柔化背景，让桌面颜色轻微透入。' },
  { id: 'liquid', name: '液态玻璃', detail: '更通透的底色，配合明亮的边缘。' },
];

export function MaterialOptics() {
  return (
    <section className="optics" aria-labelledby="optics-title">
      <div className="site-shell optics-grid">
        <div className="optics-copy">
          <p className="section-kicker">MATERIAL STUDY</p>
          <h2 id="optics-title">材质退后，内容在前。</h2>
          <p>
            三种材质保留相同的内容层级。封面和歌词是媒体页的重点，系统控件保持低调。
          </p>
          <p className="optics-note">
            材质样本使用 CSS 近似。完整交互可在上方“材质”选择器中比较；软件中的实际效果由 Skia 绘制。
          </p>
          <a className="text-link" href="#appearance">在小岛中比较材质 ↑</a>
        </div>
        <div className="optics-samples" aria-label="CSS 材质样本">
          {MATERIALS.map((material) => (
            <figure className="optics-sample" key={material.id}>
              <div className="optics-swatch" aria-hidden="true">
                <span className={`optics-swatch__surface optics-swatch__surface--${material.id}`} />
              </div>
              <figcaption>
                <strong>{material.name}</strong>
                <span>{material.detail}</span>
              </figcaption>
            </figure>
          ))}
        </div>
      </div>
    </section>
  );
}
