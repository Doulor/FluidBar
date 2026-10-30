import { useEffect, useRef, useState } from 'react';
import { GithubLogo, List, X } from '@phosphor-icons/react';
import { PillNav } from '../ui/reactbits';
import { BrandMark } from '../ui/BrandMark';
import { useActiveSection } from '../lib/useActiveSection';
import { GITHUB } from '../lib/links';
import './site-nav.css';

const ITEMS = [
  { label: '体验', href: '#experience' },
  { label: '外观', href: '#appearance' },
  { label: '插件', href: '#plugins' },
  { label: '下载', href: '#download' },
];

export function SiteNav() {
  const active = useActiveSection();
  const [scrolled, setScrolled] = useState(false);
  const [open, setOpen] = useState(false);
  const sentinelRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const sentinel = sentinelRef.current;
    if (!sentinel) return;
    const observer = new IntersectionObserver(([entry]) => setScrolled(!entry.isIntersecting), {
      threshold: 0,
    });
    observer.observe(sentinel);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (!open) return;
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false);
    };
    document.addEventListener('keydown', handleKey);
    document.body.classList.add('nav-open');
    return () => {
      document.removeEventListener('keydown', handleKey);
      document.body.classList.remove('nav-open');
    };
  }, [open]);

  return (
    <>
      <div ref={sentinelRef} className="top-sentinel" aria-hidden="true" />
      <header className={`site-header ${scrolled ? 'is-scrolled' : ''}`}>
        <div className="site-shell nav-inner">
          <a className="brand" href="#experience">
            <BrandMark size={30} />
            <span className="brand-name">
              FluidBar
              <sup>PREVIEW</sup>
            </span>
          </a>

          <div className="nav-desktop">
            <PillNav
              items={ITEMS}
              activeHref={`#${active === 'story' ? 'experience' : active}`}
              theme="light"
              size="sm"
              layout="split"
              surface="glass"
              itemStyle="pill"
              hoverEffect="fill"
              indicator="dot"
              intro={false}
              collapse="never"
              ariaLabel="主导航"
            />
          </div>

          <div className="nav-actions">
            <a
              className="nav-icon-link"
              href={GITHUB}
              target="_blank"
              rel="noopener noreferrer"
              aria-label="在 GitHub 查看 FluidBar"
            >
              <GithubLogo size={19} weight="regular" aria-hidden="true" />
            </a>
            <a className="nav-cta" href="#download">
              下载预览版
            </a>
            <button
              className="nav-menu-button"
              type="button"
              aria-expanded={open}
              aria-controls="mobile-nav"
              aria-label={open ? '关闭导航' : '打开导航'}
              onClick={() => setOpen((value) => !value)}
            >
              {open ? <X size={22} /> : <List size={22} />}
            </button>
          </div>
        </div>

        {open ? (
          <div className="mobile-nav-wrap">
            <nav id="mobile-nav" className="site-shell mobile-nav" aria-label="移动导航">
              {ITEMS.map((item) => (
                <a key={item.href} href={item.href} onClick={() => setOpen(false)}>
                  {item.label}
                </a>
              ))}
              <a href={GITHUB} target="_blank" rel="noopener noreferrer">
                GitHub
              </a>
              <a className="nav-cta" href="#download" onClick={() => setOpen(false)}>
                下载预览版
              </a>
            </nav>
          </div>
        ) : null}
      </header>
    </>
  );
}
