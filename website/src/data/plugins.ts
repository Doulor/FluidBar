export interface PluginItem {
  id: string;
  label: string;
  icon: 'media' | 'notifications' | 'clipboard' | 'agent' | 'devices' | 'widgets';
  summary: string;
  capabilities: string[];
  code: string;
}

export const PLUGINS: PluginItem[] = [
  {
    id: 'media',
    label: '媒体',
    icon: 'media',
    summary: '封面、标题、歌词、进度与播放控制。',
    capabilities: ['media.sessions', 'media.control'],
    code: `"capabilities": {
  "media": {
    "sessions": true,
    "control": true,
    "reason": "Show and control the active media session"
  }
}`,
  },
  {
    id: 'notifications',
    label: '通知',
    icon: 'notifications',
    summary: '应用、标题、正文、时间与关闭操作。',
    capabilities: ['notifications.read'],
    code: `"capabilities": {
  "notifications": {
    "read": true,
    "post": false
  }
}`,
  },
  {
    id: 'clipboard',
    label: '剪贴板',
    icon: 'clipboard',
    summary: '文本、图片与文件历史，支持固定和复制。',
    capabilities: ['clipboard.read', 'clipboard.write'],
    code: `"capabilities": {
  "clipboard": {
    "read": true,
    "write": true,
    "reason": "Restore an item selected by the user"
  }
}`,
  },
  {
    id: 'agent',
    label: 'Agent',
    icon: 'agent',
    summary: 'Claude Code、Codex 与 DeepSeek Harness 的任务状态。',
    capabilities: ['process.read'],
    code: `"capabilities": {
  "process": {
    "spawn": false,
    "reason": "Read agent task state from declared processes"
  }
}`,
  },
  {
    id: 'devices',
    label: '设备',
    icon: 'devices',
    summary: 'USB、蓝牙、电量与设备连接状态。',
    capabilities: ['devices.read'],
    code: `"capabilities": {
  "background": {
    "intervalMinMs": 2000,
    "reason": "Refresh device presence while the island is visible"
  }
}`,
  },
  {
    id: 'widgets',
    label: '小组件',
    icon: 'widgets',
    summary: '天气、日历、任务与系统状态卡片。',
    capabilities: ['ui.widgets'],
    code: `"capabilities": {
  "ui": {
    "widgets": true,
    "island": true,
    "settings": true
  }
}`,
  },
];

export const MANIFEST_SAMPLE = `{
  "schemaVersion": 2,
  "id": "now-playing",
  "version": "1.0.0",
  "apiVersion": "2.0.0",
  "minAppVersion": "2.0.0",
  "publisher": {
    "id": "doulor",
    "displayName": "Doulor"
  },
  "name": {
    "en-US": "Now Playing",
    "zh-Hans": "正在播放"
  },
  "summary": {
    "en-US": "A media activity for FluidBar.",
    "zh-Hans": "在 FluidBar 上显示媒体活动。"
  },
  "license": "MIT",
  "entry": {
    "runtime": "inproc",
    "assembly": "plugin/FluidBar.Plugins.NowPlaying.dll",
    "type": "FluidBar.Plugins.NowPlaying.NowPlayingPlugin",
    "targetFramework": "net10.0"
  },
  "locales": {
    "default": "en-US",
    "available": ["en-US", "zh-Hans"],
    "path": "locales",
    "fallback": "en-US"
  },
  "capabilities": {
    "media": {
      "sessions": true,
      "control": true,
      "reason": "Show and control the active media session"
    }
  },
  "contributes": {}
}`;
