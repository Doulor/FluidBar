# 代码签名证书：申请材料与后续接入

> 现状：**unsigned**。这带来两个后果，彼此独立：

1. **Windows / SmartScreen**：首次运行弹「未知发布者」。与"是不是单文件"无关，只看 exe 里有没有受信任的
   Authenticode 签名。用户点"仍要运行"即可，功能不受影响。
2. **应用内自动安装**：`CompositeUpdateVerifier` 默认要求 SHA-256 + 签名双通过。**这是本项目自己的策略**，
   不是 Windows 强制。预览版构建已经放宽为"仅 SHA-256"（见 `UpdateServiceOptions.RequireSignature`），
   所以**没有证书也能自动更新**，只是不会消除 SmartScreen 警告。

## 1. 三条可选路径

| 方案 | 大致成本 | 门槛 | 适合 |
|---|---|---|---|
| **SignPath Foundation**（signpath.org/apply） | 免费 | 开源项目（OSI 许可证）、人工审核、CI 在 GitHub Actions | ✅ 本项目（MIT）首选 |
| **Certum Open Source Code Signing** | ~€100/年 | 个人开发者 + 开源项目证明 | ✅ 备选，无需基金会审核 |
| **Azure Trusted Signing** | ~$9.99/月 | 组织主体、一般要求 3 年经营历史 | ⚠️ 个人开发者通常不满足 |
| SSL.com / Sectigo OV/IV | $200–400/年 | 企业或个人身份核验，EV 需硬件 token | 商业发行时考虑 |

自签证书（`New-SelfSignedCertificate`）**只适合本机开发测试**：用户机器没有你的根证书，
`WinVerifyTrust` 依然判不信任。

## 2. SignPath Foundation 申请表要准备什么

以下信息全部可在本仓库直接取到：

| 字段 | 填什么 |
|---|---|
| Project name | FluidBar |
| Repository | https://github.com/Doulor/FluidBar |
| License | MIT（`LICENSE`） |
| Primary language / stack | C# / .NET 10 / Avalonia + Win32 |
| Build system | `dotnet build FluidBar.sln`；发布脚本 `tools/release/publish.ps1`（单文件 win-x64） |
| CI | GitHub Actions（`.github/workflows/release.yml`，push tag `v*` 触发） |
| Artifact to sign | `build/release/win-x64/FluidBar.exe`（框架依赖单文件，约 59 MB） |
| Maintainer | Doulor（仓库所有者） |
| 说明要点 | 桌面小工具、无网络收集、签名用于让用户确认发布者并启用应用内更新 |

提交时通常需要：仓库地址、许可证、CI 配置文件链接、以及"为什么需要签名"的一段说明（可直接用上表）。

## 3. 拿到证书后怎么接（代码侧已就绪，无需改动）

1. **证书/凭据进 CI secrets**（不要进仓库）：
   - SignPath：`SIGNPATH_API_TOKEN`、`SIGNPATH_ORGANIZATION_ID`；
   - 或 Azure Trusted Signing：`AZURE_TENANT_ID` / `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET`；
   - 或 PFX：`CODESIGN_PFX`（base64）+ `CODESIGN_PASSWORD`。
2. **签名时机**：`dotnet publish` 之后、压缩/上传之前，对单个 exe 签名：
   ```powershell
   signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a build\release\win-x64\FluidBar.exe
   ```
3. **写进发布清单**：`build/publish.ps1` 里 `signature = 'unsigned'` 改成签名者主体或证书指纹。
4. **打开强制模式**：稳定版构建默认 `RequireSignature = true`，签名一到位即自动生效；
   预览版可保留"仅 SHA-256"的宽松策略。
5. **验证**：本机对签名后的 exe 跑一遍 `WinVerifyTrust`（应用内 `WinVerifyTrustSignatureVerifier` 同一套 API），
   再用 `file://` 本地 feed 走一遍 检查 → 下载 → 校验 → 安装 → 重启。

## 4. 期间如何发布（无证书）

```powershell
git tag v3.0.0-preview.1        # 必须 semver 且大于当前装配版本（2.0.0）
git push origin v3.0.0-preview.1
```

`release.yml` 会用 tag 当版本号跑 `build/publish.ps1`，然后 `gh release create` 上传
`FluidBar.exe` + `publish-3.0.0-preview.1.json`；经 API 上传的资产 GitHub 会自动填 `digest: sha256:…`，
正是更新器校验所需的字段。发布后客户端点「检查更新」即可看到"发现新版本 + 打开发布页"，
预览版构建还能直接"重启并安装"。
