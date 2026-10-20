using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluidBar.App.Controls;
using FluidBar.App.Localization;
using FluidBar.App.State;
using FluidBar.App.Theme;
using FluidBar.Core.Activities;
using CoreIslandPage = FluidBar.Core.Activities.PageRef;

namespace FluidBar.App.Island.Expanded;

/// <summary>Dashboard: clock, system status and quick actions.</summary>
public sealed class HomePage : IslandPageView
{
    private static readonly IslandPageAction[] QuickActions =
    [
        IslandPageAction.OpenSettings,
        IslandPageAction.OpenMarket,
        IslandPageAction.HideIsland,
    ];

    /// <summary>Creates the home page.</summary>
    public HomePage()
        : base(CoreIslandPage.Home)
    {
    }

    /// <inheritdoc />
    public override string AutomationName => Localizer.Get(LocKeys.Rail.Home);

    /// <inheritdoc />
    public override IslandPageActionRequest HitTest(Point point)
    {
        for (var i = 0; i < 3; i++)
        {
            if (QuickActionRect(i).Contains(point))
            {
                return new IslandPageActionRequest(QuickActions[i], i);
            }
        }

        return default;
    }

    /// <summary>Dashboard clock rectangle.</summary>
    public static Rect ClockRect => new(Inset, BodyTop, 172, 48);

    /// <summary>Dashboard date rectangle.</summary>
    public static Rect DateRect => new(Inset, BodyTop + 50, 176, 20);

    /// <summary>Dashboard greeting rectangle.</summary>
    public static Rect GreetingRect => new(Inset, BodyTop + 70, 176, 18);

    /// <summary>Status card rectangle for <paramref name="index"/>.</summary>
    public static Rect StatusCardRect(int index) => new(200, BodyTop + 4 + (index * 40), 184, 34);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (Context is null)
        {
            return;
        }

        var palette = Context.Settings.Colors;
        var fg = new SolidColorBrush(palette.Foreground);
        var secondary = new SolidColorBrush(palette.ForegroundSecondary);
        var tertiary = new SolidColorBrush(palette.ForegroundTertiary);

        var now = Context.Content.Now;
        var culture = Localizer.CurrentCulture;
        IslandDraw.Text(context, now.ToString("HH:mm", culture), ClockRect,
            Fonts.DisplayBold, Typography.DashboardClockSize, fg);
        IslandDraw.Text(context, now.ToString("dddd, MMMM d", culture), DateRect,
            Fonts.DisplaySemiBold, Typography.DashboardDateSize, secondary);
        IslandDraw.Text(context, Greeting(now.Hour), GreetingRect, Fonts.Regular,
            Typography.CaptionSize, tertiary);

