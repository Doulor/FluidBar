# 子灵动岛（split satellite）渲染接入契约

本文件是动效驱动层（FluidBar.Island.SplitSatelliteMotion）与渲染层（IslandRenderer /
IslandMetrics / SplitSatelliteStyle）之间的最小约定。渲染文件由视觉线（rail-pixel）持有，
动效侧只负责产出数值；本契约不要求改动动效代码。

- 动效实现：src/FluidBar.Island/SplitSatelliteMotion.cs
- 参数常量：src/FluidBar.Island/MotionTokens.cs（SatelliteMotionTokens）
- 固定参数测试：tests/FluidBar.Island.Tests/SplitSatelliteMotionTests.cs

---

## 1. SplitSatelliteMotion.Values 字段语义

类型：readonly record struct SplitSatelliteValues，每帧读取，纯值，无副作用。

| 字段 | 类型 | 单位 | 范围 | 语义 |
|---|---|---|---|---|
| State | SplitSatelliteState | — | Hidden/Birth/Idle/Merge/Merged | 生命周期。Hidden、Merged 时不绘制；Birth 期间绘制出生形变；Idle 时呼吸与圆环旋转生效 |
| X | double | DIP | 相对主岛锚点，通常 0–160 | 卫星中心横坐标（不是左边缘） |
| Y | double | DIP | 相对主岛锚点，通常 −40–60 | 卫星中心纵坐标（向下为正） |
| Scale | double | 倍率 | 0.90–1.05 | 已合成的缩放：出生/融合弹簧 × 按压 × 悬停 × 呼吸。渲染直接乘基准尺寸，不要再叠加其它缩放 |
| StretchX | double | 归一化 | −0.08–0.08 | 沿 X 的方向性拉伸量；0.08 = 该轴放大 8%。出生时沿运动方向，随时间指数衰减到 0 |
| StretchY | double | 归一化 | −0.08–0.08 | 同上，Y 轴 |
| Highlight | double | 0–1 | 悬停 0–0.35 | 悬停高光强度（乘到高光/描边的不透明度上） |
| Brightness | double | 增量 | −0.06–0 | 亮度增量；按压时为 −0.06（与主岛按压一致）。渲染用白色叠加或黑色压暗的等价实现均可，只要视觉一致 |
| BreathScale | double | 倍率 | 0.988–1.012 | 仅呼吸分量。已包含在 Scale 里，单独给出是为了让渲染侧可以在需要时只对某个图层应用呼吸（例如让蓝环不呼吸） |
| RingAngleDegrees | double | 度 | 0–360 | Agent 蓝环角度；Idle 时以 1/3 圈每秒恒定旋转，非 Idle 时保持不变 |
| PressDepth | double | 0–1 | 0 = 松开，1 = 完全按下 | 按压深度，用于内阴影强度映射（见第 4 节） |

注意：Scale 与 BreathScale 有重叠。默认渲染只用 Scale；只有需要「呼吸只作用于球体、不作用于蓝环」时才用 Scale / BreathScale 得到不含呼吸的缩放。

---

## 2. 渲染侧需要新增的字段

建议在 IslandRenderState 增加（与现有 SplitIslets / SplitProgress 并存，互不覆盖）：

    /// <summary>
    /// One animated split-satellite frame, projected from SplitSatelliteMotion.Values.
    /// Ordered like SplitIslets: index i belongs to SplitIslets[i].
    /// Empty when the satellites are not animated (hosts/tests that render a settled layout).
    /// </summary>
    public IslandSatelliteFrame[] SatelliteFrames { get; init; } = [];

    /// <summary>Render-ready split-satellite pose (see docs/satellite-render-contract.md).</summary>
    public readonly record struct IslandSatelliteFrame(
        SplitSatelliteState State,
        double X,               // DIP, centre, relative to the main-island anchor
        double Y,               // DIP, centre, relative to the main-island anchor
        double Scale,           // composed: birth/merge x press x hover x breathing
        double StretchX,        // normalised, -0.08..0.08
        double StretchY,        // normalised, -0.08..0.08
        double Highlight,       // 0..1
        double Brightness,      // -0.06..0
        double RingAngleDegrees,
        double PressDepth);     // 0..1

约定：

1. SatelliteFrames.Length == SplitIslets.Length；State 为 Hidden/Merged 的条目视为不可见（渲染跳过）。
2. 渲染器若拿不到 SatelliteFrames（老调用方、测试、快照），必须退回现有 SplitProgress 路径，行为不变。
3. SplitIslets[i] 仍提供图标、标题、副标题、颜色等内容；SatelliteFrames[i] 只提供姿态。

---

## 3. 调用顺序与线程要求

    // host frame loop (island UI thread, same thread that owns the Skia surface)
    foreach (var satellite in satellites)          // one SplitSatelliteMotion per satellite
    {
        satellite.Update(dtSeconds);               // dt = frame delta, 0 < dt <= 0.05 recommended
    }

    // after all updates:
    renderState = renderState with
    {
        SatelliteFrames = satellites.Select(s => s.ToFrame()).ToArray(),
    };
    renderer.Draw(renderState);

