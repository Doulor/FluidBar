import { useEffect, useRef, useState } from 'react';
import { ArrowDown, GithubLogo, List, X } from '@phosphor-icons/react';
import { BrandMark } from '../ui/BrandMark';
import { useActiveSection } from '../lib/useActiveSection';
import { GITHUB } from '../lib/links';
import { SITE_SECTIONS } from '../lib/sections';
import './site-nav.css';

export function SiteNav() {
  const active = useActiveSection();
  const [open, setOpen] = useState(false);
  const toggleRef = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (!open) return;
    const key = (event: KeyboardEvent) => { if (event.key === 'Escape') { setOpen(false); toggleRef.current?.focus(); } };
    const media = window.matchMedia('(min-width: 961px)');
    const close = () => { if (media.matches) setOpen(false); };
    document.addEventListener('keydown', key);
    media.addEventListener('change', close);
    return () => { document.removeEventListener('keydown', key); media.removeEventListener('change', close); };
  }, [open]);
  return (
    <header className="site-header">
      <div className="site-shell nav-inner">
        <a className="brand" href="#experience" aria-label="FluidBar 首页"><BrandMark size={30} /><span>FluidBar</span></a>
        <nav className="nav-desktop" aria-label="主导航">
          {SITE_SECTIONS.map(item => <a key={item.id} href={item.href} aria-current={active === item.id ? 'page' : undefined}>{item.label}</a>)}
        </nav>
        <div className="nav-actions">
          <a className="nav-github" href={GITHUB} target="_blank" rel="noopener noreferrer" aria-label="在 GitHub 查看 FluidBar"><GithubLogo size={21} aria-hidden="true" /></a>
          <a className="nav-cta" href="#download">下载<ArrowDown size={14} aria-hidden="true" /></a>
          <button ref={toggleRef} className="nav-menu-button" type="button" aria-expanded={open} aria-controls={open ? 'mobile-nav' : undefined} aria-label={open ? '关闭导航' : '打开导航'} onClick={() => setOpen(value => !value)}>{open ? <X size={22} aria-hidden="true" /> : <List size={22} aria-hidden="true" />}</button>
        </div>
      </div>
      {open && <nav id="mobile-nav" className="mobile-nav site-shell" aria-label="移动导航">
        {SITE_SECTIONS.map(item => <a key={item.id} href={item.href} aria-current={active === item.id ? 'page' : undefined} onClick={() => setOpen(false)}>{item.label}<ArrowDown size={16} aria-hidden="true" /></a>)}
        <a href={GITHUB} target="_blank" rel="noopener noreferrer">GitHub<GithubLogo size={18} aria-hidden="true" /></a>
      </nav>}
    </header>
  );
}