        DrawStatusCards(context, palette, secondary);
        DrawQuickActions(context, palette, fg, secondary);
    }

    private void DrawStatusCards(DrawingContext context, IslandColors palette, IBrush secondary)
    {
        var cards = new (IconKind Icon, string Text, Color Accent, string Caption)[]
        {
            (
                Context.Content.IsCharging ? IconKind.Bolt : IconKind.Battery,
                Context.Content.BatteryPercent is { } battery
                    ? Localizer.Format(LocKeys.Island.Idle.Battery, ("level", battery))
                    : Localizer.Get(LocKeys.Common.Unknown),
                Context.Content.IsCharging ? Accents.Success : Accents.Fallback,
                Localizer.Get(LocKeys.Battery.Title)),
            (
                IconKind.Wifi,
                Context.Content.NetworkName ?? Localizer.Get(LocKeys.Common.Disconnected),
                Context.Content.IsOnline ? Accents.Success : Accents.Danger,
                Localizer.Get(LocKeys.Page.Home.System)),
            (
                IconKind.Focus,
                Localizer.Get(Context.Content.FocusOn ? LocKeys.Page.Home.FocusTime : LocKeys.Page.Home.FocusOff),
                Context.Content.FocusOn ? Accents.Info : Accents.Warning,
                Localizer.Get(LocKeys.Activity.Focus)),
        };

        for (var i = 0; i < cards.Length; i++)
        {
            var rect = StatusCardRect(i);
            IslandDraw.Card(context, rect, palette);
            Icons.Draw(context, cards[i].Icon, new Rect(rect.X + 8, rect.Y + 8, 16, 16),
                new SolidColorBrush(cards[i].Accent));
            IslandDraw.Text(context, cards[i].Text, new Rect(rect.X + 32, rect.Y, 104, rect.Height),
                Fonts.SemiBold, Typography.CaptionSize, new SolidColorBrush(palette.Foreground));
            IslandDraw.Text(context, cards[i].Caption, new Rect(rect.Right - 56, rect.Y, 48, rect.Height),
                Fonts.Regular, 10, secondary, TextAlignment.Right);
        }
    }

    private void DrawQuickActions(DrawingContext context, IslandColors palette, IBrush fg, IBrush secondary)
    {
        var icons = new[] { IconKind.Settings, IconKind.Store, IconKind.Close };
        var labels = new[]
        {
            Localizer.Get(LocKeys.Page.Home.OpenSettings),
            Localizer.Get(LocKeys.Page.Home.OpenMarket),
            Localizer.Get(LocKeys.Page.Home.HideIsland),
        };

        for (var i = 0; i < 3; i++)
        {
            var rect = QuickActionRect(i);
            IslandDraw.Card(context, rect, palette);
            Icons.Draw(context, icons[i], new Rect(rect.Center.X - 10, rect.Y + 14, 20, 20), fg);
            IslandDraw.Text(context, labels[i], new Rect(rect.X + 4, rect.Y + 48, rect.Width - 8, 22),
                Fonts.SemiBold, Typography.CaptionSize, secondary, TextAlignment.Center);
        }
    }

    /// <summary>Quick-action card rectangle for <paramref name="index"/> (fills the body down to y=244).</summary>
    public static Rect QuickActionRect(int index)
    {
        const double gap = 8;
        var width = (UsableWidth - (2 * gap)) / 3;
        return new Rect(Inset + (index * (width + gap)), BodyTop + 126, width, 92);
    }

    private string Greeting(int hour) => hour switch
    {
        < 12 => Localizer.Get(LocKeys.Page.Home.GreetingMorning),
        < 18 => Localizer.Get(LocKeys.Page.Home.GreetingAfternoon),
        _ => Localizer.Get(LocKeys.Page.Home.GreetingEvening),
    };
}

/// <summary>Media page: artwork, track metadata, seek bar, transport and lyrics.</summary>
public sealed class MediaPage : IslandPageView
{
    /// <summary>Artwork rectangle (64×64).</summary>
    public static Rect ArtRect => new(Inset, BodyTop, 64, 64);

    /// <summary>Track title rectangle.</summary>
    public static Rect TitleRect => new(96, BodyTop, 280, 22);

    /// <summary>Artist rectangle.</summary>
    public static Rect ArtistRect => new(96, BodyTop + 22, 280, 20);

    /// <summary>Album rectangle.</summary>
    public static Rect AlbumRect => new(96, BodyTop + 44, 280, 18);

    /// <summary>Seek track rectangle.</summary>
    public static Rect TrackRect => new(Inset, BodyTop + 76, UsableWidth, 4);

    /// <summary>Elapsed-time rectangle.</summary>
    public static Rect PositionRect => new(Inset, BodyTop + 84, 80, 16);

    /// <summary>Duration rectangle.</summary>
    public static Rect DurationRect => new(PageWidth - Inset - 80, BodyTop + 84, 80, 16);

    /// <summary>Previous-track hit rectangle.</summary>
    public static Rect PreviousRect => new(136, BodyTop + 92, 36, 36);

    /// <summary>Play/pause hit rectangle.</summary>
    public static Rect PlayRect => new(176, BodyTop + 88, 40, 40);

    /// <summary>Next-track hit rectangle.</summary>
    public static Rect NextRect => new(220, BodyTop + 92, 36, 36);

    /// <summary>Current lyric line rectangle.</summary>
    public static Rect LyricsRect => new(Inset, BodyTop + 162, UsableWidth, 26);

    /// <summary>Next lyric line rectangle.</summary>
    public static Rect NextLyricsRect => new(Inset, BodyTop + 188, UsableWidth, 22);

