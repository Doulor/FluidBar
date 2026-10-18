using System.Collections.ObjectModel;

namespace FluidBar.Platform.Windows.Diagnostics;

/// <summary>
/// Lock-free counters for the platform layer. Every counter is a monotonic <see cref="long"/>
/// (gauges are stored with <see cref="SetGauge"/>). Exposed in the diagnostics bundle so the
/// "no drops / no leaks" soak assertions in the rewrite spec can be verified at runtime.
/// </summary>
public sealed class PlatformCounters
{
    private long _logEntries;
    private long _logDropped;
    private long _logRateLimited;
    private long _logWriteFailures;
    private long _snapshotsPublished;
    private long _snapshotsCoalesced;
    private long _snapshotDispatchFailures;
    private long _monitorStartFailures;
    private long _monitorStopFailures;
    private long _monitorCallbackFailures;
    private long _comFailures;
    private long _win32Failures;
    private long _hotkeysRegistered;
    private long _hotkeyRegistrationFailures;
    private long _hotkeyPressed;
    private long _trayOperations;
    private long _trayOperationFailures;
    private long _clipboardEvents;
    private long _clipboardDeduped;
    private long _clipboardReadFailures;
    private long _clipboardHistoryEvictions;
    private long _notificationEvents;
    private long _notificationAccessDenied;
    private long _mediaSessionEvents;
    private long _mediaSnapshotChanges;
    private long _mediaSessionErrors;
    private long _lyricsFetches;
    private long _lyricsFetchFailures;
    private long _lyricsCacheHits;
    private long _lyricsCacheMisses;
    private long _lyricsCacheEvictions;
    private long _artworkCacheHits;
    private long _artworkCacheMisses;
    private long _artworkCacheEvictions;
    private long _artworkCacheRejected;
    private long _artworkTempSwept;
    private long _deviceEvents;
    private long _agentEvents;
    private long _updateChecks;
    private long _updateCheckFailures;
    private long _artworkCacheBytes;

    public void IncrementLogEntries() => Interlocked.Increment(ref _logEntries);
    public void IncrementLogDropped() => Interlocked.Increment(ref _logDropped);
    public void IncrementLogRateLimited() => Interlocked.Increment(ref _logRateLimited);
    public void IncrementLogWriteFailures() => Interlocked.Increment(ref _logWriteFailures);
    public void IncrementSnapshotsPublished() => Interlocked.Increment(ref _snapshotsPublished);
    public void IncrementSnapshotsCoalesced() => Interlocked.Increment(ref _snapshotsCoalesced);
    public void IncrementSnapshotDispatchFailures() => Interlocked.Increment(ref _snapshotDispatchFailures);
    public void IncrementMonitorStartFailures() => Interlocked.Increment(ref _monitorStartFailures);
    public void IncrementMonitorStopFailures() => Interlocked.Increment(ref _monitorStopFailures);
    public void IncrementMonitorCallbackFailures() => Interlocked.Increment(ref _monitorCallbackFailures);
    public void IncrementComFailures() => Interlocked.Increment(ref _comFailures);
    public void IncrementWin32Failures() => Interlocked.Increment(ref _win32Failures);
    public void IncrementHotkeysRegistered() => Interlocked.Increment(ref _hotkeysRegistered);
    public void IncrementHotkeyRegistrationFailures() => Interlocked.Increment(ref _hotkeyRegistrationFailures);
    public void IncrementHotkeyPressed() => Interlocked.Increment(ref _hotkeyPressed);
    public void IncrementTrayOperations() => Interlocked.Increment(ref _trayOperations);
    public void IncrementTrayOperationFailures() => Interlocked.Increment(ref _trayOperationFailures);
    public void IncrementClipboardEvents() => Interlocked.Increment(ref _clipboardEvents);
    public void IncrementClipboardDeduped() => Interlocked.Increment(ref _clipboardDeduped);
    public void IncrementClipboardReadFailures() => Interlocked.Increment(ref _clipboardReadFailures);
    public void IncrementClipboardHistoryEvictions() => Interlocked.Increment(ref _clipboardHistoryEvictions);
    public void IncrementNotificationEvents() => Interlocked.Increment(ref _notificationEvents);
    public void IncrementNotificationAccessDenied() => Interlocked.Increment(ref _notificationAccessDenied);
    public void IncrementMediaSessionEvents() => Interlocked.Increment(ref _mediaSessionEvents);
    public void IncrementMediaSnapshotChanges() => Interlocked.Increment(ref _mediaSnapshotChanges);
    public void IncrementMediaSessionErrors() => Interlocked.Increment(ref _mediaSessionErrors);
    public void IncrementLyricsFetches() => Interlocked.Increment(ref _lyricsFetches);
    public void IncrementLyricsFetchFailures() => Interlocked.Increment(ref _lyricsFetchFailures);
    public void IncrementLyricsCacheHits() => Interlocked.Increment(ref _lyricsCacheHits);
    public void IncrementLyricsCacheMisses() => Interlocked.Increment(ref _lyricsCacheMisses);
    public void IncrementLyricsCacheEvictions() => Interlocked.Increment(ref _lyricsCacheEvictions);
    public void IncrementArtworkCacheHits() => Interlocked.Increment(ref _artworkCacheHits);
    public void IncrementArtworkCacheMisses() => Interlocked.Increment(ref _artworkCacheMisses);
    public void IncrementArtworkCacheEvictions() => Interlocked.Increment(ref _artworkCacheEvictions);
    public void IncrementArtworkCacheRejected() => Interlocked.Increment(ref _artworkCacheRejected);
    public void IncrementArtworkTempSwept() => Interlocked.Increment(ref _artworkTempSwept);
    public void IncrementDeviceEvents() => Interlocked.Increment(ref _deviceEvents);
    public void IncrementAgentEvents() => Interlocked.Increment(ref _agentEvents);
    public void IncrementUpdateChecks() => Interlocked.Increment(ref _updateChecks);
    public void IncrementUpdateCheckFailures() => Interlocked.Increment(ref _updateCheckFailures);
    public void SetArtworkCacheBytes(long bytes) => Interlocked.Exchange(ref _artworkCacheBytes, bytes);

