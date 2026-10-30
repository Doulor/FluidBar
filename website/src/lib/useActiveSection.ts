import { useEffect, useState } from 'react';
import { SITE_SECTIONS } from './sections';

export function useActiveSection() {
  const [active, setActive] = useState<string>('experience');

  useEffect(() => {
    const sections = SITE_SECTIONS.map(item => document.getElementById(item.id)).filter(
      (section): section is HTMLElement => Boolean(section),
    );
    let frame = 0;
    const update = () => {
      frame = 0;
      const offset = parseFloat(getComputedStyle(document.documentElement).scrollPaddingTop) || 96;
      let current = sections[0]?.id ?? 'experience';
      for (const section of sections) {
        if (section.getBoundingClientRect().top <= offset + 2) current = section.id;
      }
      if (scrollY > 0 && scrollY + innerHeight >= document.documentElement.scrollHeight - 2) {
        current = sections.at(-1)?.id ?? current;
      }
      setActive(current);
    };
    const schedule = () => { if (!frame) frame = requestAnimationFrame(update); };
    // Vite renders the chapter anchors after the browser's initial fragment
    // lookup. Align once after mounting, and again when web fonts have settled.
    const initialHash = window.location.hash;
    let disposed = false;
    const alignInitialHash = () => {
      if (disposed || !initialHash || location.hash !== initialHash) return;
      let id: string;
      try { id = decodeURIComponent(initialHash.slice(1)); } catch { return; }
      const target = document.getElementById(id);
      if (target && document.querySelector('main')?.contains(target)) {
        target.scrollIntoView({ behavior: 'instant', block: 'start' });
        schedule();
      }
    };
    const initialFrame = requestAnimationFrame(alignInitialHash);
    void document.fonts.ready.then(alignInitialHash);
    const observer = new ResizeObserver(schedule);
    observer.observe(document.body);
    window.addEventListener('scroll', schedule, { passive: true });
    window.addEventListener('resize', schedule);
    window.addEventListener('hashchange', schedule);
    update();
    return () => {
      disposed = true;
      observer.disconnect();
      cancelAnimationFrame(frame);
      cancelAnimationFrame(initialFrame);
      window.removeEventListener('scroll', schedule);
      window.removeEventListener('resize', schedule);
      window.removeEventListener('hashchange', schedule);
    };
  }, []);
  return active;
}
