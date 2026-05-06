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
            // Migrate any operator data sitting next to the previously-
            // registered service exe BEFORE we overwrite SCM's binPath. A
            // legacy install (pre-Velopack, pre-PROGRAMDATA-data-path) kept
            // appsettings.json + state.json next to the exe; the new code
            // path reads/writes them under %PROGRAMDATA% so they survive
            // future updates. By doing this here -- inside RegisterAsync,
            // before sc config rewrites binPath -- we still have a
            // reliable pointer to where the legacy data lives. Once
            // binPath has been overwritten, the trail is gone.
            //
            // This block is idempotent: MigrateLegacyDataIfNeeded checks
            // whether the destination already has files (in which case it
            // does nothing) and renames the legacy source files to .stale
            // after a successful copy so a future run won't re-migrate
            // and a curious admin can see at a glance which files are no
            // longer authoritative.
            await MigrateLegacyDataIfNeededAsync();

            // sc.exe argument layout note: pass `binpath=` as one argv slot
            // and the path as a SEPARATE argv slot. CliWrap quotes the path
            // automatically when it contains spaces (e.g. "Program Files
            // (x86)"), so SCM receives the path cleanly with no embedded
            // quote characters.
            //
            // The previous form passed a single combined arg
            //   $"binPath= \"{exePath}\""
            // which CliWrap re-escaped as "binPath= \"C:\\...\"". sc.exe's
            // parser then stored the value WITH the literal quote
            // characters as part of the binPath, and SCM's CreateProcess
            // failed with ERROR_INVALID_PARAMETER (87) because it couldn't
            // find a file literally named "C:\Program Files...".
            //
            // Same correction applies to `start=auto` and `displayname=`
            // below: keyword and value go in separate argv slots.
            var create = await Cli.Wrap("sc")
                .WithArguments(new[] {
                    "create", ServiceName,
                    "binPath=", exePath,
                    "start=", "auto",
                    "displayname=", ServiceName,
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
                // Same separate-argv-slot pattern as `sc create` above --
                // see the long comment there for why combined args break.
                var config = await Cli.Wrap("sc")
                    .WithArguments(new[] {
                        "config", ServiceName,
                        "binPath=", exePath,
                        "start=", "auto",
                        "displayname=", ServiceName,
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
    /// One-time migration of operator data (appsettings.json, state.json)
    /// from a legacy install location to <see cref="AppPaths.DataDirectory"/>.
    /// Called from <see cref="RegisterAsync"/> before binPath is rewritten
    /// — the only moment we still have a reliable pointer to where the
    /// previous install kept its data.
    ///
    /// Migration is gated on the destination being EMPTY: if
    /// %PROGRAMDATA%\GundiRadioService\appsettings.json already exists,
    /// the operator has either already been migrated or has populated
    /// data via the wizard, and we don't touch anything. The destination's
    /// existence is the sole "have we done this" marker — no separate
    /// flag file required.
    ///
    /// On a successful copy, the legacy source file is renamed to
    /// "<name>.stale" rather than deleted. This:
    ///   * Removes the footgun where an admin edits the legacy file
    ///     thinking it's the live config (it isn't anymore).
    ///   * Preserves the original data as a manual recovery option.
    ///   * Makes the migration visible — a tech looking at the legacy
    ///     directory immediately sees what happened.
    ///
    /// All steps are best-effort: a failure here logs but does not flip
    /// the overall registration outcome to failure. Worst case, the
    /// operator runs the wizard once.
    /// </summary>
    private static async Task MigrateLegacyDataIfNeededAsync()
    {
        try
        {
            var legacyDir = await TryGetLegacyInstallDirectoryAsync();
            if (legacyDir is null)
            {
                // No prior service registration -- fresh install, nothing
                // to migrate. Wizard will populate %PROGRAMDATA% on first
                // save.
                return;
            }
            if (string.Equals(
                    Path.GetFullPath(legacyDir),
                    Path.GetFullPath(AppPaths.DataDirectory),
                    StringComparison.OrdinalIgnoreCase))
            {
                // Pathological but possible: legacy install was somehow
                // pointed at %PROGRAMDATA% itself. Don't try to copy a
                // file onto itself.
                return;
            }

            AppPaths.EnsureDataDirectoryExists();
            MigrateOneFile(legacyDir, AppPaths.ConfigFileName);
            MigrateOneFile(legacyDir, AppPaths.StateFileName);
        }
        catch (Exception ex)
        {
            logger.Warn(ex, "Legacy data migration failed; service registration continues. " +
                            "Operator can run the first-run wizard to (re)create config.");
        }
    }

    /// <summary>
    /// Reads the existing service's binPath from SCM (if registered) and
    /// returns the directory it points at, which is where any legacy
    /// appsettings.json / state.json would have been kept. Returns null
    /// if the service isn't registered, or if sc.exe output can't be
    /// parsed.
    /// </summary>
    private static async Task<string?> TryGetLegacyInstallDirectoryAsync()
    {
        // Run sc.exe directly via Process.Start so we can capture stdout
        // (CliWrap's PipeStdoutTo path would also work but a throwaway
        // Process.Start is simpler here, and we only need a one-shot
        // read). Exit codes 0 = service exists, 1060 = doesn't exist.
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"qc \"{ServiceName}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = System.Diagnostics.Process.Start(psi);
        if (p is null) return null;
        var stdout = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) return null;

        // Look for "BINARY_PATH_NAME : <path>". sc.exe pads with spaces
        // and may surround the path with quotes if it contains them
        // (today's bug -- separately fixed). Strip surrounding quotes
        // defensively so this works on installs registered by any
        // historical version of our code.
        const string marker = "BINARY_PATH_NAME";
        var idx = stdout.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var lineEnd = stdout.IndexOf('\n', idx);
        var line = lineEnd < 0 ? stdout[idx..] : stdout[idx..lineEnd];

        var colonIdx = line.IndexOf(':');
        if (colonIdx < 0) return null;
        var raw = line[(colonIdx + 1)..].Trim().Trim('"').Trim();
        if (string.IsNullOrEmpty(raw)) return null;

        var dir = Path.GetDirectoryName(raw);
        return string.IsNullOrEmpty(dir) ? null : dir;
    }

    /// <summary>
    /// Copy <paramref name="fileName"/> from the legacy install directory
    /// to <see cref="AppPaths.DataDirectory"/>, then rename the legacy
    /// source to "<paramref name="fileName"/>.stale". Skips if the
    /// destination already exists or the source is missing.
    /// </summary>
    private static void MigrateOneFile(string legacyDir, string fileName)
    {
        var src = Path.Combine(legacyDir, fileName);
        var dst = Path.Combine(AppPaths.DataDirectory, fileName);

        if (File.Exists(dst))
        {
            // Destination wins. Either we already migrated (and the
            // legacy source might still be sitting unrenamed for some
            // reason), or the operator ran the wizard already. Don't
            // overwrite live data.
            return;
        }
        if (!File.Exists(src))
        {
            return;
        }

        File.Copy(src, dst, overwrite: false);
        logger.Info("Migrated legacy {0}: {1} -> {2}", fileName, src, dst);

        var stale = src + ".stale";
        try
        {
            // If a previous (possibly aborted) migration already left a
            // .stale, replace it; the source we just copied from is the
            // newer and authoritative version of "what was live before."
            if (File.Exists(stale)) File.Delete(stale);
            File.Move(src, stale);
            logger.Info("Renamed legacy {0} to {1}", src, stale);
        }
        catch (Exception ex)
        {
            // Rename failure is not fatal -- the destination is in place,
            // the operator's data is migrated. We just couldn't deactivate
            // the legacy file. Log and move on.
            logger.Warn(ex, "Could not rename legacy {0} to .stale; leaving in place. " +
                            "Operator may want to delete it manually to avoid confusion.", src);
        }
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