    /// <summary>Immutable point-in-time view of every counter.</summary>
    public PlatformCountersSnapshot Snapshot() => new(
        LogEntries: Interlocked.Read(ref _logEntries),
        LogDropped: Interlocked.Read(ref _logDropped),
        LogRateLimited: Interlocked.Read(ref _logRateLimited),
        LogWriteFailures: Interlocked.Read(ref _logWriteFailures),
        SnapshotsPublished: Interlocked.Read(ref _snapshotsPublished),
        SnapshotsCoalesced: Interlocked.Read(ref _snapshotsCoalesced),
        SnapshotDispatchFailures: Interlocked.Read(ref _snapshotDispatchFailures),
        MonitorStartFailures: Interlocked.Read(ref _monitorStartFailures),
        MonitorStopFailures: Interlocked.Read(ref _monitorStopFailures),
        MonitorCallbackFailures: Interlocked.Read(ref _monitorCallbackFailures),
        ComFailures: Interlocked.Read(ref _comFailures),
        Win32Failures: Interlocked.Read(ref _win32Failures),
        HotkeysRegistered: Interlocked.Read(ref _hotkeysRegistered),
        HotkeyRegistrationFailures: Interlocked.Read(ref _hotkeyRegistrationFailures),
        HotkeyPressed: Interlocked.Read(ref _hotkeyPressed),
        TrayOperations: Interlocked.Read(ref _trayOperations),
        TrayOperationFailures: Interlocked.Read(ref _trayOperationFailures),
        ClipboardEvents: Interlocked.Read(ref _clipboardEvents),
        ClipboardDeduped: Interlocked.Read(ref _clipboardDeduped),
        ClipboardReadFailures: Interlocked.Read(ref _clipboardReadFailures),
        ClipboardHistoryEvictions: Interlocked.Read(ref _clipboardHistoryEvictions),
        NotificationEvents: Interlocked.Read(ref _notificationEvents),
        NotificationAccessDenied: Interlocked.Read(ref _notificationAccessDenied),
        MediaSessionEvents: Interlocked.Read(ref _mediaSessionEvents),
        MediaSnapshotChanges: Interlocked.Read(ref _mediaSnapshotChanges),
        MediaSessionErrors: Interlocked.Read(ref _mediaSessionErrors),
        LyricsFetches: Interlocked.Read(ref _lyricsFetches),
        LyricsFetchFailures: Interlocked.Read(ref _lyricsFetchFailures),
        LyricsCacheHits: Interlocked.Read(ref _lyricsCacheHits),
        LyricsCacheMisses: Interlocked.Read(ref _lyricsCacheMisses),
        LyricsCacheEvictions: Interlocked.Read(ref _lyricsCacheEvictions),
        ArtworkCacheHits: Interlocked.Read(ref _artworkCacheHits),
        ArtworkCacheMisses: Interlocked.Read(ref _artworkCacheMisses),
        ArtworkCacheEvictions: Interlocked.Read(ref _artworkCacheEvictions),
        ArtworkCacheRejected: Interlocked.Read(ref _artworkCacheRejected),
        ArtworkTempSwept: Interlocked.Read(ref _artworkTempSwept),
        DeviceEvents: Interlocked.Read(ref _deviceEvents),
        AgentEvents: Interlocked.Read(ref _agentEvents),
        UpdateChecks: Interlocked.Read(ref _updateChecks),
        UpdateCheckFailures: Interlocked.Read(ref _updateCheckFailures),
        ArtworkCacheBytes: Interlocked.Read(ref _artworkCacheBytes));
}

