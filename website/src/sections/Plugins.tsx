import { useState } from 'react';
import {
  ArrowUpRight,
  Bell,
  ClipboardText,
  Devices,
  MusicNotes,
  Robot,
  SquaresFour,
} from '@phosphor-icons/react';
import { BorderGlow, SpotlightCard } from '../ui/reactbits';
import { MANIFEST_SAMPLE, PLUGINS, type PluginItem } from '../data/plugins';
import { PLUGIN_DOCS } from '../lib/links';
import './plugins.css';

const ICONS = {
  media: MusicNotes,
  notifications: Bell,
  clipboard: ClipboardText,
  agent: Robot,
  devices: Devices,
  widgets: SquaresFour,
};

function PluginIcon({ icon }: { icon: PluginItem['icon'] }) {
  const Icon = ICONS[icon];
  return <Icon size={19} weight="regular" aria-hidden="true" />;
}

export function Plugins() {
  const [selected, setSelected] = useState<PluginItem>(PLUGINS[0]);
  const [showManifest, setShowManifest] = useState(false);

  return (
    <section id="plugins" className="plugins" aria-labelledby="plugins-title">
      <div className="site-shell">
        <div className="section-heading plugins-heading">
          <p className="section-kicker">03 / PLUGINS</p>
          <h2 id="plugins-title">
            小岛不大，
            <br />
            装得下生态。
          </h2>
          <p>
            内置插件开箱可用，社区插件通过签名包和声明式页面接入。安装、更新与回滚都不必离开 FluidBar。
          </p>
        </div>

        <div className="plugins-grid">
          <div className="plugins-left">
            <SpotlightCard
              className="sdk-card"
              theme="light"
              spotlightColor="rgba(36, 71, 255, 0.16)"
              intensity={0.3}
              spotlightSize={300}
              grain={0.08}
            >
              <div className="sdk-card__top">
                <span className="mono">PLUGIN SDK</span>
                <span className="sdk-card__tag">声明式 UI</span>
              </div>
              <h3>只写能力，不写 XAML。</h3>
              <p>布局、设置页、命令和小组件由宿主渲染。插件不再决定岛长什么样。</p>
            </SpotlightCard>

            <div className="plugin-list" aria-label="内置插件">
              {PLUGINS.map((plugin) => (
                <button
                  key={plugin.id}
                  type="button"
                  className={`plugin-row ${selected.id === plugin.id ? 'is-selected' : ''}`}
                  aria-pressed={selected.id === plugin.id}
                  onClick={() => setSelected(plugin)}
                >
                  <span className="plugin-row__icon">
                    <PluginIcon icon={plugin.icon} />
                  </span>
                  <span className="plugin-row__copy">
                    <strong>{plugin.label}</strong>
                    <span>{plugin.summary}</span>
                  </span>
                  <span className="plugin-row__caps">
                    {plugin.capabilities[0]}
                  </span>
                </button>
              ))}
            </div>

            <a className="plugins-link" href={PLUGIN_DOCS} target="_blank" rel="noopener noreferrer">
              阅读插件文档
              <ArrowUpRight size={15} aria-hidden="true" />
            </a>
          </div>

          <div className="plugins-code">
            <div className="code-toolbar">
              <div className="code-dots" aria-hidden="true">
                <span />
                <span />
                <span />
              </div>
              <span className="mono">{showManifest ? 'manifest.json' : 'capabilities.json'}</span>
              <button
                type="button"
                className="code-switch"
                onClick={() => setShowManifest((value) => !value)}
              >
                {showManifest ? '看能力声明' : '看完整 manifest'}
              </button>
            </div>

            <BorderGlow
              className="code-glow"
              glowColor="220 100% 62%"
              backgroundColor="#0b0d10"
              borderRadius={24}
              glowRadius={32}
              glowIntensity={0.65}
              edgeSensitivity={24}
              coneSpread={28}
              colors={['#2447ff', '#5ad7f2', '#ffffff']}
              fillOpacity={0.4}
            >
              <div className="code-panel">
                <div className="code-panel__head">
                  <span>{showManifest ? '一个可运行的最小插件' : selected.label}</span>
                  <span className="code-panel__caps">
                    {showManifest ? 'schemaVersion 2' : selected.capabilities.join(' · ')}
                  </span>
                </div>
                <pre>
                  <code>{showManifest ? MANIFEST_SAMPLE : `{\n  ${selected.code}\n}`}</code>
                </pre>
                <div className="code-panel__foot">
                  <span>签名 .fbp</span>
                  <span>应用内市场</span>
                  <span>安装 / 更新 / 回滚</span>
                </div>
              </div>
            </BorderGlow>
          </div>
        </div>
      </div>
    </section>
  );
}
