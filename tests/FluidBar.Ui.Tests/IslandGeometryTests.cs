using System.Collections.Immutable;
using Avalonia;
using Avalonia.Media;
using FluidBar.App.Island;
using FluidBar.App.Island.Expanded;
using FluidBar.Core.Layout;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Geometry regressions from the Skia island spike: connectable squircle outline, flow archipelago,
/// expanded page vertical rhythm and compact pill overflow.
/// </summary>
public sealed class IslandGeometryTests
{
    private const double Eps = 1e-6;

    [Fact]
    public async Task SquircleOutline_BuildsAClosedPathMatchingContains()
    {
        await AvaloniaTest.Run(() =>
        {
            var shell = new VisualRect(24, 24, 440, 244);
            var geometry = ShellGeometry.Create(shell, CornerRadii.Uniform(36));
            var outline = geometry.SampleOutline(15);

            var path = new StreamGeometry();
            using (var context = path.Open())
            {
                context.BeginFigure(new Point(outline[0].X, outline[0].Y), isFilled: true);
                for (var i = 1; i < outline.Length; i++)
                {
                    context.LineTo(new Point(outline[i].X, outline[i].Y));
                }

                context.EndFigure(true);
            }

            // The StreamGeometry reaches all four shell edges and closes back onto the left edge:
            // the first sample is on the left edge at the TL corner, the last is on the left edge at
            // the BL corner, so EndFigure(true) is the short left-edge closing segment.
            Assert.Equal(shell.X, path.Bounds.X, Eps);
            Assert.Equal(shell.Y, path.Bounds.Y, Eps);
            Assert.Equal(shell.Width, path.Bounds.Width, Eps);
            Assert.Equal(shell.Height, path.Bounds.Height, Eps);
            Assert.InRange(outline[0].X, shell.X - Eps, shell.X + 0.5);
            Assert.InRange(outline[^1].X, shell.X - Eps, shell.X + 0.5);
            Assert.InRange(outline[0].Y, shell.Y + 30, shell.Y + 60);
            Assert.InRange(outline[^1].Y, shell.Bottom - 60, shell.Bottom + 5);

            // Avalonia's headless Geometry.FillContains is only reliable for four-point paths, so
            // evaluate the same closed polygon (the StreamGeometry's source points) in managed code.
            // A crossed outline (the old corner order) fails this grid.
            Assert.True(PolygonContains(outline, shell.CenterX, shell.CenterY));
            Assert.True(PolygonContains(outline, shell.CenterX, shell.Y + 1));
            Assert.False(PolygonContains(outline, shell.X + 1, shell.Y + 1));
            Assert.False(PolygonContains(outline, shell.X - 2, shell.CenterY));

            for (var x = shell.X + 2; x < shell.Right - 2; x += 6)
            {
                for (var y = shell.Y + 2; y < shell.Bottom - 2; y += 6)
                {
                    if (NearOutline(new Point(x, y), outline, 2.0))
                    {
                        continue;
                    }

                    Assert.Equal(geometry.Contains(x, y), PolygonContains(outline, x, y));
                }
            }
        });
    }

    [Fact]
    public void ExpandedPages_FillTheBodyBandAndStayInsideTheViewport()
    {
        var pages = new (string Name, Rect[] Body)[]
        {
            ("home", Rects(
                HomePage.ClockRect, HomePage.DateRect, HomePage.GreetingRect,
                HomePage.StatusCardRect(0), HomePage.StatusCardRect(1), HomePage.StatusCardRect(2),
                HomePage.QuickActionRect(0), HomePage.QuickActionRect(1), HomePage.QuickActionRect(2))),
            ("media", Rects(
                MediaPage.ArtRect, MediaPage.TitleRect, MediaPage.ArtistRect, MediaPage.AlbumRect,
                MediaPage.TrackRect, MediaPage.PositionRect, MediaPage.DurationRect,
                MediaPage.PreviousRect, MediaPage.PlayRect, MediaPage.NextRect,
                MediaPage.LyricsRect, MediaPage.NextLyricsRect)),
            ("notifications", Rects(
                NotificationsPage.RowRect(0), NotificationsPage.RowRect(1), NotificationsPage.RowRect(2),
                NotificationsPage.RowRect(3), NotificationsPage.RowRect(4))),
            ("clipboard", Rects(
                ClipboardPage.RowRect(0), ClipboardPage.RowRect(1), ClipboardPage.RowRect(2), ClipboardPage.RowRect(3),
                ClipboardPage.PinRect(0), ClipboardPage.PinRect(3))),
            ("widgets", Rects(
                WidgetsPage.CalendarRect, WidgetsPage.TasksRect,
                WidgetsPage.CalendarNavRect(previous: true), WidgetsPage.CalendarNavRect(previous: false),
                WidgetsPage.TaskRect(0), WidgetsPage.TaskRect(1), WidgetsPage.TaskRect(2), WidgetsPage.TaskRect(3))),
        };

        foreach (var (name, body) in pages)
        {
            foreach (var rect in body)
            {
                Assert.True(rect.X >= -Eps && rect.Y >= -Eps, $"{name}: {rect} starts outside the page");
                Assert.True(rect.Right <= IslandPageView.PageWidth + Eps,
                    $"{name}: {rect} exceeds page width {IslandPageView.PageWidth}");
                Assert.True(rect.Bottom <= IslandPageView.PageHeight + Eps,
                    $"{name}: {rect} exceeds page height {IslandPageView.PageHeight}");
            }

            var top = body.Min(rect => rect.Y);
            var bottom = body.Max(rect => rect.Bottom);
            Assert.True(top <= IslandPageView.BodyTop + 2,
                $"{name}: body starts at y={top}, leaving a top dead band below {IslandPageView.BodyTop}");
            Assert.True(bottom >= IslandPageView.BodyBottom - 8,
                $"{name}: body ends at y={bottom}, leaving a bottom dead band above {IslandPageView.BodyBottom}");
        }

        // Title-row actions remain in the title row, not the body.
        Assert.True(NotificationsPage.ClearRect.Bottom <= IslandPageView.BodyTop);
        Assert.Equal(244, HomePage.QuickActionRect(0).Bottom, Eps);
        Assert.Equal(244, ClipboardPage.RowRect(3).Bottom, Eps);
        Assert.Equal(244, WidgetsPage.CalendarRect.Bottom, Eps);
        Assert.Equal(244, WidgetsPage.TasksRect.Bottom, Eps);
    }

