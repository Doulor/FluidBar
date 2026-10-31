# FluidBar 4.0.0 正式版（2026-10-31）

- 自包含单文件 win-x64；发布包 smoke 退出码 0。
- 全量测试 2818 通过，0 失败。
- 自动更新本地链路实测：检查、下载、SHA-256 校验、重启替换 exe。

## 资产

| 文件 | 大小 | SHA-256 |
|---|---:|---|
| FluidBar-4.0.0-win-x64.zip | 51,990,912 bytes | `5cf86ad65565ee84420868cf7789a3cd24b7eb9527e1905c0fb923725da595cd` |
| FluidBar.exe | 57,659,889 bytes | `fba29b7ba97f7d0e2511069b26fb777cf1e1d92e7e29e39463eebe82a8b05cef` |

首个正式版未使用 Authenticode 证书；应用内更新仍强制校验 GitHub Release 资产的 SHA-256。
