export interface PerformanceStat {
  value: number;
  decimals?: number;
  suffix: string;
  label: string;
  note: string;
}

export const PERFORMANCE_STATS: PerformanceStat[] = [
  { value: 0.61, decimals: 2, suffix: 'ms', label: '单帧渲染', note: 'Skia 稳定帧' },
  { value: 0.85, decimals: 2, suffix: 'ms', label: 'p99', note: '长尾帧耗时' },
  { value: 120, suffix: 'fps', label: '动画预算', note: '弹簧与形变' },
  { value: 0, suffix: 'B', label: '每帧分配', note: '稳定帧 GC 压力' },
  { value: 2600, suffix: '+', label: '自动化测试', note: 'Core / Platform / UI' },
];

export const ARCHITECTURE = [
  {
    id: 'host',
    name: 'Win32 分层宿主',
    short: '固定窗口与合成层',
    detail: '固定尺寸的分层窗口，逐像素透明，点击穿透。窗口不逐帧移动，动画只发生在内部。',
  },
  {
    id: 'render',
    name: 'Skia 渲染器',
    short: '岛、页面与材质',
    detail: 'SkiaSharp 自绘壳体、文字、图标与材质。命中区、渲染和 UIA 使用同一套布局几何。',
  },
  {
    id: 'core',
    name: 'Core 状态机',
    short: '活动、优先级与布局',
    detail: '纯领域层：活动状态、优先级、弹簧、pager 和设置。没有 UI 依赖，方便测试。',
  },
  {
    id: 'sdk',
    name: 'Plugin SDK',
    short: '声明式贡献',
    detail: '插件只贡献声明式布局、设置页、命令和小组件；每个插件独立加载，可卸载。',
  },
];
