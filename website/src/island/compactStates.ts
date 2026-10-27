import type { CompactView } from './protocol';

export interface CompactStateMeta {
  id: CompactView;
  label: string;
  size: string;
  description: string;
}

export const COMPACT_STATE_META: CompactStateMeta[] = [
  { id: 'idle', label: '常驻', size: '132 × 36', description: '时间与安静状态' },
  { id: 'media', label: '媒体', size: '226 × 40', description: '封面、标题、播放与进度' },
  { id: 'notifications', label: '通知', size: '248 × 36', description: '应用、标题与未读数' },
  { id: 'clipboard', label: '剪贴板', size: '264 × 36', description: '最近一次复制内容' },
  { id: 'agent', label: 'Agent', size: '236 × 36', description: '任务状态与耗时' },
  { id: 'timer', label: '计时器', size: '190 × 36', description: '专注倒计时' },
  { id: 'battery', label: '电池', size: '208 × 36', description: '充电状态与电量' },
  { id: 'hud', label: '音量', size: '208 × 40', description: '音量与亮度 HUD' },
];

export const COMPACT_STATE_IDS = COMPACT_STATE_META.map((state) => state.id);