/// <summary>Serializable snapshot of <see cref="PlatformCounters"/> for the diagnostics bundle.</summary>
public readonly record struct PlatformCountersSnapshot(
    long LogEntries,
    long LogDropped,
    long LogRateLimited,
    long LogWriteFailures,
    long SnapshotsPublished,
    long SnapshotsCoalesced,
    long SnapshotDispatchFailures,
    long MonitorStartFailures,
    long MonitorStopFailures,
    long MonitorCallbackFailures,
    long ComFailures,
    long Win32Failures,
    long HotkeysRegistered,
    long HotkeyRegistrationFailures,
    long HotkeyPressed,
    long TrayOperations,
    long TrayOperationFailures,
    long ClipboardEvents,
    long ClipboardDeduped,
    long ClipboardReadFailures,
    long ClipboardHistoryEvictions,
    long NotificationEvents,
    long NotificationAccessDenied,
    long MediaSessionEvents,
    long MediaSnapshotChanges,
    long MediaSessionErrors,
    long LyricsFetches,
    long LyricsFetchFailures,
    long LyricsCacheHits,
    long LyricsCacheMisses,
    long LyricsCacheEvictions,
    long ArtworkCacheHits,
    long ArtworkCacheMisses,
    long ArtworkCacheEvictions,
    long ArtworkCacheRejected,
    long ArtworkTempSwept,
    long DeviceEvents,
    long AgentEvents,
    long UpdateChecks,
    long UpdateCheckFailures,
    long ArtworkCacheBytes)
{
    /// <summary>Flat name/value view, ready for JSON serialization.</summary>
    public ReadOnlyDictionary<string, long> ToDictionary() => new(new Dictionary<string, long>(StringComparer.Ordinal)
    {
        ["log.entries"] = LogEntries,
        ["log.dropped"] = LogDropped,
        ["log.rateLimited"] = LogRateLimited,
        ["log.writeFailures"] = LogWriteFailures,
        ["snapshot.published"] = SnapshotsPublished,
        ["snapshot.coalesced"] = SnapshotsCoalesced,
        ["snapshot.dispatchFailures"] = SnapshotDispatchFailures,
        ["monitor.startFailures"] = MonitorStartFailures,
        ["monitor.stopFailures"] = MonitorStopFailures,
        ["monitor.callbackFailures"] = MonitorCallbackFailures,
        ["platform.comFailures"] = ComFailures,
        ["platform.win32Failures"] = Win32Failures,
        ["hotkey.registered"] = HotkeysRegistered,
        ["hotkey.registrationFailures"] = HotkeyRegistrationFailures,
        ["hotkey.pressed"] = HotkeyPressed,
        ["tray.operations"] = TrayOperations,
        ["tray.operationFailures"] = TrayOperationFailures,
        ["clipboard.events"] = ClipboardEvents,
        ["clipboard.deduped"] = ClipboardDeduped,
        ["clipboard.readFailures"] = ClipboardReadFailures,
        ["clipboard.historyEvictions"] = ClipboardHistoryEvictions,
        ["notification.events"] = NotificationEvents,
        ["notification.accessDenied"] = NotificationAccessDenied,
        ["media.sessionEvents"] = MediaSessionEvents,
        ["media.snapshotChanges"] = MediaSnapshotChanges,
        ["media.sessionErrors"] = MediaSessionErrors,
        ["lyrics.fetches"] = LyricsFetches,
        ["lyrics.fetchFailures"] = LyricsFetchFailures,
        ["lyrics.cacheHits"] = LyricsCacheHits,
        ["lyrics.cacheMisses"] = LyricsCacheMisses,
        ["lyrics.cacheEvictions"] = LyricsCacheEvictions,
        ["artwork.cacheHits"] = ArtworkCacheHits,
        ["artwork.cacheMisses"] = ArtworkCacheMisses,
        ["artwork.cacheEvictions"] = ArtworkCacheEvictions,
        ["artwork.cacheRejected"] = ArtworkCacheRejected,
        ["artwork.tempSwept"] = ArtworkTempSwept,
        ["device.events"] = DeviceEvents,
        ["agent.events"] = AgentEvents,
        ["update.checks"] = UpdateChecks,
        ["update.checkFailures"] = UpdateCheckFailures,
        ["artwork.cacheBytes"] = ArtworkCacheBytes,
    });
}
