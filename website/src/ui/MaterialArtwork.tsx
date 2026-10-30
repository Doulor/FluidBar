import { useRef, type PointerEvent } from 'react';
import { ShaderOrb } from './ShaderOrb';
import './material-artwork.css';

/** The original material artwork, restored beside the architecture narrative. */
export function MaterialArtwork() {
  const stageRef = useRef<HTMLDivElement>(null);

  const moveLight = (event: PointerEvent<HTMLDivElement>) => {
    if (event.pointerType !== 'mouse' || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const stage = stageRef.current;
    if (!stage) return;
    const rect = stage.getBoundingClientRect();
    stage.style.setProperty('--mx', `${((event.clientX - rect.left) / rect.width) * 100}%`);
    stage.style.setProperty('--my', `${((event.clientY - rect.top) / rect.height) * 100}%`);
  };

  const resetLight = () => {
    stageRef.current?.style.removeProperty('--mx');
    stageRef.current?.style.removeProperty('--my');
  };

  return (
    <div
      ref={stageRef}
      className="material-artwork"
      onPointerMove={moveLight}
      onPointerLeave={resetLight}
      aria-hidden="true"
    >
      <div className="material-artwork__wordmark">FLUID<br />BAR</div>
      <div className="material-artwork__horizon" />
      <div className="material-artwork__glass">
        <span className="material-artwork__sheen" />
        <span className="material-artwork__label mono">LIQUID GLASS · 视觉近似</span>
        <span className="material-artwork__pill" />
      </div>
      <div className="material-artwork__obsidian">
        <span className="material-artwork__edge" />
        <span className="mono">OBSIDIAN</span>
      </div>
      <ShaderOrb />
      <div className="material-artwork__rule material-artwork__rule--h" />
      <div className="material-artwork__rule material-artwork__rule--v" />
    </div>
  );
}
