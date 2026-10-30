import { useEffect, useRef, useState } from 'react';
import { ArrowUpRight } from '@phosphor-icons/react';
import { PrototypeFrame } from '../island/PrototypeFrame';
import { usePrototype } from '../island/usePrototype';
import { COMPACT_STATE_META } from '../island/compactStates';
import type { CompactView, IslandMaterial, IslandMode } from '../island/protocol';
import './island-lab.css';

const PAGE_LABELS = ['主页', '媒体', '通知', '剪贴板', 'Agent', '小组件'];
const ACTIVITY_VIEWS: CompactView[] = ['media', 'notifications', 'clipboard', 'agent'];

const STEPS = [
  {
    id: 'rest',
    title: '常驻',
    copy: '132 × 36，留下时间和日期。',
  },
  {
    id: 'activity',
    title: '活动态',
    copy: '有新活动时，显示封面、歌词或消息摘要。',
  },
  {
    id: 'expand',
    title: '展开',
    copy: '点击，变成 440 × 284 的控制中心。',
  },
  {
    id: 'switch',
    title: '切换',
    copy: '六页内容，支持拖动、滚轮和数字键 1–6。',
  },
  {
    id: 'collapse',
    title: '退场',
    copy: '操作完，安静收起。',
  },
];

export function IslandLab() {
  const { frameRef, state, setState, send } = usePrototype();
  const [activeStep, setActiveStep] = useState(0);
  const configRef = useRef<HTMLDivElement>(null);
  const [activity, setActivity] = useState<CompactView>('media');
  const [page, setPage] = useState(0);
  const [mode, setMode] = useState<IslandMode>('float');
  const [material, setMaterial] = useState<IslandMaterial>('obsidian');
  const stepRefs = useRef<Array<HTMLDivElement | null>>([]);
  const manualUntilRef = useRef(0);

  useEffect(() => {
    const nodes = stepRefs.current.filter((node): node is HTMLDivElement => Boolean(node));
    if (!nodes.length) return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (performance.now() < manualUntilRef.current) return;
        const visible = entries
          .filter((entry) => entry.isIntersecting)
          .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top);
        if (!visible.length) return;
        const index = nodes.indexOf(visible[0].target as HTMLDivElement);
        if (index >= 0) setActiveStep(index);
      },
      { rootMargin: '-30% 0px -50% 0px', threshold: 0 },
    );
    nodes.forEach((node) => observer.observe(node));
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (activeStep === 0) send({ state: 'idle' });
    if (activeStep === 1) send({ state: activity });
    if (activeStep === 2) send({ state: 'expanded', page: 0 });
    if (activeStep === 3) send({ state: 'expanded', page });
    if (activeStep === 4) send({ state: 'idle' });
  }, [activeStep, activity, page, send]);

  useEffect(() => {
    const config = configRef.current;
    if (!config) return;
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (entry.isIntersecting && performance.now() >= manualUntilRef.current) setActiveStep(-1);
      },
      { rootMargin: '-18% 0px -45% 0px', threshold: 0.1 },
    );
    observer.observe(config);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    send({ mode });
  }, [mode, send]);

  useEffect(() => {
    send({ material });
  }, [material, send]);

  const chooseStep = (index: number) => {
    frameRef.current?.load();
    manualUntilRef.current = performance.now() + 1600;
    setActiveStep(index);
    if (index === 0) send({ state: 'idle' });
    if (index === 1) send({ state: activity });
    if (index === 2) send({ state: 'expanded', page: 0 });
    if (index === 3) send({ state: 'expanded', page });
    if (index === 4) send({ state: 'idle' });
  };

  const chooseActivity = (view: CompactView) => {
    frameRef.current?.load();
    manualUntilRef.current = performance.now() + 1600;
    setActivity(view);
    setActiveStep(1);
    send({ state: view });
  };

  const choosePage = (index: number) => {
    frameRef.current?.load();
    manualUntilRef.current = performance.now() + 1600;
    setPage(index);
    setActiveStep(3);
    send({ state: 'expanded', page: index });
  };

  const compact = COMPACT_STATE_META.find((item) => item.id === state.view) ?? COMPACT_STATE_META[0];
  const status = !state.ready
    ? '载入真实原型…'
    : state.expanded
      ? `已展开 · ${PAGE_LABELS[state.page] ?? '主页'} · 440 × 284`
      : `${compact.label} · ${state.width} × ${state.height}`;
  const poster =
    state.expanded
      ? `${import.meta.env.BASE_URL}posters/lab-expanded.png`
      : `${import.meta.env.BASE_URL}posters/hero-idle.png`;

  return (
    <section id="story" className="lab" aria-labelledby="lab-title">
      <div className="site-shell">
        <div className="section-heading lab-heading">
          <p className="section-kicker">01 / THE ISLAND</p>
          <h2 id="lab-title">
            从一眼即知，
            <br />
            到展开处理。
          </h2>
          <p>
            收起时看活动摘要，展开后看完整内容。选择状态，或直接操作预览中的小岛。
          </p>
        </div>

        <div className="lab-grid">
          <div className="lab-column">
            <div className="lab-steps">
              {STEPS.map((step, index) => (
                <div
                  key={step.id}
                  ref={(node) => {
                    stepRefs.current[index] = node;
                  }}
                  className={`lab-step ${activeStep === index ? 'is-active' : ''}`}
                >
                  <button
                    type="button"
                    className="lab-step__head"
                    aria-current={activeStep === index ? 'step' : undefined}
                    onClick={() => chooseStep(index)}
                  >
                    <span className="lab-step__index">0{index + 1}</span>
                    <span className="lab-step__body">
                      <strong>{step.title}</strong>
                      <span>{step.copy}</span>
                    </span>
                  </button>

                  {index === 1 ? (
                    <div className="step-options" aria-label="真实活动态">
                      {ACTIVITY_VIEWS.map((view) => {
                        const meta = COMPACT_STATE_META.find((item) => item.id === view)!;
                        return (
                          <button
                            key={view}
                            type="button"
                            className={`option-chip ${activity === view ? 'is-selected' : ''}`}
                            onClick={() => chooseActivity(view)}
                            aria-pressed={activity === view}
                          >
                            {meta.label}
                            <span>{meta.size}</span>
                          </button>
                        );
                      })}
                    </div>
                  ) : null}

                  {index === 3 ? (
                    <div className="step-options step-options--pages" aria-label="页面">
                      {PAGE_LABELS.map((label, index) => (
                        <button
                          key={label}
                          type="button"
                          className={`option-chip ${page === index ? 'is-selected' : ''}`}
                          onClick={() => choosePage(index)}
                          aria-pressed={page === index}
                        >
                          <span className="option-index">0{index + 1}</span>
                          {label}
                        </button>
                      ))}
                    </div>
                  ) : null}
                </div>
              ))}
            </div>

            <div
              ref={configRef}
              id="appearance"
              className="config"
            >
              <div className="config-heading">
                <p className="section-kicker">02 / APPEARANCE</p>
                <h3>
                  形态与材质
                </h3>
                <p>选择停靠方式，再展开比较三种材质。</p>
              </div>

              <div className="control-group">
                <span className="control-label">形态</span>
                <div className="segmented" role="group" aria-label="岛形态">
                  {[
                    ['float', '浮岛'],
                    ['flush', '贴顶'],
                    ['dock', 'Dock'],
                  ].map(([value, label]) => (
                    <button
                      key={value}
                      type="button"
                      className={mode === value ? 'is-selected' : ''}
                      aria-pressed={mode === value}
                      onClick={() => {
                        frameRef.current?.load();
                        setMode(value as IslandMode);
                        send({ mode: value as IslandMode });
                      }}
                    >
                      {label}
                    </button>
                  ))}
                </div>
              </div>

              <div className="control-group">
                <span className="control-label">材质</span>
                <div className="segmented" role="group" aria-label="岛材质">
                  {[
                    ['obsidian', '黑曜石'],
                    ['acrylic', '亚克力'],
                    ['liquid', '液态玻璃'],
                  ].map(([value, label]) => (
                    <button
                      key={value}
                      type="button"
                      className={material === value ? 'is-selected' : ''}
                      aria-pressed={material === value}
                      onClick={() => {
                        frameRef.current?.load();
                        setMaterial(value as IslandMaterial);
                        send({
                          material: value as IslandMaterial,
                          state: 'expanded',
                          page: 0,
                        });
                      }}
                    >
                      {label}
                    </button>
                  ))}
                </div>
              </div>

              <p className="config-note">
                切换材质会展开 440×284 的真实原型预览。网页中的材质为视觉近似，实际效果由 Skia 渲染器绘制。
              </p>
            </div>
          </div>

          <div className="lab-stage">
            <div className="stage-status" aria-live="polite">
              <span className="stage-status__live">
                <span className="live-dot" aria-hidden="true" />
                LIVE · 真实状态
              </span>
              <span className="mono">{status}</span>
            </div>

            <PrototypeFrame
              ref={frameRef}
              title="FluidBar 真实灵动岛原型"
              name="fluidbar-lab"
              poster={poster}
              lazy
              onState={setState}
            />

            <div className="lab-stage__foot">
              <span className="mono">示例数据</span>
              <a
                href={`${import.meta.env.BASE_URL}island/embed.html`}
                target="_blank"
                rel="noreferrer"
                className="stage-link"
              >
                打开独立演示
                <ArrowUpRight size={12} aria-hidden="true" />
              </a>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
