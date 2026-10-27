import { useEffect, useRef, useState } from 'react';
import './shader-orb.css';

type ShaderLib = typeof import('shaders/react');

export function ShaderOrb() {
  const hostRef = useRef<HTMLDivElement>(null);
  const [inView, setInView] = useState(false);
  const [lib, setLib] = useState<ShaderLib | null>(null);
  const [status, setStatus] = useState<'idle' | 'ready' | 'unavailable'>('idle');

  useEffect(() => {
    const host = hostRef.current;
    if (!host) return;
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting) {
          setInView(true);
          observer.disconnect();
        }
      },
      { rootMargin: '400px 0px' },
    );
    observer.observe(host);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (!inView || lib) return;
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const narrow = window.matchMedia('(max-width: 900px)').matches;
    const hasGpu = 'gpu' in navigator;
    if (reduced || narrow || !hasGpu) {
      setStatus('unavailable');
      return;
    }
    let cancelled = false;
    import('shaders/react')
      .then((module) => {
        if (cancelled) return;
        if (!module.isWebGPUSupported()) {
          setStatus('unavailable');
          return;
        }
        setLib(module);
      })
      .catch(() => {
        if (!cancelled) setStatus('unavailable');
      });
    return () => {
      cancelled = true;
    };
  }, [inView, lib]);

  if (!lib) {
    return (
      <div ref={hostRef} className="optics-orb optics-orb--fallback" aria-hidden="true">
        <span className="optics-orb__fallback-ring" />
      </div>
    );
  }

  const { Shader, Obsidian } = lib;

  return (
    <div
      ref={hostRef}
      className={`optics-orb ${status === 'ready' ? 'is-ready' : ''}`}
      aria-hidden="true"
    >
      <Shader
        className="optics-orb__canvas"
        onReady={() => setStatus('ready')}
        onUnavailable={() => setStatus('unavailable')}
      >
        <Obsidian
          bodyColor="#08080a"
          iridescence={0.22}
          rimWidth={0.24}
          rimSoftness={0.5}
          flowScale={0.8}
          gloss={0.62}
          bevelWidth={0.1}
          bevelShape={0.2}
          speed={0.12}
        />
      </Shader>
    </div>
  );
}
