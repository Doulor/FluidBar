export const COMPACT_VIEWS = [
  'idle',
  'media',
  'notifications',
  'clipboard',
  'agent',
  'timer',
  'battery',
  'hud',
] as const;

export type CompactView = (typeof COMPACT_VIEWS)[number];
export type IslandMode = 'float' | 'flush' | 'dock';
export type IslandMaterial = 'obsidian' | 'acrylic' | 'liquid';

export interface PrototypeState {
  ready: boolean;
  expanded: boolean;
  view: CompactView;
  page: number;
  mode: IslandMode;
  material: IslandMaterial;
  theme: 'dark' | 'light';
  accent: string;
  width: number;
  height: number;
}

export interface PrototypeCommand {
  type: 'fluidbar:set';
  state?: CompactView | 'expanded';
  page?: number;
  track?: number;
  mode?: IslandMode;
  material?: IslandMaterial;
  accent?: string;
  motion?: 'normal' | 'reduced';
}

export interface PrototypeStateMessage {
  type: 'fluidbar:state';
  state: PrototypeState;
}

export const DEFAULT_PROTOTYPE_STATE: PrototypeState = {
  ready: false,
  expanded: false,
  view: 'idle',
  page: 0,
  mode: 'float',
  material: 'obsidian',
  theme: 'dark',
  accent: '#4cc9f0',
  width: 132,
  height: 36,
};

const VIEW_SET = new Set<string>(COMPACT_VIEWS);
const MODE_SET = new Set<string>(['float', 'flush', 'dock']);
const MATERIAL_SET = new Set<string>(['obsidian', 'acrylic', 'liquid']);

export function normalizePage(page: number): number {
  if (!Number.isFinite(page)) return 0;
  return Math.max(0, Math.min(5, Math.trunc(page)));
}

export function isPrototypeState(value: unknown): value is PrototypeState {
  if (!value || typeof value !== 'object') return false;
  const state = value as Record<string, unknown>;

  return (
    typeof state.ready === 'boolean' &&
    typeof state.expanded === 'boolean' &&
    typeof state.view === 'string' &&
    VIEW_SET.has(state.view) &&
    typeof state.page === 'number' &&
    Number.isInteger(state.page) &&
    state.page >= 0 &&
    state.page <= 5 &&
    typeof state.mode === 'string' &&
    MODE_SET.has(state.mode) &&
    typeof state.material === 'string' &&
    MATERIAL_SET.has(state.material) &&
    (state.theme === 'dark' || state.theme === 'light') &&
    typeof state.accent === 'string' &&
    typeof state.width === 'number' &&
    typeof state.height === 'number'
  );
}

export function isPrototypeStateMessage(value: unknown): value is PrototypeStateMessage {
  if (!value || typeof value !== 'object') return false;
  const message = value as Record<string, unknown>;
  return message.type === 'fluidbar:state' && isPrototypeState(message.state);
}

export function makeCommand(command: Omit<PrototypeCommand, 'type'>): PrototypeCommand {
  return { type: 'fluidbar:set', ...command };
}