    /// <summary>Creates the media page.</summary>
    public MediaPage()
        : base(CoreIslandPage.Media)
    {
    }

    /// <inheritdoc />
    public override string AutomationName => Localizer.Get(LocKeys.Rail.Media);

    /// <inheritdoc />
    public override IslandPageActionRequest HitTest(Point point)
    {
        if (PlayRect.Contains(point))
        {
            return new IslandPageActionRequest(IslandPageAction.TogglePlay, -1);
        }

        if (PreviousRect.Contains(point))
        {
            return new IslandPageActionRequest(IslandPageAction.PreviousTrack, -1);
        }

        if (NextRect.Contains(point))
        {
            return new IslandPageActionRequest(IslandPageAction.NextTrack, -1);
        }

        return default;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (Context is null)
        {
            return;
        }

        var palette = Context.Settings.Colors;
        var fg = new SolidColorBrush(palette.Foreground);
        var secondary = new SolidColorBrush(palette.ForegroundSecondary);
        var tertiary = new SolidColorBrush(palette.ForegroundTertiary);
        var media = IslandPresenter.ResolveMedia(Context.Content, Context.Island);

        if (media is null)
        {
            DrawEmpty(context, IconKind.Media, Localizer.Get(LocKeys.Media.NoSession),
                Localizer.Get(LocKeys.Page.Media.NoSession));
            return;
        }

        var accent = Accents.ParseOrDefault(media.AccentHex, Accents.Fallback);
        IslandDraw.Artwork(context, ArtRect, accent);
        IslandDraw.Text(context, media.Title, TitleRect, Fonts.DisplaySemiBold,
            Typography.PageTitleSize, fg);
        IslandDraw.Text(context, media.Artist, ArtistRect, Fonts.Regular,
            Typography.SettingsBodySize, secondary);
        IslandDraw.Text(context, media.Album, AlbumRect, Fonts.Regular,
            Typography.CaptionSize, tertiary);

        IslandDraw.Track(context, TrackRect, media.ProgressPercent / 100.0, new SolidColorBrush(accent), palette);
        IslandDraw.Text(context, media.PositionText, PositionRect, Fonts.MonoRegular,
            Typography.CaptionSize, secondary);
        IslandDraw.Text(context, media.DurationText, DurationRect,
            Fonts.MonoRegular, Typography.CaptionSize, secondary, TextAlignment.Right);

        IslandDraw.IconButton(context, PreviousRect, 34, IconKind.Previous, fg, palette);
        IslandDraw.IconButton(context, PlayRect, 40, media.IsPlaying ? IconKind.Pause : IconKind.Play, fg, palette, elevated: true);
        IslandDraw.IconButton(context, NextRect, 34, IconKind.Next, fg, palette);

        if (!string.IsNullOrEmpty(media.LyricLine))
        {
            IslandDraw.Text(context, media.LyricLine, LyricsRect,
                Fonts.SemiBold, Typography.SettingsBodySize, secondary, TextAlignment.Center);
            IslandDraw.Text(context, media.NextLyricLine, NextLyricsRect,
                Fonts.Regular, Typography.CaptionSize, tertiary, TextAlignment.Center);
        }
        else if (!string.IsNullOrEmpty(media.Source))
        {
            IslandDraw.Text(context, Localizer.Format(LocKeys.Media.From, ("app", media.Source)),
                LyricsRect, Fonts.Regular, Typography.CaptionSize, tertiary, TextAlignment.Center);
        }
    }
}

/// <summary>Notifications page: latest notifications with verification-code chips.</summary>
public sealed class NotificationsPage : IslandPageView
{
    private const double RowHeight = 40;
    private const double RowGap = 4;

    /// <summary>Creates the notifications page.</summary>
    public NotificationsPage()
        : base(CoreIslandPage.Notifications)
    {
    }

    /// <inheritdoc />
    public override string AutomationName => Localizer.Get(LocKeys.Rail.Notifications);

