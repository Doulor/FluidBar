// Editorial copy grounded in plugins/*/README.md. Release metadata is always
// read from the signed catalog; these descriptions never replace signed fields.
export const guides = {
  'fluidbar-file-shelf': {
    eyebrow: '文件流转，少开几个窗口',
    intro: '先把文件放到手边，需要时再拖出去。文件暂存保存的是引用，原文件仍留在原来的位置。',
    features: [
      ['随手放进来', '把文件拖到灵动岛，或打开插件的暂存窗口，通过拖入、选择文件添加。'],
      ['拖出去，就是复制', '从暂存窗口拖动文件名到其他应用，按复制方式传递文件。'],
      ['整理引用，不动原文件', '最多保留 32 项引用。移除或清空只改变暂存列表，不会移动、删除或重命名原文件。'],
    ],
    afterInstall: '启用后，从插件页打开文件暂存窗口，或直接把文件拖到灵动岛。若旧版宿主提示能力不可用，请先更新 FluidBar。',
    source: 'plugins/fluidbar-file-shelf',
  },
  'fluidbar-timer': {
    eyebrow: '把时间，留在视线里',
    intro: '给眼前的事情留一段时间。开始倒计时后，小岛显示剩余时间，到时提醒你。',
    features: [
      ['从 1 分钟到 180 分钟', '在插件页设置时长并开始。休息、等候或一段专注时间，都可以自己安排。'],
      ['随时暂停，接着继续', '支持暂停、继续和结束，计划变化时不用重新开始。'],
      ['只在需要时计时', '运行时更新剩余时间并在完成时提醒；暂停、结束或卸载后释放周期调度。'],
    ],
    afterInstall: '启用后，在插件页设置 1–180 分钟的时长，点击开始。运行中的剩余时间会显示在小岛上。',
    source: 'plugins/fluidbar-timer',
  },
  'sample-minimal': {
    eyebrow: '从第一条岛消息开始',
    intro: '面向开发者的最小入门插件。用一个小例子串起生命周期、本地化和声明式岛布局，再以此开始自己的插件。',
    features: [
      ['最小的完整链路', '参考初始化、启动、停止与释放的生命周期，以及插件构建、校验和打包。'],
      ['声明式布局', '通过清单中的 text.v1 模板显示岛消息，无需编写 UI 或 XAML。'],
      ['本地化与资源管理', '包含语言资源与宿主管理的定时任务，展示可随卸载释放的资源用法。'],
    ],
    afterInstall: '这是 SDK 开发示例。安装体验后，可阅读源码并复制为自己的插件起点。',
    source: 'plugins/samples/FluidBar.Plugins.SampleMinimal',
  },
  'sample-rich': {
    eyebrow: '看看插件可以怎样扩展',
    intro: '面向开发者的完整贡献示例，展示设置、命令、右键菜单、小组件与悬停卡片如何在同一个插件中协作。',
    features: [
      ['多个入口，一份清单', '参考紧凑布局、悬停卡片、设置页、右键菜单和附属小组件的声明方式。'],
      ['连接设置与命令', '示例使用宿主管理的设置，并把清单中的命令 ID 绑定到运行时处理器。'],
      ['依赖与本地化', '包含插件私有依赖、支持复数形式的语言消息，以及卸载时的资源清理。'],
    ],
    afterInstall: '这是 SDK 参考插件。可结合开发文档阅读设置、命令和小组件的实现。',
    source: 'plugins/samples/FluidBar.Plugins.SampleRich',
  },
};

export const capabilities = {
  files: ['文件访问', '访问插件功能所需的文件。'],
  ui: ['界面展示', '在小岛及插件界面中展示内容。'],
  notifications: ['通知提醒', '发送插件产生的提醒。'],
  background: ['后台任务', '在插件运行时安排后台工作。'],
  network: ['网络访问', '连接插件功能所需的网络服务。'],
  clipboard: ['剪贴板', '读取或写入剪贴板内容。'],
  media: ['媒体状态', '访问媒体相关功能。'],
};
