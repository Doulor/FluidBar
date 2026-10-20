# Plugin Page API (v1)

Status: frozen with the v1 plugin platform. Applies to `FluidBar.Sdk` apiVersion 2.x.
Code: `src/FluidBar.Sdk/Canvas/IslandCanvas.cs`, `src/FluidBar.Sdk/Pages/PageData.cs`,
`src/FluidBar.Core/Presentation/IslandCanvas.cs`.

## 1. Page registration

A plugin declares `capabilities.ui.pages = true` and registers a page:

```csharp
context.Islands.RegisterPage(new PageDescriptor
{
    PageId = "my-plugin.status",
    Order = 60,
    Title = new LocalizedString("My status"),
    ShortTitle = new LocalizedString("Status"),
    IconToken = "agent",
    TemplateId = PageTemplates.Custom, // or PageTemplates.List
});
```

- `PageId` must use the plugin namespace (for example `my-plugin.status`); trusted built-ins use `builtin.`.
- A plugin may register at most four pages; the host has a global page cap of 12.
- The manifest must allow the template in `capabilities.ui.pageTemplates`.
- `StopAsync`, disable and unload remove the page and its command registrations.

## 2. Templates

| Template id | Use |
|---|---|
| `list` | Host-rendered rows (`PageListData`, maximum 20 rows). |
| `custom` | Plugin command canvas. |

Custom pages must publish `PageCanvasData`. A custom page without canvas commands is invalid.

## 3. Canvas coordinate space

Custom page commands use a **400 × 232 DIP** page body coordinate space. The origin is the top-left of the
page body; commands outside this rectangle, negative dimensions and malformed clip scopes are rejected.

The host owns rendering and theming. Plugin command records never contain fonts, font sizes, image paths,
shader source, or arbitrary file paths.

```csharp
var canvas = new CanvasRecorder();
canvas.FillRoundRect(16, 16, 368, 200, 18, "#14141B");
canvas.DrawText("72%", 140, 64, 120, 48, "accent", CanvasTextStyle.Title, CanvasTextAlign.Center);
canvas.DrawIcon("agent", 24, 24, 20, "foregroundSecondary");
canvas.DrawProgress(140, 144, 120, 6, 0.72, "accent");
```

### Commands

| Method | Purpose |
|---|---|
| `FillRect` | Solid rectangle. |
| `FillRoundRect` | Solid rounded rectangle. |
| `StrokeRoundRect` | Outline rounded rectangle. |
| `DrawText` | Host text style + left/center/right alignment. |
| `DrawIcon` | Host icon token only (no paths or bytes). |
| `DrawProgress` | Track + normalized 0..1 progress fill. |
| `PushClip` / `PopClip` | Intersect subsequent drawing with a rectangle; must balance. |
| `PushOpacity` / `PopOpacity` | Opacity scope 0..1; must balance. |

Colors are host tokens (`foreground`, `foregroundSecondary`, `accent`, `success`, `warning`,
`danger`, `info`, `track`, `transparent`) or `#RRGGBB`/`#AARRGGBB` hex. Icon tokens are limited to the
host allowlist; unknown tokens and paths are rejected.

## 4. Hit areas

`PageHitArea` declares the semantic, interactive regions of a custom page:

```csharp
new PageHitArea
{
    Id = "ring-button",
    Kind = PageHitAreaKind.Button, // Button | Row | Slider | Scroll
    X = 60, Y = 40, Width = 152, Height = 152,
    CommandId = "my-plugin.hit",
    AccessibleName = "Increase ring progress",
    LongPress = true,
    DoubleClick = true,
}
```

- Hit testing, pointer capture, long-press timing (500 ms), double-click timing (400 ms) and slider
  normalization are host-owned.
- Slider declarations carry `Min`, `Max` and `Value`; UIA exposes a RangeValue 0–100.
- The command target receives the hit-area id plus `CommandId`; slider commands receive a normalized
  0..1 value.
- **Every interactive region must supply `AccessibleName`.** A hit area without a non-empty name is treated
  as decorative: it is excluded from pointer hit testing and UIA, even if it declares a command.

## 5. Compact view

`IslandActivity.CompactView` may carry a `CompactViewDescriptor`:

```csharp
CompactView = new CompactViewDescriptor
{
    Width = 236,   // 132..430
    Height = 36,   // 36..44
    BackgroundColor = "#14141B",
    ForegroundColor = "#FFFFFF",
    AccessibleName = "Ring progress compact",
    Commands = compactRecorder.Build(),
}
```

The host validates size, command bounds and background/foreground contrast (minimum 4.5:1). An invalid
descriptor is ignored and the generic compact template is used. Compact commands use the descriptor's local
coordinate space (0..Width, 0..Height).

## 6. Settings preview canvas

`SettingsPageContribution.PreviewCanvas` accepts `PreviewCanvasData` with the same command records. It is a
decorative preview surface; it does not create interactive hit areas. Invalid commands are not rendered.

## 7. Limits and validation

| Limit | Value |
|---|---:|
| Commands per published canvas | 256 |
| Text characters per command | 512 |
| Hit areas per page | 32 |
| Compact width | 132–430 DIP |
| Compact height | 36–44 DIP |
| Compact contrast ratio | ≥ 4.5 |
| Clip/opacity nesting | 8 each, balanced |

Rejected payloads fail closed with a `PageRegistry`/custom-canvas error; they are not rendered partially.

## 8. Error codes

| Code / failure | Meaning |
|---|---|
| `manifest.contributes.pages` / template not allowed | Manifest did not allow `list` or `custom`. |
| `custom canvas payload is missing` | `TemplateId=custom` without `PageData.Canvas`. |
| `Canvas command geometry is outside the canvas` | Negative/out-of-bounds command geometry. |
| `Canvas icon token is not allowed` | Icon token not in the host allowlist. |
| `Canvas color is not a host token or hex` | Invalid color token/hex. |
| `Canvas has an unmatched clip/opacity scope` | Push without matching pop. |
| `Canvas progress must be in 0..1` / opacity error | Invalid numeric range. |
| `Canvas command limit exceeded` | More than 256 commands. |
| `Canvas hit area is invalid or outside the canvas` | Bad hit-area id/geometry/slider range. |
| `Compact view size is outside host bounds` | Width/height outside compact limits. |
| `Compact view contrast is too low` | Background/foreground contrast below 4.5. |
| `PluginLoadFailureKind.Untrusted` | Market package failed the load-time trust gate. |

