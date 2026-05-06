namespace service;

using CliWrap;
using NLog;

/// <summary>
/// Single source of truth for Windows service registration. Used by:
///   - The legacy /install and /uninstall command-line arguments (manual
///     setup from a portable zip).
///   - The Velopack install and uninstall hooks (auto-update path).
///
/// Both call into the same Register/Unregister methods so the SCM
/// configuration (auto-start, recovery actions, description) is
/// guaranteed to match across distribution channels.
/// </summary>
public static class ServiceManager
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    public const string ServiceName = "Gundi Radio Service";
    public const string ExecutableName = "RadioService.exe";

    private const string Description =
        "A Gundi/EarthRanger service that reads radio location data from a local database.";

    // The embedded UI is bound to localhost:8080 (see Program.cs); the
    // shortcut just opens the operator's default browser there. Kept
    // in sync by hand -- if the bind ever moves off 8080 this string
    // and the Kestrel ListenLocalhost call need to change together.
    private const string LocalUiUrl = "http://localhost:8080/";

    // Naming and location of the public-desktop shortcut. CommonDesktopDirectory
    // is the All Users desktop -- the shortcut appears for every account on
    // the machine, which matches the install model (technical advisor sets
    // up once, any operator who later logs in sees the icon).
    private const string ShortcutFileName = "Gundi Radio Service.url";

    /// <summary>
    /// Creates the service (or reuses an existing entry with the same
    /// name), configures recovery actions, sets the description, and
    /// starts it. Idempotent — re-running on an already-installed
    /// service is a no-op for create and overwrites the rest.
    /// </summary>
    /// <param name="exePath">Absolute path to RadioService.exe to register as binPath.</param>
    /// <returns>True on success, false if any step failed.</returns>
    public static async Task<bool> RegisterAsync(string exePath)
    {
        try
        {
            // Quote the exe path so SCM treats it as a single token. Most
            // installs land in "C:\Program Files\..." or
            // "C:\Program Files (x86)\..." which contain spaces; without
            // the quotes, sc.exe leaves the unquoted path in registry and
            // the next service start fails with "file not found".
            var create = await Cli.Wrap("sc")
                .WithArguments(new[] {
                    "create", ServiceName,
                    $"binPath= \"{exePath}\"",
                    "start=auto",
                    $"displayname={ServiceName}"
                })
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync();

            // Exit codes:
            //   0    = service created
            //   1073 = a service with that name already exists
            int[] ok = { 0, 1073 };
            if (!ok.Contains(create.ExitCode))
            {
                logger.Error("sc create failed with exit code {0}", create.ExitCode);
                return false;
            }

            // If the service already existed, its binPath might point at a
            // stale location (e.g. a prior per-user Velopack install in
            // %LocalAppData%, which is invisible to LocalSystem). Always
            // overwrite the binPath / start / displayname via sc config
            // so a fresh registration corrects any drift.
            if (create.ExitCode == 1073)
            {
                var config = await Cli.Wrap("sc")
                    .WithArguments(new[] {
                        "config", ServiceName,
                        $"binPath= \"{exePath}\"",
                        "start=auto",
                        $"displayname={ServiceName}"
                    })
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteAsync();

                if (config.ExitCode != 0)
                {
                    logger.Error("sc config failed with exit code {0} (existing binPath may be stale)", config.ExitCode);
                    return false;
                }
                logger.Info("Service '{0}' already existed; updated binPath to {1}.", ServiceName, exePath);
            }

            // sc failure / sc description exit codes are now actually
            // checked. A non-zero here means the service exists but is
            // missing recovery actions or description — not a strictly
            // catastrophic state, but enough that a wholesale "registered
            // OK" return would be a lie. We log + return false so the
            // caller (Velopack OnAfterInstall hook or the /install arg
            // path) can surface the failure rather than declaring success
            // with a half-configured service.
            var failure = await Cli.Wrap("sc").WithArguments(new[] {
                "failure", ServiceName,
                "reset=0",
                "actions=restart/60000/restart/120000/restart/180000"
            }).WithValidation(CommandResultValidation.None).ExecuteAsync();
            if (failure.ExitCode != 0)
            {
                logger.Error("sc failure failed with exit code {0}; recovery actions are unconfigured.",
                    failure.ExitCode);
                return false;
            }

            var description = await Cli.Wrap("sc").WithArguments(new[] {
                "description", ServiceName, Description
            }).WithValidation(CommandResultValidation.None).ExecuteAsync();
            if (description.ExitCode != 0)
            {
                logger.Error("sc description failed with exit code {0}; description is missing.",
                    description.ExitCode);
                return false;
            }

            // Start is best-effort: if the service is already running (1073
            // case above), `sc start` returns non-zero — we don't want that
            // to flip the overall outcome to failure. Logged for visibility.
            var start = await Cli.Wrap("sc")
                .WithArguments(new[] { "start", ServiceName })
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync();
            if (start.ExitCode != 0)
            {
                logger.Info("sc start exit code {0} (typically 1056 = already running, ignored).",
                    start.ExitCode);
            }

            logger.Info("Service '{0}' registered and started (binPath: {1}).", ServiceName, exePath);

            // Drop a "Gundi Radio Service" shortcut on the All Users desktop
            // so the operator has an obvious one-click path to the embedded
            // UI. Best-effort: a failure here is logged but doesn't flip
            // the registration outcome to failure -- the service itself is
            // up, the operator can still type the URL, the icon is a
            // convenience.
            try
            {
                EnsureDesktopShortcut(exePath);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed to create desktop shortcut (service is up regardless)");
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to register service");
            return false;
        }
    }

    /// <summary>
    /// Writes (or overwrites) the public-desktop .url shortcut that opens
    /// the embedded UI. Uses the simple Internet Shortcut format -- no
    /// WScript.Shell COM dance required, just plain text. IconFile points
    /// at the registered exe so Windows picks up its embedded icon.
    /// </summary>
    private static void EnsureDesktopShortcut(string exePath)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        if (string.IsNullOrEmpty(desktop))
        {
            // GetFolderPath returns "" rather than throwing when the folder
            // doesn't exist; on a default Windows install it always does,
            // but guard anyway so a weird machine doesn't crash registration.
            logger.Warn("CommonDesktopDirectory unavailable; skipping shortcut.");
            return;
        }

        var shortcutPath = Path.Combine(desktop, ShortcutFileName);

        // .url is INI-shaped: [InternetShortcut] header, URL=..., optional
        // IconFile/IconIndex. ASCII-only on the ANSI codepage; our exe
        // path can contain spaces but no characters that need escaping
        // here.
        var contents =
            "[InternetShortcut]\r\n" +
            $"URL={LocalUiUrl}\r\n" +
            $"IconFile={exePath}\r\n" +
            "IconIndex=0\r\n";

        File.WriteAllText(shortcutPath, contents);
        logger.Info("Desktop shortcut written to {0}", shortcutPath);
    }

    /// <summary>
    /// Removes the public-desktop shortcut if present. Best-effort --
    /// missing file is fine, IO errors during uninstall shouldn't block
    /// the service tear-down.
    /// </summary>
    private static void RemoveDesktopShortcut()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (string.IsNullOrEmpty(desktop)) return;
            var shortcutPath = Path.Combine(desktop, ShortcutFileName);
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
                logger.Info("Desktop shortcut removed from {0}", shortcutPath);
            }
        }
        catch (Exception ex)
        {
            logger.Warn(ex, "Failed to remove desktop shortcut (uninstall continues)");
        }
    }

    /// <summary>
    /// Stops and deletes the service. Idempotent — `sc stop` on an
    /// already-stopped service and `sc delete` on a non-existent service
    /// both return non-zero exit codes that we tolerate.
    /// </summary>
    public static async Task UnregisterAsync()
    {
        try
        {
            await Cli.Wrap("sc")
                .WithArguments(new[] { "stop", ServiceName })
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync();

            await Cli.Wrap("sc")
                .WithArguments(new[] { "delete", ServiceName })
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync();

            // Drop the desktop shortcut alongside the service. We do this
            // after sc delete rather than before so a failed unregister
            // (rare, but possible) doesn't leave an orphan icon pointing
            // at a still-running service.
            RemoveDesktopShortcut();

            logger.Info("Service '{0}' stopped and deleted.", ServiceName);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to unregister service");
        }
    }
}
