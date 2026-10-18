using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluidBar.App.Localization;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using AppControls = FluidBar.App.Controls;
using CoreIslandPage = FluidBar.Core.Activities.IslandPage;

namespace FluidBar.App.Island.Expanded;

/// <summary>Actions an expanded page can raise to the host (data-only; the host executes them).</summary>
public enum IslandPageAction
{
    /// <summary>No action.</summary>
    None,

    /// <summary>Open the settings window.</summary>
    OpenSettings,

    /// <summary>Open the marketplace (settings window, marketplace section).</summary>
    OpenMarket,

    /// <summary>Hide the island.</summary>
    HideIsland,

    /// <summary>Toggle media playback.</summary>
    TogglePlay,

    /// <summary>Previous track.</summary>
    PreviousTrack,

    /// <summary>Next track.</summary>
    NextTrack,

    /// <summary>Clear all notifications.</summary>
    ClearNotifications,

    /// <summary>Copy a notification verification code.</summary>
    CopyVerificationCode,

    /// <summary>Copy a clipboard history item.</summary>
    CopyClipboardItem,

    /// <summary>Toggle the pin state of a clipboard item.</summary>
    ToggleClipboardPin,

    /// <summary>Toggle a widget task.</summary>
    ToggleTask,

    /// <summary>Show the previous calendar month.</summary>
    PreviousMonth,

    /// <summary>Show the next calendar month.</summary>
    NextMonth,

    /// <summary>Open an agent session.</summary>
    OpenAgent,
}

/// <summary>One page action request plus the row index it applies to (-1 when not row-specific).</summary>
/// <param name="Action">Action identity.</param>
/// <param name="Index">Row index, or -1.</param>
public readonly record struct IslandPageActionRequest(IslandPageAction Action, int Index);

/// <summary>
/// Base class for the six expanded pages. Pages draw imperatively at the uniform 392×284 content size
/// (440 shell − 48 rail), so switching pages never changes the shell size and never triggers layout of
/// the content beyond the fixed page root (spec 2.7 / lead decision).
/// </summary>
public abstract class IslandPageView : Control, ILocalizable
{
    /// <summary>Page content width (440 − 48 rail).</summary>
    public const double PageWidth = 400;

    /// <summary>Page content height (uniform for every page).</summary>
    public const double PageHeight = 252;

    /// <summary>Horizontal page inset.</summary>
    public const double Inset = 16;

    /// <summary>Usable content width (360).</summary>
    public const double UsableWidth = PageWidth - (2 * Inset);

    /// <summary>Page title baseline row (drawn by the pager overlay, above the page body).</summary>
    public const double TitleTop = 0;

    /// <summary>Page body start Y (below the title row).</summary>
    public const double BodyTop = 26;

    /// <summary>Page body bottom Y (8 DIP above the viewport edge; the body fills the remaining height).</summary>
    public const double BodyBottom = 244;

    /// <summary>Height of the page body band (218 DIP).</summary>
    public const double BodyHeight = BodyBottom - BodyTop;

    private ILocalizer? _localizer;

    /// <summary>Creates a page for <paramref name="page"/>.</summary>
    protected IslandPageView(CoreIslandPage page)
    {
        Page = page;
        IsHitTestVisible = false;
        Opacity = 0;
    }

    /// <summary>Rail page identity.</summary>
    public CoreIslandPage Page { get; }

    /// <summary>Latest page data snapshot.</summary>
    protected ExpandedPageContext Context { get; private set; } = null!;

    /// <summary>Current localizer.</summary>
    protected ILocalizer Localizer => _localizer ?? LocalizationRuntime.Current;

    /// <summary>Updates the page data and repaints (the page root is never recreated).</summary>
    public virtual void Update(ExpandedPageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        _localizer = context.Localizer;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public virtual void ApplyLocalization(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        _localizer = localizer;
        InvalidateVisual();
    }

    /// <summary>Maps a page-local point to an action request.</summary>
    public abstract IslandPageActionRequest HitTest(Point point);

    /// <summary>Automation name for the page root.</summary>
    public abstract string AutomationName { get; }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(PageWidth, PageHeight);

    /// <summary>Draws the page title row and an optional right-aligned action label.</summary>
    protected void DrawTitle(DrawingContext context, string title, string? rightAction = null)
    {
        var palette = Context.Settings.Colors;
        IslandDraw.Text(context, title, new Rect(Inset, TitleTop, UsableWidth - 80, 24),
            FluidBar.App.Theme.Fonts.DisplaySemiBold, FluidBar.App.Theme.Typography.PageTitleSize,
            new SolidColorBrush(palette.Foreground));
        if (!string.IsNullOrEmpty(rightAction))
        {
            IslandDraw.Text(context, rightAction, new Rect(PageWidth - Inset - 76, TitleTop, 76, 24),
                FluidBar.App.Theme.Fonts.SemiBold, FluidBar.App.Theme.Typography.CaptionSize,
                new SolidColorBrush(FluidBar.App.Theme.Accents.Info), TextAlignment.Right);
        }
    }

    /// <summary>Empty-state icon rectangle (centred in the page body band).</summary>
    public static Rect EmptyIconRect => new((PageWidth / 2) - 14, 96, 28, 28);

    /// <summary>Empty-state title rectangle.</summary>
    public static Rect EmptyTitleRect => new(Inset, 132, UsableWidth, 20);

    /// <summary>Empty-state hint rectangle.</summary>
    public static Rect EmptyHintRect => new(Inset, 154, UsableWidth, 18);

    /// <summary>Draws a centered empty state.</summary>
    protected void DrawEmpty(DrawingContext context, AppControls.IconKind icon, string title, string hint)
    {
        var palette = Context.Settings.Colors;
        AppControls.Icons.Draw(context, icon, EmptyIconRect, new SolidColorBrush(palette.ForegroundTertiary));
        IslandDraw.Text(context, title, EmptyTitleRect, FluidBar.App.Theme.Fonts.SemiBold,
            FluidBar.App.Theme.Typography.TitleSize, new SolidColorBrush(palette.ForegroundSecondary),
            TextAlignment.Center);
        IslandDraw.Text(context, hint, EmptyHintRect, FluidBar.App.Theme.Fonts.Regular,
            FluidBar.App.Theme.Typography.CaptionSize, new SolidColorBrush(palette.ForegroundTertiary),
            TextAlignment.Center);
    }
}
