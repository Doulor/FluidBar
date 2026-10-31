# FluidBar 4.0.0 正式版（2026-10-31）

- 自包含单文件 win-x64；发布包 smoke 退出码 0。
- 全量测试 2818 通过，0 失败。
- 自动更新已在本地 feed 和 GitHub Releases 生产通道实测：检查、下载、SHA-256 校验、重启替换 exe。

## 资产

| 文件 | 大小 | SHA-256 |
|---|---:|---|
| FluidBar-4.0.0-win-x64.zip | 51,990,839 bytes | `8d58457cdb32325e19cfb08d7abb6a355ec55a8138ad7461ff7c252746bec186` |
| FluidBar.exe | 57,659,878 bytes | `2f6818f4e6acd10ecc32dcc961e1d91ca063ffce067f88d70c8d91ab5b36733d` |

首个正式版未使用 Authenticode 证书；应用内更新仍强制校验 GitHub Release 资产的 SHA-256。