- 线程：SplitSatelliteMotion 不是线程安全的，必须在与 IslandRenderer 相同的 UI 线程上 Update 并读取 Values；不要在后台线程采样。
- 顺序：先 Update(dt)，再读 Values，再投影进 IslandRenderState，最后绘制。同一帧内不要重复 Update。
- dt：内部按 ≤ 4 ms 子步积分，传入帧间隔即可；长时间挂起（> 0.5 s）建议钳制到 0.05 s，避免一次补齐导致跳变。
- 中断/重触发：点击、滚轮、活动变化时调用 BeginBirth / BeginMerge / Press / Release / SetHover 即可，无需重建对象；弹簧会从当前值继续。
- 测试：宿主未驱动动效时不要填 SatelliteFrames，保持空数组，渲染走旧路径，快照测试不受影响。

---

## 4. 伪代码渲染示例

    void DrawSatellite(SKCanvas canvas, in IslandSatelliteFrame frame, in IslandSplitIslet content,
                       SKPoint anchor, float baseDiameter, float dpiScale)
    {
        if (frame.State is SplitSatelliteState.Hidden or SplitSatelliteState.Merged)
        {
            return;                                        // nothing to draw
        }

        // 1) centre in device pixels
        var centre = new SKPoint(
            anchor.X + (float)frame.X * dpiScale,
            anchor.Y + (float)frame.Y * dpiScale);

        // 2) size: one composed scale, then the directional stretch (8 % along travel)
        var radius = baseDiameter * 0.5f * (float)frame.Scale * dpiScale;
        var sx = 1f + (float)frame.StretchX;
        var sy = 1f + (float)frame.StretchY;

        // 3) draw the body with stretch about the centre
        using var body = new SKPath();
        body.AddCircle(0, 0, radius);
        var transform = SKMatrix.CreateScale(sx, sy);
        transform.TransX = centre.X;
        transform.TransY = centre.Y;
        canvas.Save();
        canvas.SetMatrix(transform);

        // pressed: darken by |Brightness| (equivalent of the -6 % lift); hovered: +Highlight
        var bodyColor = Darken(content.Accent, (float)Math.Abs(frame.Brightness));
        using var fill = new SKPaint { Color = bodyColor, IsAntialias = true };
        canvas.DrawPath(body, fill);

        // 4) press depth -> inner shadow at the top edge (max 18 % black at full press)
        if (frame.PressDepth > 0.01)
        {
            var alpha = (byte)(0.18f * (float)frame.PressDepth * 255);
            using var shadow = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, -radius), new SKPoint(0, radius * 0.2f),
                    [new SKColor(0, 0, 0, alpha), new SKColor(0, 0, 0, 0)],
                    [0f, 1f],
                    SKShaderTileMode.Clamp),
            };
            using var clip = new SKPath();
            clip.AddCircle(0, 0, radius);
            canvas.Save();
            canvas.ClipPath(clip, antialias: true);
            canvas.DrawPath(body, shadow);
            canvas.Restore();
        }

        // 5) hover highlight ring (Highlight 0..0.35)
        if (frame.Highlight > 0.01)
        {
            using var ring = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f * dpiScale,
                Color = new SKColor(255, 255, 255, (byte)(frame.Highlight * 255)),
            };
            canvas.DrawCircle(0, 0, radius - 1, ring);
        }

        // 6) Agent ring: constant spin, same angle for all satellites
        if (content.Kind == ActivityKind.Agent)
        {
            using var agentRing = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f * dpiScale,
                Color = new SKColor(90, 140, 255),
            };
            using var arc = new SKPath();
            arc.AddArc(new SKRect(-radius, -radius, radius, radius),
                (float)frame.RingAngleDegrees, sweepAngle: 300);
            canvas.DrawPath(arc, agentRing);
        }

        canvas.Restore();
    }

要点：

- StretchX/Y 的应用：以卫星中心为原点做 scale(1 + StretchX, 1 + StretchY)，不要用平移模拟；拉伸会同时影响描边宽度，必要时用 SKPaint.StrokeWidth = base / max(sx, sy) 补偿。
- PressDepth → 内阴影：满按 18% 黑（与主岛一致），线性映射 alpha = 0.18 * PressDepth，方向固定为顶部进入、下缘消失。
- Highlight 乘到白色高光/描边 alpha 上，不要改主色。
- RingAngleDegrees 已含累计角度，直接传给 AddArc 的起始角。

---

## 5. 数值合同（供视觉线校对）

| 行为 | 数值 |
|---|---|
| 出生总时长 | 0.445 s（合同 0.42–0.50） |
| 预压 | 0.92 保持 60 ms |
| 形变 | 沿运动方向 8 %，τ = 0.16 s 衰减 |
| 落位过冲 | 约 4.6 %（ζ = 0.68），随后吸附 |
| 融合过冲 | 2 %，落地后 80 ms 归零 |
| 悬停 | 1.02 缩放 + Highlight 0.35 |
| 按压 | 0.94 缩放 + 亮度 −0.06 + 内阴影 0.18 |
| 呼吸 | 4 s 周期，±1.2 % |
| Agent 环 | 1/3 圈每秒（120°/s），仅 Idle |
