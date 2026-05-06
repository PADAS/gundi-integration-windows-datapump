namespace service;

using System.Diagnostics;
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
        // bucket where publish-velopack.ps1 drops the Velopack release
        // artifacts (RELEASES + .nupkg + Setup.exe + .msi files).
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
    /// Downloads the pending update and orchestrates the file swap +
    /// service restart. Replaces an earlier attempt that called
    /// <c>WaitExitThenApplyUpdates</c>; that path silently no-ops in a
    /// Windows Service running as LocalSystem (Update.exe is never
    /// actually spawned, the swap never happens, and SCM eventually
    /// brings the service back on the OLD binary).
    ///
    /// New flow:
    ///   1. <see cref="UpdateManager.DownloadUpdatesAsync"/> stages the
    ///      .nupkg in <c>packages/</c>.
    ///   2. We spawn a detached <c>cmd.exe</c> orchestrator that runs:
    ///        Update.exe --verbose --log apply.log apply --waitPid &lt;us&gt; --norestart
    ///        sc start "Gundi Radio Service"
    ///      The orchestrator survives our exit because Windows doesn't
    ///      auto-kill orphaned children. <c>--waitPid</c> serializes the
    ///      swap behind our exit; <c>--norestart</c> stops Update.exe
    ///      from respawning a free LocalSystem process (which is the
    ///      problem the legacy path produced); <c>sc start</c> brings
    ///      the service back via SCM in proper service context.
    ///   3. We schedule <see cref="IHostApplicationLifetime.StopApplication"/>
    ///      a couple of seconds out so the HTTP response carrying the
    ///      success message reaches the operator's browser before the
    ///      SignalR circuit tears down.
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

            // Locate Update.exe -- one level up from current/ in the
            // standard Velopack install layout. AppContext.BaseDirectory
            // is typically "...\GundiRadioService\current\" for the
            // service binary; Update.exe lives in the install root.
            var currentDir = AppContext.BaseDirectory;
            var installRoot = Directory.GetParent(
                currentDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName;
            if (installRoot is null)
            {
                return new UpdateApplyOutcome(false,
                    $"Could not locate install root from base directory '{currentDir}'.");
            }
            var updateExe = Path.Combine(installRoot, "Update.exe");
            if (!File.Exists(updateExe))
            {
                return new UpdateApplyOutcome(false,
                    $"Update.exe not found at '{updateExe}'.");
            }

            var ourPid = Environment.ProcessId;
            var serviceName = ServiceManager.ServiceName;
            var applyLog = Path.Combine(installRoot, "apply.log");

            // Single `&` (not `&&`) so sc start runs whether or not the
            // apply step succeeded. If the apply works, SCM brings up
            // the new version; if it fails, SCM brings up the old
            // version and the operator sees "still on old version" --
            // which is no worse than the failure mode we have today,
            // and apply.log will tell us why the swap didn't take.
            //
            // Inner double-quoting is mandatory: cmd /c parses the rest
            // of the line and re-tokenizes; without surrounding quotes
            // the command path with spaces (Program Files (x86)) breaks.
            var orchCommand =
                $"\"\"{updateExe}\" --verbose --log \"{applyLog}\" apply --waitPid {ourPid} --norestart " +
                $"& sc start \"{serviceName}\"\"";

            _logger.Info("Spawning update orchestrator: cmd /c {0}", orchCommand);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + orchCommand,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = installRoot,
            };

            try
            {
                var orch = Process.Start(psi);
                if (orch is null)
                {
                    return new UpdateApplyOutcome(false,
                        "Failed to spawn update orchestrator (Process.Start returned null).");
                }
                _logger.Info("Update orchestrator spawned (cmd PID {0}; will wait on our PID {1}).",
                    orch.Id, ourPid);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to spawn update orchestrator.");
                return new UpdateApplyOutcome(false, $"Failed to spawn updater: {ex.Message}");
            }

            // Don't stop the host immediately: the HTTP response carrying
            // the success message hasn't been delivered yet. If we tear
            // down Kestrel synchronously, the operator's browser sees the
            // request fail and can't tell whether the update went through
            // (and may click Apply again). Schedule the stop a couple
            // seconds out, after the response has had time to flush over
            // the SignalR circuit, then return immediately.
            _logger.Info("Orchestrator queued; signalling host stop in 2s so the response can deliver first.");
            _ = DelayThenStopAsync(_lifetime, TimeSpan.FromSeconds(2));

            return new UpdateApplyOutcome(true,
                "Update downloaded. The service is restarting on the new version. " +
                "When the connection drops, click 'Refresh page' to reload on the new build.");
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Apply failed.");
            return new UpdateApplyOutcome(false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Fire-and-forget helper for ApplyAsync: waits the given delay, then
    /// stops the host. Pulled out into a named method so the intent
    /// (give the response time to deliver before tearing down) is clear
    /// at the call site rather than buried in a Task.Delay continuation.
    /// </summary>
    private static async Task DelayThenStopAsync(IHostApplicationLifetime lifetime, TimeSpan delay)
    {
        await Task.Delay(delay);
        lifetime.StopApplication();
    }
}

public record UpdateCheckOutcome(bool HasUpdate, string? AvailableVersion, string? Error, bool UpToDate);
public record UpdateApplyOutcome(bool Success, string? Message);
