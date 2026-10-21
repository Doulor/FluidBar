# 构建与测试产物清理规则（读我）

> 2026-10-21 立规。起因：D 盘被本仓库的构建/测试产物吃掉 **130.8 GB**（清理后释放 118.4 GB）。

## 规则

1. **每次构建/测试跑完必须清理**，不允许把产物留在仓库里过夜。
2. `dotnet test` 的隔离输出只允许用**同一个固定目录**（`artifacts/test-out`），**禁止**按次新建 `artifacts/t-<tag>/`。
   每次带 `-p:BaseOutputPath=artifacts/xxx/` 都会复制一整套依赖图（0.5–1.3 GB），跑几十次就是上百 GB。
3. 需要并行/隔离时用 `--no-build` 复用已有产物，或把输出放到 `%TEMP%\fluidbar-build`（系统盘清理会带走）。
4. 收工前执行：
   ```powershell
   # 删掉所有测试/探针输出（.gitignore 内，可随时重生成）
   Get-ChildItem D:\build\GitLocal\FluidBar -Recurse -Directory -Force |
     Where-Object { $_.Name -eq 'artifacts' } | Remove-Item -Recurse -Force
   # 可选：清 bin/obj（下次构建慢几分钟，但能再腾 ~6 GB）
   dotnet clean FluidBar.sln
   ```
5. `build/release/win-x64`（发布产物）**不在清理范围内** —— 那是可交付物，只有重新发布会覆盖。

## 现状（2026-10-21 20:50 清理后）

| 项 | 清理前 | 清理后 |
|---|---|---|
| 仓库内 artifacts 类目录 | 130.8 GB | 0 |
| D 盘可用 | 68.4 GB | 186.8 GB |
| 仓库 bin+obj | ~6.2 GB | 保留（加速构建） |

## 谁容易踩坑

- 用 `-p:BaseOutputPath=artifacts/t-u9/` 这种"每次一换"的 tag 跑测试的 AI / 脚本；
- 把 verify 源码树整棵复制到 `artifacts/taskNN/verify/` 的验收流程（task43/task44 各占 ~15 GB）。