    [Fact]
    public void EmptyStates_StayCentredInsideTheBodyBand()
    {
        var rects = Rects(IslandPageView.EmptyIconRect, IslandPageView.EmptyTitleRect, IslandPageView.EmptyHintRect);
        foreach (var rect in rects)
        {
            Assert.True(rect.Y >= IslandPageView.BodyTop);
            Assert.True(rect.Bottom <= IslandPageView.BodyBottom);
        }

        var centre = (rects.Min(rect => rect.Y) + rects.Max(rect => rect.Bottom)) / 2;
        var bodyCentre = (IslandPageView.BodyTop + IslandPageView.BodyBottom) / 2;
        Assert.InRange(centre, bodyCentre - 6, bodyCentre + 6);
    }

    [Fact]
    public void CompactNotificationSlots_DoNotOverlapAndStayInsideThePill()
    {
        var icon = NotificationCompactSurface.IconRect;
        var title = NotificationCompactSurface.TitleRect(hasChip: true);
        var body = NotificationCompactSurface.BodyRect(hasChip: true);
        var chip = NotificationCompactSurface.ChipRect;

        Assert.Equal(CompactPillLayout.IconSlot, icon.Width, Eps);
        Assert.Equal(CompactPillLayout.IconSlot, icon.Height, Eps);
        Assert.True(icon.Right + CompactPillLayout.IconGap <= title.X + Eps, "icon slot overlaps the title");
        Assert.True(title.Right + CompactPillLayout.ChipGap <= chip.X + Eps, "text column overlaps the chip");
        Assert.True(title.Width > 0 && body.Width > 0);

        AssertInsidePill(icon, NotificationCompactSurface.PillWidth, NotificationCompactSurface.PillHeight, "notification icon");
        AssertInsidePill(title, NotificationCompactSurface.PillWidth, NotificationCompactSurface.PillHeight, "notification title");
        AssertInsidePill(body, NotificationCompactSurface.PillWidth, NotificationCompactSurface.PillHeight, "notification body");
        AssertInsidePill(chip, NotificationCompactSurface.PillWidth, NotificationCompactSurface.PillHeight, "notification chip");

        var noChip = NotificationCompactSurface.TextColumn(hasChip: false);
        Assert.True(noChip.Width > NotificationCompactSurface.TextColumn(hasChip: true).Width);
        AssertInsidePill(noChip, NotificationCompactSurface.PillWidth, NotificationCompactSurface.PillHeight, "notification text (no chip)");
    }

    [Fact]
    public void CompactClipboardSlots_DoNotOverlapAndStayInsideThePill()
    {
        var icon = ClipboardCompactSurface.IconRect;
        var text = ClipboardCompactSurface.TextRect;
        var chip = ClipboardCompactSurface.ChipRect;

        Assert.Equal(CompactPillLayout.IconSlot, icon.Width, Eps);
        Assert.True(icon.Right + CompactPillLayout.IconGap <= text.X + Eps, "icon slot overlaps the text");
        Assert.True(text.Right + CompactPillLayout.ChipGap <= chip.X + Eps, "text column overlaps the chip");
        Assert.True(text.Width > 0);

        AssertInsidePill(icon, ClipboardCompactSurface.PillWidth, ClipboardCompactSurface.PillHeight, "clipboard icon");
        AssertInsidePill(text, ClipboardCompactSurface.PillWidth, ClipboardCompactSurface.PillHeight, "clipboard text");
        AssertInsidePill(chip, ClipboardCompactSurface.PillWidth, ClipboardCompactSurface.PillHeight, "clipboard chip");
    }

    private static Rect[] Rects(params Rect[] rects) => rects;

    private static void AssertInsidePill(Rect rect, double width, double height, string label)
    {
        Assert.True(rect.X >= -Eps && rect.Y >= -Eps, $"{label}: {rect} starts outside the pill");
        Assert.True(rect.Right <= width + Eps, $"{label}: {rect} exceeds pill width {width}");
        Assert.True(rect.Bottom <= height + Eps, $"{label}: {rect} exceeds pill height {height}");
    }

    private static bool NearOutline(Point point, ImmutableArray<VisualPoint> outline, double distance)
    {
        var squared = distance * distance;
        foreach (var sample in outline)
        {
            var dx = point.X - sample.X;
            var dy = point.Y - sample.Y;
            if ((dx * dx) + (dy * dy) <= squared)
            {
                return true;
            }
        }

        return false;
    }

    private static bool PolygonContains(ImmutableArray<VisualPoint> polygon, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var pi = polygon[i];
            var pj = polygon[j];
            if ((pi.Y > y) != (pj.Y > y)
                && x < (((pj.X - pi.X) * (y - pi.Y) / (pj.Y - pi.Y)) + pi.X))
            {
                inside = !inside;
            }
        }

        return inside;
    }
}