    /// <inheritdoc />
    public override IslandPageActionRequest HitTest(Point point)
    {
        if (Context is null)
        {
            return default;
        }

        if (ClearRect.Contains(point) && !Context.Content.Notifications.IsDefaultOrEmpty)
        {
            return new IslandPageActionRequest(IslandPageAction.ClearNotifications, -1);
        }

        var count = Math.Min(Context.Content.Notifications.Length, 4);
        for (var i = 0; i < count; i++)
        {
            if (RowRect(i).Contains(point) && Context.Content.Notifications[i].VerificationCode is { Length: > 0 })
            {
                return new IslandPageActionRequest(IslandPageAction.CopyVerificationCode, i);
            }
        }

        return default;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (Context is null)
        {
            return;
        }

        var palette = Context.Settings.Colors;
        var fg = new SolidColorBrush(palette.Foreground);
        var secondary = new SolidColorBrush(palette.ForegroundSecondary);
        var tertiary = new SolidColorBrush(palette.ForegroundTertiary);
        var items = Context.Content.Notifications;

        if (items.IsDefaultOrEmpty)
        {
            DrawEmpty(context, IconKind.Bell, Localizer.Get(LocKeys.Notifications.Empty),
                Localizer.Get(LocKeys.Notifications.EmptyHint));
            return;
        }

        var count = Math.Min(items.Length, 5);
        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            var rect = RowRect(i);
            IslandDraw.Card(context, rect, palette);
            context.DrawEllipse(new SolidColorBrush(Accents.Info), null, new Point(rect.X + 12, rect.Center.Y), 3, 3);
            IslandDraw.Text(context, item.Title, new Rect(rect.X + 24, rect.Y + 2, 180, 19),
                Fonts.SemiBold, Typography.TitleSize, fg);
            IslandDraw.Text(context, item.Body, new Rect(rect.X + 24, rect.Y + 21, 224, 17),
                Fonts.Regular, Typography.CaptionSize, secondary);
            IslandDraw.Text(context, item.TimeText, new Rect(rect.Right - 60, rect.Y + 4, 48, 14),
                Fonts.Regular, 10, tertiary, TextAlignment.Right);
            if (item.VerificationCode is { Length: > 0 } code)
            {
                var chip = new Rect(rect.Right - 74, rect.Y + 20, 62, 16);
                IslandDraw.Chip(context, code, chip, Accents.Info, fg, 10);
            }
        }
    }

    /// <summary>"Clear" action rectangle in the title row.</summary>
    public static Rect ClearRect => new(PageWidth - Inset - 76, TitleTop, 76, 24);

    /// <summary>Notification row rectangle for <paramref name="index"/>.</summary>
    public static Rect RowRect(int index) => new(Inset, BodyTop + (index * (RowHeight + RowGap)), UsableWidth, RowHeight);
}

/// <summary>Clipboard history page.</summary>
public sealed class ClipboardPage : IslandPageView
{
    private const double RowHeight = 50;
    private const double RowGap = 6;

    /// <summary>Creates the clipboard page.</summary>
    public ClipboardPage()
        : base(CoreIslandPage.Clipboard)
    {
    }

    /// <inheritdoc />
    public override string AutomationName => Localizer.Get(LocKeys.Rail.Clipboard);

