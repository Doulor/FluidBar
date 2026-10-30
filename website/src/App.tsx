import { SiteNav } from './sections/SiteNav';
import { Hero } from './sections/Hero';
import { IslandLab } from './sections/IslandLab';
import { MaterialOptics } from './sections/MaterialOptics';
import { Plugins } from './sections/Plugins';
import { Performance } from './sections/Performance';
import { Download } from './sections/Download';
import { SiteFooter } from './sections/SiteFooter';

export default function App() {
  return (
    <div className="site">
      <a className="skip-link" href="#main">
        跳到主要内容
      </a>
      <SiteNav />
      <main id="main">
        <Hero />
        <IslandLab />
        <MaterialOptics />
        <Plugins />
        <Performance />
        <Download />
      </main>
      <SiteFooter />
    </div>
  );
}
