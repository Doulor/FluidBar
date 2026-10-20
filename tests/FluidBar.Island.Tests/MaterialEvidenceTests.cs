using System.Globalization;
using System.Text;
using System.Text.Json;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Deterministic evidence for the material stack. Skipped unless FLUIDBAR_MATERIAL_EVIDENCE=1; writes
/// build/reports/ui-audit/material/layers.png (five-layer diagram) plus contrast.md/contrast.json with the
/// WCAG table the audit references.
/// </summary>
public sealed class MaterialEvidenceTests
{
    private const int Width = 1600;
    private const int Height = 1800;
    private const float Scale = 2f;

    [Fact]
    public void Emit_material_layer_diagram_and_contrast_table()
    {
        if (Environment.GetEnvironmentVariable("FLUIDBAR_MATERIAL_EVIDENCE") != "1")
        {
            return;
        }

        var dir = Environment.GetEnvironmentVariable("FLUIDBAR_MATERIAL_DIR")
            ?? Path.Combine(IslandTestFixtures.RepoRoot, "build", "reports", "ui-audit", "material");
        Directory.CreateDirectory(dir);

        var tokens = MaterialTokens.Dark;
        using (var bitmap = RenderDiagram(tokens))
        {
            IslandTestFixtures.SavePng(bitmap, Path.Combine(dir, "layers.png"));
        }

        File.WriteAllText(Path.Combine(dir, "contrast.md"), BuildMarkdown(tokens), Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "contrast.json"), BuildJson(tokens), Encoding.UTF8);