    /// <inheritdoc />
    public override IslandPageActionRequest HitTest(Point point)
    {
        if (Context is null)
        {
            return default;
        }

        var count = Math.Min(Context.Content.Clipboard.Length, 5);
        for (var i = 0; i < count; i++)
        {
            var rect = RowRect(i);
            if (PinRect(i).Contains(point))
            {
                return new IslandPageActionRequest(IslandPageAction.ToggleClipboardPin, i);
            }

            if (rect.Contains(point))
            {
                return new IslandPageActionRequest(IslandPageAction.CopyClipboardItem, i);
            }
        }

        return default;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (Context is null)
        {
            return;
        }

        var palette = Context.Settings.Colors;
        var fg = new SolidColorBrush(palette.Foreground);
        var secondary = new SolidColorBrush(palette.ForegroundSecondary);
        var tertiary = new SolidColorBrush(palette.ForegroundTertiary);
        var items = Context.Content.Clipboard;

        if (items.IsDefaultOrEmpty)
        {
            DrawEmpty(context, IconKind.Clipboard, Localizer.Get(LocKeys.Clipboard.Empty),
                Localizer.Get(LocKeys.Clipboard.EmptyHint));
            return;
        }

        var count = Math.Min(items.Length, 4);
        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            var rect = RowRect(i);
            IslandDraw.Card(context, rect, palette);
            Icons.Draw(context, item.IsImage ? IconKind.Media : IconKind.Document,
                new Rect(rect.X + 10, rect.Y + 17, 16, 16),
                new SolidColorBrush(item.Pinned ? Accents.Info : palette.ForegroundSecondary));
            IslandDraw.Text(context, item.Preview, new Rect(rect.X + 34, rect.Y + 8, rect.Width - 110, 20),
                Fonts.Regular, Typography.TitleSize, fg);
            IslandDraw.Text(context, item.Meta, new Rect(rect.X + 34, rect.Y + 29, 120, 14),
                Fonts.Regular, 10, tertiary);
            Icons.Draw(context, IconKind.Pin, new Rect(rect.Right - 30, rect.Y + 17, 16, 16),
                new SolidColorBrush(item.Pinned ? Accents.Info : palette.ForegroundTertiary));
        }
    }

    /// <summary>Clipboard row rectangle for <paramref name="index"/> (four rows fill the body band).</summary>
    public static Rect RowRect(int index) => new(Inset, BodyTop + (index * (RowHeight + RowGap)), UsableWidth, RowHeight);

    /// <summary>Pin hit rectangle for <paramref name="index"/>.</summary>
    public static Rect PinRect(int index)
    {
        var rect = RowRect(index);
        return new Rect(rect.Right - 36, rect.Y, 32, rect.Height);
    }
}

/// <summary>Widgets page: mini calendar and task list.</summary>
public sealed class WidgetsPage : IslandPageView
{
    private const double CalendarWidth = 206;
    private const double TaskX = Inset + CalendarWidth + 8;
    private const double TaskWidth = UsableWidth - CalendarWidth - 8;

    /// <summary>Creates the widgets page.</summary>
    public WidgetsPage()
        : base(CoreIslandPage.Widgets)
    {
    }

    /// <inheritdoc />
    public override string AutomationName => Localizer.Get(LocKeys.Rail.Widgets);

