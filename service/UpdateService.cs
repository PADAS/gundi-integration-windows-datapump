namespace service;

using System.Reflection;
using Microsoft.Extensions.Hosting;
using NLog;
using Velopack;
using Velopack.Sources;

/// <summary>
/// Wraps Velopack's UpdateManager. Exposes a small, UI-friendly surface
/// for the Status page: current version, "is this an updateable build?",
/// check, apply.
///
/// Two operating modes:
///   - Installed via Velopack: full check/download/apply pipeline.
///   - Running from source (`dotnet run`) or another non-Velopack
///     deployment: <see cref="IsInstalled"/> is false and the UI hides
///     the update affordances. Reading <see cref="CurrentVersion"/>
///     still works (falls back to the assembly's informational version).
/// </summary>
public class UpdateService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly UpdateManager? _manager;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly string? _feedUrl;

    /// <summary>
    /// Reason why <see cref="_manager"/> is null, for the UI. Distinguishes
    /// "feed not configured" from "feed configured but UpdateManager
    /// constructor threw" -- the latter is logged but the operator
    /// previously saw the same misleading "feed not configured" message
    /// either way.
    /// </summary>
    private readonly string? _initError;

    private UpdateInfo? _pendingUpdate;

    public UpdateService(IConfiguration config, IHostApplicationLifetime lifetime)
    {
        _lifetime = lifetime;

        // Feed URL is operator-overridable via "Updates:FeedUrl" in
        // appsettings.json. The default points at the canonical GCS
        // bucket where publish-to-gcs.ps1 will eventually drop the
        // Velopack release artifacts (RELEASES + .nupkg files).
        _feedUrl = config["Updates:FeedUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(_feedUrl))
        {
            _feedUrl = "https://storage.googleapis.com/radio-connectors/velopack/";
        }

        try
        {
            _manager = new UpdateManager(new SimpleWebSource(_feedUrl));
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to initialize Velopack UpdateManager.");
            _initError = $"Update manager unavailable; see logs ({ex.GetType().Name}: {ex.Message}).";
        }
    }

    /// <summary>True if the current process is running from a Velopack-managed install.</summary>
    public bool IsInstalled => _manager?.IsInstalled ?? false;

    /// <summary>The configured update feed URL, for display.</summary>
    public string? FeedUrl => _feedUrl;

    /// <summary>
    /// Current version. Reads Velopack's recorded install version when
    /// available (most accurate); falls back to the assembly's
    /// <see cref="AssemblyInformationalVersionAttribute"/> when running
    /// outside a Velopack install (e.g. <c>dotnet run</c>).
    /// </summary>
    public string CurrentVersion =>
        _manager?.CurrentVersion?.ToString()
        ?? typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    /// <summary>Whether a CheckAsync has reported a pending update awaiting Apply.</summary>
    public bool HasPendingUpdate => _pendingUpdate is not null;

    public async Task<UpdateCheckOutcome> CheckAsync()
    {
        if (_manager is null)
            return new UpdateCheckOutcome(false, null,
                _initError ?? "Update feed not configured.", false);

        if (!_manager.IsInstalled)
            return new UpdateCheckOutcome(false, null,
                "Updates unavailable: this build is not installed via Velopack.", false);

        try
        {
            _pendingUpdate = await _manager.CheckForUpdatesAsync();
            if (_pendingUpdate is null)
                return new UpdateCheckOutcome(false, null, null, UpToDate: true);

            return new UpdateCheckOutcome(
                HasUpdate: true,
                AvailableVersion: _pendingUpdate.TargetFullRelease.Version.ToString(),
                Error: null,
                UpToDate: false);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Update check failed.");
            return new UpdateCheckOutcome(false, null, $"{ex.GetType().Name}: {ex.Message}", false);
        }
    }

    /// <summary>
    /// Downloads the pending update and queues the file swap to run after
    /// this process exits, then signals the host to stop. Windows Service
    /// Manager (with the recovery actions configured at /install time)
    /// brings us back on the new version.
    /// </summary>
    public async Task<UpdateApplyOutcome> ApplyAsync()
    {
        if (_manager is null) return new UpdateApplyOutcome(false,
            _initError ?? "Update feed not configured.");
        if (!_manager.IsInstalled) return new UpdateApplyOutcome(false, "Not installed via Velopack.");
        if (_pendingUpdate is null) return new UpdateApplyOutcome(false, "No pending update — run a check first.");

        try
        {
            await _manager.DownloadUpdatesAsync(_pendingUpdate);

            // For services we want the swap to happen after THIS process
            // exits, not an in-process apply-and-restart. The updater
            // queues itself, watches the parent PID, applies the swap on
            // exit, and respawns.
            _manager.WaitExitThenApplyUpdates(_pendingUpdate);

            _logger.Info("Update applied; signalling host to stop so the swap can complete.");
            _lifetime.StopApplication();

            return new UpdateApplyOutcome(true,
                "Update downloaded. The service is restarting on the new version.");
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Apply failed.");
            return new UpdateApplyOutcome(false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}

public record UpdateCheckOutcome(bool HasUpdate, string? AvailableVersion, string? Error, bool UpToDate);
public record UpdateApplyOutcome(bool Success, string? Message);
