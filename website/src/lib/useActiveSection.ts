import { useEffect, useState } from 'react';

const SECTION_IDS = ['experience', 'story', 'appearance', 'plugins', 'performance', 'download'];

export function useActiveSection() {
  const [active, setActive] = useState('experience');

  useEffect(() => {
    const sections = SECTION_IDS.map((id) => document.getElementById(id)).filter(
      (section): section is HTMLElement => Boolean(section),
    );
    if (!sections.length) return;

    const visible = new Map<string, number>();
    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          const id = entry.target.id;
          if (entry.isIntersecting) visible.set(id, entry.intersectionRatio);
          else visible.delete(id);
        });
        if (!visible.size) return;
        const next = [...visible.entries()].sort((a, b) => b[1] - a[1])[0]?.[0];
        if (next) setActive(next === 'story' ? 'experience' : next);
      },
      {
        rootMargin: '-18% 0px -55% 0px',
        threshold: [0.05, 0.2, 0.45, 0.7],
      },
    );

    sections.forEach((section) => observer.observe(section));
    return () => observer.disconnect();
  }, []);

  return active;
}