    /// <inheritdoc />
    public override IslandPageActionRequest HitTest(Point point)
    {
        if (Context is null)
        {
            return default;
        }

        if (CalendarNavRect(previous: true).Contains(point))
        {
            return new IslandPageActionRequest(IslandPageAction.PreviousMonth, -1);
        }

        if (CalendarNavRect(previous: false).Contains(point))
        {
            return new IslandPageActionRequest(IslandPageAction.NextMonth, -1);
        }

        var count = Math.Min(Context.Content.Tasks.Length, 4);
        for (var i = 0; i < count; i++)
        {
            if (TaskRect(i).Contains(point))
            {
                return new IslandPageActionRequest(IslandPageAction.ToggleTask, i);
            }
        }

        return default;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        if (Context is null)
        {
            return;
        }

        var palette = Context.Settings.Colors;
        var fg = new SolidColorBrush(palette.Foreground);
        var secondary = new SolidColorBrush(palette.ForegroundSecondary);
        var tertiary = new SolidColorBrush(palette.ForegroundTertiary);

        var calendar = CalendarRect;
        IslandDraw.Card(context, calendar, palette);
        IslandDraw.Text(context, Context.Content.CalendarMonthTitle, new Rect(calendar.X + 28, calendar.Y + 10, calendar.Width - 56, 18),
            Fonts.DisplaySemiBold, Typography.SettingsBodySize, fg, TextAlignment.Center);
        DrawCalendarNav(context, palette, previous: true);
        DrawCalendarNav(context, palette, previous: false);

        var days = Context.Content.CalendarDays;
        var headers = Context.Content.WeekdayHeaders;
        var gridTop = calendar.Y + 36;
        var cellWidth = (calendar.Width - 16) / 7;
        for (var i = 0; i < headers.Length; i++)
        {
            IslandDraw.Text(context, headers[i], new Rect(calendar.X + 8 + (i * cellWidth), gridTop, cellWidth, 16),
                Fonts.SemiBold, 9, tertiary, TextAlignment.Center);
        }

        var cellHeight = 22.0;
        for (var i = 0; i < Math.Min(days.Length, 42); i++)
        {
            var day = days[i];
            var column = i % 7;
            var row = i / 7;
            var cell = new Rect(calendar.X + 8 + (column * cellWidth), gridTop + 18 + (row * cellHeight), cellWidth, cellHeight);
            if (day.IsToday)
            {
                context.DrawEllipse(new SolidColorBrush(Accents.Fallback, 0.20), null, cell.Center, 9, 9);
            }

            if (day.HasEvents && !day.IsToday)
            {
                context.DrawEllipse(new SolidColorBrush(Accents.Info, 0.7), null, new Point(cell.Center.X, cell.Bottom - 3), 1.4, 1.4);
            }

            IslandDraw.Text(context, day.Day.ToString(Localizer.CurrentCulture), cell,
                day.IsToday ? Fonts.SemiBold : Fonts.Regular, 10,
                day.InMonth ? fg : tertiary, TextAlignment.Center);
        }

        var tasksCard = TasksRect;
        IslandDraw.Card(context, tasksCard, palette);
        IslandDraw.Text(context, Localizer.Get(LocKeys.Page.Widgets.Tasks), new Rect(tasksCard.X + 12, tasksCard.Y + 10, tasksCard.Width - 24, 16),
            Fonts.SemiBold, Typography.CaptionSize, new SolidColorBrush(palette.ForegroundSecondary));
        var tasks = Context.Content.Tasks;
        for (var i = 0; i < Math.Min(tasks.Length, 4); i++)
        {
            var task = tasks[i];
            var rect = TaskRect(i);
            var box = new Rect(rect.X + 12, rect.Center.Y - 7, 14, 14);
            if (task.Done)
            {
                context.DrawRectangle(new SolidColorBrush(Accents.Success), null, new RoundedRect(box, 4));
                Icons.Draw(context, IconKind.Check, box.Deflate(2.5), new SolidColorBrush(palette.Background));
            }
            else
            {
                context.DrawRectangle(null, new Pen(new SolidColorBrush(palette.ForegroundTertiary), 1.2),
                    new RoundedRect(box, 4));
            }

            IslandDraw.Text(context, task.Title, new Rect(box.Right + 8, rect.Y, rect.Width - 46, 36),
                Fonts.Regular, Typography.CaptionSize,
                task.Done ? tertiary : new SolidColorBrush(palette.Foreground));
        }
    }

    private void DrawCalendarNav(DrawingContext context, IslandColors palette, bool previous)
    {
        var rect = CalendarNavRect(previous);
        if (previous)
        {
            var mirror = Matrix.CreateTranslation(-(rect.X + (rect.Width / 2)), 0)
                * Matrix.CreateScale(-1, 1)
                * Matrix.CreateTranslation(rect.X + (rect.Width / 2), 0);
            using var transform = context.PushTransform(mirror);
            Icons.Draw(context, IconKind.ChevronRight, rect.Deflate(4),
                new SolidColorBrush(palette.ForegroundSecondary));
            return;
        }

        Icons.Draw(context, IconKind.ChevronRight, rect.Deflate(4),
            new SolidColorBrush(palette.ForegroundSecondary));
    }

    /// <summary>Calendar card rectangle (fills the body band).</summary>
    public static Rect CalendarRect => new(Inset, BodyTop, CalendarWidth, BodyHeight);

    /// <summary>Tasks card rectangle (fills the body band).</summary>
    public static Rect TasksRect => new(TaskX, BodyTop, TaskWidth, BodyHeight);

    /// <summary>Calendar month navigation rectangle.</summary>
    public static Rect CalendarNavRect(bool previous)
        => new(previous ? Inset + 6 : Inset + CalendarWidth - 26, BodyTop + 8, 20, 20);

    /// <summary>Task row rectangle for <paramref name="index"/>.</summary>
    public static Rect TaskRect(int index)
        => new(TaskX, BodyTop + 34 + (index * 44), TaskWidth, 40);
}


