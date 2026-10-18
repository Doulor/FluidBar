using FluidBar.App.Localization;

namespace FluidBar.App.Views.Market;

/// <summary>
/// Catalog availability wrapper: the real signed-index service is used whenever it can be fetched and
/// verified; when it cannot (offline, HTTP error, signature failure) the deterministic design catalog is
/// shown instead, clearly labelled, and every mutating operation is refused. A demo install must never
/// pretend to have installed a plugin.
/// </summary>
public sealed class MarketplaceFallbackUiService : IMarketplaceUiService
{
    private readonly IMarketplaceUiService? _primary;
    private readonly IMarketplaceUiService _fallback;
    private readonly ILocalizer _localizer;
    private readonly object _gate = new();
    private bool _offline;

    /// <summary>Creates the wrapper; a null primary means "no marketplace is configured".</summary>
    public MarketplaceFallbackUiService(IMarketplaceUiService? primary, IMarketplaceUiService fallback, ILocalizer localizer)
    {
        _primary = primary;
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        if (_primary is not null)
        {
            _primary.Changed += OnPrimaryChanged;
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public bool IsOfflineFallback
    {
        get
        {
            lock (_gate)
            {
                return _offline || _primary is null;
            }
        }
    }

    /// <inheritdoc />
    public string? StatusMessage => IsOfflineFallback
        ? _localizer.Get(LocKeys.Settings.Market.Offline) + " " + _localizer.Get(LocKeys.Settings.Market.DemoCatalog)
        : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketPlugin>> BrowseAsync(MarketplaceQuery query, CancellationToken cancellationToken)
    {
        if (_primary is null)
        {
            return await BrowseDemoAsync(query, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var result = await _primary.BrowseAsync(query, cancellationToken).ConfigureAwait(false);
            SetOffline(false);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return await BrowseDemoAsync(query, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<MarketPlugin?> GetPluginAsync(string pluginId, CancellationToken cancellationToken)
    {
        if (_primary is null)
        {
            return await _fallback.GetPluginAsync(pluginId, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var result = await _primary.GetPluginAsync(pluginId, cancellationToken).ConfigureAwait(false);
            SetOffline(false);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            SetOffline(true);
            return await _fallback.GetPluginAsync(pluginId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task InstallAsync(string pluginId, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        var primary = RequirePrimary();
        return primary.InstallAsync(pluginId, progress, cancellationToken);
    }

    /// <inheritdoc />
    public Task RollbackAsync(string pluginId, string version, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        var primary = RequirePrimary();
        return primary.RollbackAsync(pluginId, version, progress, cancellationToken);
    }

    /// <inheritdoc />
    public Task UninstallAsync(string pluginId, bool keepSettings, CancellationToken cancellationToken)
    {
        var primary = RequirePrimary();
        return primary.UninstallAsync(pluginId, keepSettings, cancellationToken);
    }

    private IMarketplaceUiService RequirePrimary()
    {
        if (IsOfflineFallback || _primary is null)
        {
            throw new InvalidOperationException(_localizer.Get(LocKeys.Settings.Market.DemoCatalog));
        }

        return _primary;
    }

    private async Task<IReadOnlyList<MarketPlugin>> BrowseDemoAsync(MarketplaceQuery query, CancellationToken cancellationToken)
    {
        SetOffline(true);
        var items = await _fallback.BrowseAsync(query, cancellationToken).ConfigureAwait(false);
        return items.Select(static plugin => plugin with { IsDemo = true }).ToList();
    }

    private void SetOffline(bool value)
    {
        bool changed;
        lock (_gate)
        {
            changed = _offline != value;
            _offline = value;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPrimaryChanged(object? sender, EventArgs e) => Changed?.Invoke(this, e);
}
