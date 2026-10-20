using FluidBar.Island;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Pages;

namespace FluidBar.App.Hosting.Pages;

/// <summary>
/// M0 host page registry. The five built-in pages keep their existing renderers for now; plugin pages are
/// appended after them and use the host-owned list template. M1 replaces the built-in seed with the same
/// descriptor path.
/// </summary>
public sealed class PageRegistry
{
    public const int MaxPages = 12;
    public const string BuiltinPrefix = "builtin.";

    private readonly object _gate = new();
    private readonly List<Entry> _pages = [];
    private IReadOnlyList<string> _order = [];

    /// <summary>Raised after a page is registered, updated or removed (safe from any thread).</summary>
    public event EventHandler? Changed;

    /// <summary>Total page count including the five built-in pages.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return BuiltinDescriptors.Count + _pages.Count;
            }
        }
    }

    /// <summary>Registers a page owned by <paramref name="ownerId"/>.</summary>
    public bool RegisterPage(string ownerId, PageDescriptor descriptor, out string? error)
        => RegisterPage(ownerId, descriptor, null, out error);

    /// <summary>Registers a page with an explicit page-id namespace (trusted built-ins use <c>builtin</c>).</summary>
    public bool RegisterPage(string ownerId, PageDescriptor descriptor, string? pageNamespace, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(descriptor);
        var pageNamespaceId = pageNamespace ?? ownerId;
        var allowBuiltinNamespace = string.Equals(ownerId, "builtin", StringComparison.Ordinal)
            || (string.Equals(pageNamespace, "builtin", StringComparison.Ordinal)
                && ownerId.StartsWith("fluidbar-", StringComparison.Ordinal));
        if (!PageIdRules.TryValidateOwned(pageNamespaceId, descriptor.PageId, allowBuiltinNamespace, out error))
        {
            return false;
        }

        if (!PageTemplateRegistry.IsPluginTemplateAllowed(descriptor.TemplateId))
        {
            error = $"template '{descriptor.TemplateId}' is not available for plugins (supported: list)";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Resolve(descriptor.Title)) || descriptor.Order is < 0 or > 1000)
        {
            error = "page title/order is invalid";
            return false;
        }

        lock (_gate)
        {
            if (_pages.Count + BuiltinDescriptors.Count >= MaxPages)
            {
                error = $"page limit {MaxPages} reached";
                return false;
            }

            if (_pages.Count(page => string.Equals(page.OwnerId, ownerId, StringComparison.Ordinal)) >= 4)
            {
                error = "a plugin may register at most 4 pages";
                return false;
            }

            if (_pages.Any(page => string.Equals(page.OwnerId, ownerId, StringComparison.Ordinal)
                                   && string.Equals(page.Descriptor.PageId, descriptor.PageId, StringComparison.OrdinalIgnoreCase)))
            {
                error = "page id is already registered";
                return false;
            }

            _pages.Add(new Entry(ownerId, descriptor));
            _pages.Sort(static (left, right) =>
            {
                var order = left.Descriptor.Order.CompareTo(right.Descriptor.Order);
                return order != 0
                    ? order
                    : string.Compare(left.Descriptor.PageId, right.Descriptor.PageId, StringComparison.OrdinalIgnoreCase);
            });
        }

        error = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Publishes list template data for an already-registered page.</summary>
    public bool PublishPage(string ownerId, PageData data, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(data);
        if (!string.Equals(data.TemplateId, PageTemplates.List, StringComparison.Ordinal))
        {
            error = "data template must be 'list' in M0";
            return false;
        }

        if (data.List is null || data.List.Rows.Count > 20)
        {
            error = "list data is missing or has too many rows (max 20)";
            return false;
        }

        lock (_gate)
        {
            var page = _pages.FirstOrDefault(entry => string.Equals(entry.OwnerId, ownerId, StringComparison.Ordinal)
                                                      && string.Equals(entry.Descriptor.PageId, data.PageId, StringComparison.OrdinalIgnoreCase));
            if (page is null)
            {
                error = "page is not registered by this owner";
                return false;
            }

            page.Data = data;
        }

        error = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Removes a plugin page (owner-scoped).</summary>
    public bool UnregisterPage(string ownerId, string pageId)
    {
        bool removed;
        lock (_gate)
        {
            removed = _pages.RemoveAll(entry => string.Equals(entry.OwnerId, ownerId, StringComparison.Ordinal)
                                                && string.Equals(entry.Descriptor.PageId, pageId, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>Removes every page owned by a stopping/unloaded plugin.</summary>
    public bool UnregisterOwner(string ownerId)
    {
        bool removed;
        lock (_gate)
        {
            removed = _pages.RemoveAll(entry => string.Equals(entry.OwnerId, ownerId, StringComparison.Ordinal)) > 0;
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>Built-in page descriptors; the same descriptor path as plugin pages, with native templates.</summary>
    public static IReadOnlyList<PageDescriptor> BuiltinDescriptors { get; } =
    [
        new() { PageId = "builtin.home", Order = 0, Title = new LocalizedString("Control center"), ShortTitle = new LocalizedString("Home"), IconToken = "home", TemplateId = "builtin.home" },
        new() { PageId = "builtin.media", Order = 10, Title = new LocalizedString("Now playing"), ShortTitle = new LocalizedString("Media"), IconToken = "media", TemplateId = "builtin.media" },
        new() { PageId = "builtin.notifications", Order = 20, Title = new LocalizedString("Notifications"), ShortTitle = new LocalizedString("Notifications"), IconToken = "bell", TemplateId = PageTemplates.List },
        new() { PageId = "builtin.clipboard", Order = 30, Title = new LocalizedString("Clipboard"), ShortTitle = new LocalizedString("Clipboard"), IconToken = "clipboard", TemplateId = PageTemplates.List },
        new() { PageId = "builtin.widgets", Order = 50, Title = new LocalizedString("Widgets"), ShortTitle = new LocalizedString("Widgets"), IconToken = "calendar", TemplateId = PageTemplates.Calendar },
    ];

    /// <summary>Sets the user's persisted PageId order; unknown ids stay at the end.</summary>
    public void SetOrder(IReadOnlyList<string>? pageIds)
    {
        lock (_gate)
        {
            _order = pageIds?.Where(static id => !string.IsNullOrWhiteSpace(id)).ToArray() ?? [];
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns built-ins + registered plugins in user order (unknown ids kept after known ones).</summary>
    public IReadOnlyList<PageDescriptor> BuildPageDescriptors()
    {
        lock (_gate)
        {
            var all = BuiltinDescriptors.Concat(_pages.Select(static entry => entry.Descriptor)).ToArray();
            var ranks = _order.Select((id, index) => (id, index))
                .GroupBy(static pair => pair.id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First().index, StringComparer.OrdinalIgnoreCase);
            return all
                .Select(descriptor => (descriptor, rank: ranks.TryGetValue(descriptor.PageId, out var rank) ? rank : int.MaxValue))
                .OrderBy(static pair => pair.rank)
                .ThenBy(static pair => pair.descriptor.Order)
                .ThenBy(static pair => pair.descriptor.PageId, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => pair.descriptor)
                .ToArray();
        }
    }

    /// <summary>Immutable host model snapshot for the island; M0 list templates only.</summary>
    public IReadOnlyList<PluginPageModel> BuildPluginPages()
    {
        lock (_gate)
        {
            return _pages.Select(static entry => new PluginPageModel
            {
                PageId = entry.Descriptor.PageId,
                OwnerPluginId = entry.OwnerId,
                Title = Resolve(entry.Descriptor.Title),
                ShortTitle = entry.Descriptor.ShortTitle is null ? null : Resolve(entry.Descriptor.ShortTitle),
                IconToken = entry.Descriptor.IconToken,
                Rows = entry.Data?.List?.Rows.Select(static row => new PluginPageRowModel
                {
                    RowId = row.RowId,
                    IconToken = row.IconToken,
                    Title = row.Title,
                    Subtitle = row.Subtitle,
                    Value = row.Value,
                    Progress = row.Progress,
                    Badge = row.Badge,
                    RowCommandId = row.RowCommandId,
                    TrailingCommandId = row.TrailingCommandId,
                    TrailingLabel = row.TrailingLabel,
                }).ToArray() ?? [],
                EmptyTitle = entry.Data?.List?.EmptyTitle,
                EmptyHint = entry.Data?.List?.EmptyHint,
            }).ToArray();
        }
    }

    private static string Resolve(LocalizedString value)
        => value.Value
           ?? value.Translations.Values.FirstOrDefault()
           ?? string.Empty;

    private sealed class Entry(string ownerId, PageDescriptor descriptor)
    {
        public string OwnerId { get; } = ownerId;

        public PageDescriptor Descriptor { get; } = descriptor;

        public PageData? Data { get; set; }
    }
}

/// <summary>PageId validation shared by the provisional local page registry.</summary>
public static class PageIdRules
{
    private static readonly System.Text.RegularExpressions.Regex PageIdRegex = new(
        "^[a-z][a-z0-9-]*(\\.[a-z0-9_-]+){1,4}$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    public static bool TryValidateOwned(string ownerId, string pageId, out string? error)
        => TryValidateOwned(ownerId, pageId, allowBuiltinNamespace: false, out error);

    /// <summary>Validates a page id, optionally allowing the trusted built-in namespace.</summary>
    public static bool TryValidateOwned(string ownerId, string pageId, bool allowBuiltinNamespace, out string? error)
    {
        if (string.IsNullOrWhiteSpace(pageId) || pageId.Length > 64 || !PageIdRegex.IsMatch(pageId))
        {
            error = "page id must match ^[a-z][a-z0-9-]*(\\.[a-z0-9_-]+){1,4}$ and be <=64 chars";
            return false;
        }

        // The synthetic manifest PageOwnerId "builtin" is the one trusted namespace allowed to
        // publish the host's reserved page ids (the agent page moved into a built-in plugin in M2).
        if (pageId.StartsWith(PageRegistry.BuiltinPrefix, StringComparison.Ordinal)
            && !allowBuiltinNamespace)
        {
            error = "page id prefix 'builtin.' is reserved for host pages";
            return false;
        }

        if (!pageId.StartsWith(ownerId + ".", StringComparison.OrdinalIgnoreCase))
        {
            error = "page id must be prefixed with the owning plugin id";
            return false;
        }

        error = null;
        return true;
    }
}