        // The evidence must be self-consistent: every measured row has to pass before it is published.
        tokens.EnsureContrast();
    }

    private static SKBitmap RenderDiagram(MaterialTokens tokens)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(0x05, 0x05, 0x07));
        canvas.Scale(Scale);

        const float pageWidth = Width / Scale;
        const float pageHeight = Height / Scale;

        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0xED, 0xED, 0xEE), Style = SKPaintStyle.Fill };
        using var muted = new SKPaint { IsAntialias = true, Color = new SKColor(0x9B, 0x9B, 0xA5), Style = SKPaintStyle.Fill };
        using var accent = new SKPaint { IsAntialias = true, Color = new SKColor(0x4C, 0xC9, 0xF0), Style = SKPaintStyle.Fill };

        Text(canvas, "FluidBar material layers — L0–L4", 24, 30, 16, paint, bold: true);
        Text(canvas, "docs/ios-design-study.md §2 · rendered from FluidBar.Island.MaterialTokens", 24, 48, 9, muted);

        var left = 24f;
        var top = 66f;
        var panelWidth = 752f;
        var panelHeight = 104f;
        var gap = 10f;
        var labels = new[]
        {
            $"L0 container  {MaterialTokens.Hex(tokens.L0.Color)}  opaque · no gradient · no accent",
            $"L1 card  {MaterialTokens.Hex(tokens.L1.Color)}  {tokens.L1.Opacity * 100:0} % · radius {tokens.L1.CornerRadiusDip:0}",
            $"L2 separator  {MaterialTokens.Hex(tokens.L2.Color)}  {tokens.L2.Opacity * 100:0} % · {tokens.L2.StrokeWidthDip:0} DIP",
            $"L3 top highlight  {MaterialTokens.Hex(tokens.L3.Color)}  {tokens.L3.Opacity * 100:0} % → transparent · {tokens.L3.HeightDip:0.#} DIP",
            $"L4 pressed inset  {MaterialTokens.Hex(tokens.L4.Color)}  {tokens.L4.Opacity * 100:0} % · top inset {tokens.L4.HeightDip:0} DIP",
        };

        using var paintSet = new IslandMaterialPaintSet(tokens);
        for (var index = 0; index < 5; index++)
        {
            var y = top + ((panelHeight + gap) * index);
            var panel = SKRect.Create(left, y, panelWidth, panelHeight);
            using var panelFill = new SKPaint { IsAntialias = true, Color = new SKColor(0x0A, 0x0A, 0x0D), Style = SKPaintStyle.Fill };
            using var panelStroke = new SKPaint { IsAntialias = true, Color = new SKColor(0xFF, 0xFF, 0xFF, 0x14), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
            canvas.DrawRoundRect(panel, 10, 10, panelFill);
            canvas.DrawRoundRect(panel, 10, 10, panelStroke);

            var stage = SKRect.Create(panel.Left + 14, panel.Top + 30, panel.Width - 28, panel.Height - 44);
            DrawStage(canvas, paintSet, tokens, stage, index);

            Text(canvas, labels[index], panel.Left + 14, panel.Top + 19, 9.5f, paint);
            Text(canvas, LayerCaption(index), panel.Left + 14, panel.Bottom - 8, 8f, muted);
        }

        // Contrast table below the five panels (full width, so every column is readable).
        var report = tokens.CheckContrast();
        var tableTop = top + ((panelHeight + gap) * 5) + 6;
        var tableWidth = pageWidth - (left * 2);
        var table = SKRect.Create(left, tableTop, tableWidth, 196);
        using var tableFill = new SKPaint { IsAntialias = true, Color = new SKColor(0x0A, 0x0A, 0x0D), Style = SKPaintStyle.Fill };
        using var tableStroke = new SKPaint { IsAntialias = true, Color = new SKColor(0xFF, 0xFF, 0xFF, 0x14), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        canvas.DrawRoundRect(table, 10, 10, tableFill);
        canvas.DrawRoundRect(table, 10, 10, tableStroke);
        Text(canvas, "WCAG 2.2 contrast self-check (text on the composited layers)", table.Left + 16, table.Top + 24, 11, paint, bold: true);
        Text(canvas, "body >= 4.5:1 · secondary/tertiary >= 3.0:1", table.Left + 16, table.Top + 40, 8.5f, muted);

        var columns = new[] { 16f, 90f, 140f, 250f, 360f, 440f, 520f, 600f };
        var rowY = table.Top + 64;
        var headers = new[] { "role", "layer", "text", "background", "ratio", "required", "result" };
        for (var index = 0; index < headers.Length; index++)
        {
            Text(canvas, headers[index], table.Left + columns[index], rowY, 8f, muted);
        }

        rowY += 17;
        foreach (var row in report.Rows)
        {
            Text(canvas, row.Role, table.Left + columns[0], rowY, 8.5f, paint);
            Text(canvas, row.Layer, table.Left + columns[1], rowY, 8.5f, paint);
            Text(canvas, MaterialTokens.Hex(row.Text), table.Left + columns[2], rowY, 8.5f, muted);
            Text(canvas, MaterialTokens.Hex(row.Background), table.Left + columns[3], rowY, 8.5f, muted);
            Text(canvas, row.Rounded.ToString("0.00", CultureInfo.InvariantCulture) + ":1", table.Left + columns[4], rowY, 8.5f, row.Passes ? accent : paint);
            Text(canvas, row.Required.ToString("0.0", CultureInfo.InvariantCulture) + ":1", table.Left + columns[5], rowY, 8.5f, muted);
            Text(canvas, row.Passes ? "PASS" : "FAIL", table.Left + columns[6], rowY, 8.5f, row.Passes ? accent : paint);
            rowY += 15;
        }

        rowY += 10;
        Text(canvas, $"min body {report.MinimumBody:0.00}:1 · min secondary {report.MinimumSecondary:0.00}:1", table.Left + 16, rowY, 9f, paint);
        Text(canvas, report.AllPass ? "all rows PASS" : "FAIL", table.Left + 600, rowY, 10f, report.AllPass ? accent : paint, bold: true);
        rowY += 22;
        Text(canvas, "Spec bands: " + string.Join(" · ", MaterialSpecs.All.Select(static spec => spec.Reference)), table.Left + 16, rowY, 7.5f, muted);

        Text(canvas, $"generated {DateTimeOffset.Now:yyyy-MM-dd HH:mm} · theme {tokens.Name}", 24, pageHeight - 10, 8f, muted);
        canvas.Flush();
        return bitmap;
    }

    private static void DrawStage(SKCanvas canvas, IslandMaterialPaintSet paintSet, MaterialTokens tokens, SKRect stage, int layerIndex)
    {
        // L0 is the stage itself.
        using (var container = IslandMaterialBrushes.CreateContainerFill(tokens))
        {
            canvas.DrawRect(stage, container);
        }

        if (layerIndex == 0)
        {
            using var body = Paint(0xED, 0xED, 0xEE);
            using var muted = Paint(0x9B, 0x9B, 0xA5);
            Text(canvas, "container fill #08080A", stage.Left + 12, stage.Top + 26, 9, muted);
            Text(canvas, "body text #EDEDEE", stage.Left + 12, stage.Top + 44, 9, body);
            return;
        }

        var card = SKRect.Create(stage.Left + 10, stage.Top + 6, stage.Width - 20, stage.Height - 12);
        paintSet.DrawCard(canvas, card);
        using var cardText = Paint(0xED, 0xED, 0xEE);
        using var cardMuted = Paint(0x9B, 0x9B, 0xA5);
        Text(canvas, "card #141418 60 %", card.Left + 12, card.Top + 22, 9, cardText);
        Text(canvas, "secondary #9B9BA5", card.Left + 12, card.Top + 38, 9, cardMuted);

        switch (layerIndex)
        {
            case 2:
                for (var row = 1; row <= 3; row++)
                {
                    var y = card.Top + (row * (card.Height / 4f));
                    paintSet.DrawSeparator(canvas, card.Left + 12, card.Right - 12, y);
                }

                break;
            case 3:
                paintSet.DrawTopHighlight(canvas, card);
                break;
            case 4:
                paintSet.DrawPressedInset(canvas, card);
                Text(canvas, "pressed", card.Right - 72, card.Bottom - 14, 8.5f, cardMuted);
                break;
        }
    }

    private static string LayerCaption(int index) => index switch
    {
        0 => "outer container: near-black fill, no gradient, no accent border",
        1 => "section/card: #141418 at 55–65 %, radius 12, content owns colour",
        2 => "row separators: white 8–11 % at 1 DIP, only where grouping needs them",
        3 => "top edge micro-highlight: white 4–6 %, 1–2 DIP fading to transparent",
        4 => "pressed state: black 15–20 % inset from the top edge (6–10 DIP)",
        _ => string.Empty,
    };

    private static string BuildMarkdown(MaterialTokens tokens)
    {
        var report = tokens.CheckContrast();
        var builder = new StringBuilder();
        builder.AppendLine("# Material layers — L0–L4 (docs/ios-design-study.md section 2)").AppendLine();
        builder.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} · theme: {tokens.Name} ({tokens.Mode})").AppendLine();
        builder.AppendLine("## Tokens").AppendLine();
        builder.AppendLine("| layer | colour | opacity | geometry | spec |").AppendLine("| --- | --- | ---: | --- | --- |");
        foreach (var layer in tokens.Layers)
        {
            var spec = MaterialSpecs.For(layer.Layer);
            var geometry = layer.Layer switch
            {
                MaterialLayer.L0Container => "fill only",
                MaterialLayer.L1Card => $"radius {layer.CornerRadiusDip:0} DIP",
                MaterialLayer.L2Separator => $"stroke {layer.StrokeWidthDip:0} DIP",
                MaterialLayer.L3TopHighlight => $"{layer.HeightDip:0.#} DIP gradient to transparent",
                MaterialLayer.L4InnerShadow => $"top inset {layer.HeightDip:0} DIP, blur sigma {layer.BlurSigmaDip:0}",
                _ => string.Empty,
            };
            builder.AppendLine($"| {layer.Layer} | {layer.Hex} | {layer.Opacity * 100:0.#} % | {geometry} | {spec.Reference} |");
        }

        builder.AppendLine().AppendLine("## WCAG contrast self-check").AppendLine();
        builder.AppendLine("| role | layer | text | background | ratio | required | result |").AppendLine("| --- | --- | --- | --- | ---: | ---: | --- |");
        foreach (var row in report.Rows)
        {
            builder.AppendLine($"| {row.Role} | {row.Layer} | {MaterialTokens.Hex(row.Text)} | {MaterialTokens.Hex(row.Background)} | {row.Rounded:0.00}:1 | {row.Required:0.0}:1 | {(row.Passes ? "PASS" : "FAIL")} |");
        }

        builder.AppendLine().AppendLine("## Verdict").AppendLine();
        builder.AppendLine($"- body minimum: **{report.MinimumBody:0.00}:1** (>= 4.5)");
        builder.AppendLine($"- secondary minimum: **{report.MinimumSecondary:0.00}:1** (>= 3.0)");
        builder.AppendLine($"- all rows: **{(report.AllPass ? "PASS" : "FAIL")}**");
        builder.AppendLine($"- spec validation: **{(tokens.Validate().Count == 0 ? "clean" : string.Join("; ", tokens.Validate()))}**");
        return builder.ToString();
    }

    private static string BuildJson(MaterialTokens tokens)
    {
        var report = tokens.CheckContrast();
        var payload = new
        {
            theme = tokens.Name,
            mode = tokens.Mode.ToString(),
            layers = tokens.Layers.Select(layer => new
            {
                layer = layer.Layer.ToString(),
                color = layer.Hex,
                opacity = Math.Round(layer.Opacity, 4),
                cornerRadiusDip = layer.CornerRadiusDip,
                strokeWidthDip = layer.StrokeWidthDip,
                heightDip = layer.HeightDip,
                blurSigmaDip = layer.BlurSigmaDip,
                usesGradient = layer.UsesGradient,
                usesAccent = layer.UsesAccent,
            }).ToArray(),
            contrast = report.Rows.Select(row => new
            {
                role = row.Role,
                layer = row.Layer,
                text = MaterialTokens.Hex(row.Text),
                background = MaterialTokens.Hex(row.Background),
                ratio = Math.Round(row.Ratio, 3),
                required = row.Required,
                passes = row.Passes,
            }).ToArray(),
            minimumBody = Math.Round(report.MinimumBody, 3),
            minimumSecondary = Math.Round(report.MinimumSecondary, 3),
            allPass = report.AllPass,
            validation = tokens.Validate(),
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static SKPaint Paint(byte red, byte green, byte blue) =>
        new() { IsAntialias = true, Color = new SKColor(red, green, blue), Style = SKPaintStyle.Fill };

    private static void Text(SKCanvas canvas, string value, float x, float y, float size, SKPaint paint, bool bold = false)
    {
        using var typeface = SKTypeface.FromFamilyName("Segoe UI", bold ? SKFontStyle.Bold : SKFontStyle.Normal);
        using var font = new SKFont(typeface, size);
        canvas.DrawText(value, x, y, SKTextAlign.Left, font, paint);
    }
}
