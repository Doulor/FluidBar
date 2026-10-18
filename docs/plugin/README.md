# FluidBar plugin developer documentation

Everything a contributor or maintainer needs for the v2 plugin ecosystem. The plugin contract itself is
frozen in [`docs/rewrite/contracts/plugin.md`](../rewrite/contracts/plugin.md); this directory is the practical
how-to set.

| Document | Audience | Contents |
|---|---|---|
| [quickstart.md](quickstart.md) | contributors | 15-minute scaffold → run → validate → pack → PR |
| [manifest-reference.md](manifest-reference.md) | contributors | every manifest field, capabilities/consent, versions, locales |
| [declarative-ui.md](declarative-ui.md) | contributors + App/UI | layouts/slots/settings groups/rows/controls/commands/context-menu/widgets + i18n rules |
| [listing-i18n.md](listing-i18n.md) | contributors + marketplace | marketplace listing metadata, localized descriptions, locale coverage + fallback chain |
| [signing-and-keys.md](signing-and-keys.md) | maintainer (Doulor) | package/index signing, secrets, key rotation + revocation runbook |
| [review-checklist.md](review-checklist.md) | maintainer | the PR review checklist used for every plugin |
| [publish-runbook.md](publish-runbook.md) | maintainer | merge → CI → signed index → Pages/live, rollback + incident steps |
| [troubleshooting.md](troubleshooting.md) | everyone | manifest/loader/signature/packaging/unload failure playbooks |

## Current reality (single maintainer)

FluidBar is currently maintained by **Doulor** alone. The ecosystem is therefore optimised for a one-person
review+publish loop rather than a committee:

- Plugin source lives in a monorepo layout (`plugins/<id>/`); CI builds from reviewed source — never from an
  author-uploaded binary.
- One maintainer review; high-risk capabilities need a written acceptance note in the PR.
- Merge triggers the publish workflow; target **merge → live < 10 minutes**.
- No private keys in the repository: the signing key is a protected CI secret/HSM. See
  [signing-and-keys.md](signing-and-keys.md).
- Future community governance (CODEOWNERS, two-reviewer rule for high-risk capabilities, rotation, public
  appeal log) is documented in [`marketplace/README.md`](../../marketplace/README.md) and starts only when the
  trigger conditions are met (≥ 3 external contributors, ≥ 20 monthly active plugins).

## Tooling

The `fluidbar-plugin` CLI (source: `src/FluidBar.Cli`) is the single implementation used by contributors, the
workflow and the client installer:

```powershell
dotnet run --project src/FluidBar.Cli -- validate --plugin <dir|build-output>
dotnet run --project src/FluidBar.Cli -- policy   --plugin <dir|build-output>
dotnet run --project src/FluidBar.Cli -- pack     --plugin <dir|build-output> --out <file.fbp>
dotnet run --project src/FluidBar.Cli -- dev      --plugin <dir|build-output>
dotnet run --project src/FluidBar.Cli -- keygen   --out <private.pem>
dotnet run --project src/FluidBar.Cli -- sign     --package <file.fbp> --key <private.pem> --key-id <id>
dotnet run --project src/FluidBar.Cli -- publish-prepare --plugins-root plugins/samples --artifacts artifacts/plugins --marketplace marketplace-site --key-id <id> --key <private.pem>
dotnet run --project src/FluidBar.Cli -- verify-index --marketplace marketplace-site
```

The JSON Schemas live in [`schemas/`](../../schemas): `plugin.schema.json`, `marketplace-index.schema.json`,
`locale.schema.json`, `signature.schema.json`.
