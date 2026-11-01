# 官网插件市场展示层

`render.mjs` 生成 `/market/index.html` 与 `/market/listings/<id>/index.html`。
`content.mjs` 中的说明根据各插件 README 编写；版本、下载地址、发布者、权限、历史与校验值均来自当前签名目录。

- 修改本目录的模板、样式和交互，通过 `pnpm build` 的 `sync-marketplace.mjs` 同步到 `public/market/`。不要直接编辑生成的 HTML 或 `public/market/ui/`。
- 签名 `index.json`、`keys.json` 和 `plugins/*.json` 始终按源字节复制，不做格式化、字段合并或重签。
- 实用工具 / 开发示例是网页用途分组。示例根据已有 sample 标签或示例发布者识别，不改变签名类别。
- 搜索、筛选、复制校验值是渐进增强；禁用 JS 时仍有完整目录、详情和下载链接。
- 新插件自动获得通用详情页。专属用法说明只能依据真实产品文档补充，不能凭空添加功能、评分或使用量。
- 撤回、停用或屏蔽的条目不显示安装包下载操作。
- CSS/JS URL 带内容摘要，避免页面与交互资源版本不一致。所有资源同源，沿用主站品牌与字体。
- 中文文案或目录变化后，可运行 `python scripts/subset-market-font.py` 更新 CJK 字体子集（维护命令，需要 fonttools[woff]，不是构建依赖）。
- `tests/marketplace.spec.ts` 检查搜索、筛选、详情、复制失败、无 JS、可访问性、多尺寸及 404；Node 测试检查原始签名字节、未知插件、文本转义和不可安装状态。
