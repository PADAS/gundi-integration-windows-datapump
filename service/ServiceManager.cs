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
            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to register service");
            return false;
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

            logger.Info("Service '{0}' stopped and deleted.", ServiceName);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to unregister service");
        }
    }
}
