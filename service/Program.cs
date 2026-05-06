using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using service;
using worker;
using CliWrap;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Velopack;
using NLog;

internal class Program
{

    private static bool IsAdministrator()
    {
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            WindowsPrincipal principal = new(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// True if SCM has a service registered with our service name. Used
    /// by the interactive-launch guard above to distinguish "operator
    /// double-clicked the installed exe" (block) from "developer running
    /// `dotnet run` on a dev box" (allow).
    /// </summary>
    private static async Task<bool> IsServiceRegisteredAsync()
    {
        try
        {
            // sc query returns:
            //   exit 0    = service exists and was queried successfully
            //   exit 1060 = ERROR_SERVICE_DOES_NOT_EXIST
            //   other     = some other failure (permissions, SCM unreachable, etc.)
            // Treating "anything but 0" as "not registered" is fine for the
            // guard: in the worst case we let an interactive launch through
            // when the service IS registered but we couldn't query it, and
            // the existing port-conflict / dual-process behavior surfaces
            // the problem in a way the operator can see.
            var result = await Cli.Wrap("sc")
                .WithArguments(new[] { "query", ServiceManager.ServiceName })
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync();
            return result.ExitCode == 0;
        }
        catch
        {
            // sc.exe missing from PATH, or some other catastrophic failure.
            // Same fail-open rationale as above.
            return false;
        }
    }
    private static async Task Main(string[] args)
    {
        const string service_name = ServiceManager.ServiceName;
        const string service_executable_name = ServiceManager.ExecutableName;

        // After the install hook fires, the MSI's interactive launch of
        // RadioService.exe should exit immediately — the service is now
        // registered, SCM will start it, and continuing on to start a
        // web host as the operator would race the service for port 8080
        // and leave a console window open that confuses the customer.
        bool exitAfterHook = false;

        // Velopack hooks must run before anything else. When the installer
        // invokes our exe with --veloapp-install / --veloapp-updated /
        // --veloapp-uninstall, this call processes the hook and exits the
        // process. For ordinary launches it returns and we proceed normally.
        // Putting it before IsAdministrator() avoids the installer's hook
        // invocations getting blocked by the admin check (the installer
        // itself prompts for elevation, so the hooks already run as admin
        // when needed).
        VelopackApp.Build()
            .OnFirstRun(_ => LogManager.GetCurrentClassLogger().Info("First run after fresh install."))
            .OnAfterInstallFastCallback(v =>
            {
                // Fresh install via Setup.exe: register the Windows service
                // pointing at the just-installed exe. Same logic as the
                // legacy /install arg path; both call into ServiceManager.
                var hookLog = LogManager.GetCurrentClassLogger();
                var exePath = Path.Combine(AppContext.BaseDirectory, ServiceManager.ExecutableName);
                hookLog.Info($"Velopack OnAfterInstall: registering service at {exePath}");
                bool ok = ServiceManager.RegisterAsync(exePath).GetAwaiter().GetResult();

                if (!ok)
                {
                    // Don't let an install report success when the service
                    // wasn't registered. Throw so Velopack's hook
                    // dispatcher exits non-zero; the operator sees the
                    // installer fail and looks at radioservice.log.
                    hookLog.Error("Service registration failed during OnAfterInstall.");
                    throw new InvalidOperationException(
                        "Service registration failed. See radioservice.log next to RadioService.exe for details.");
                }

                // Don't continue with the normal Main flow. SCM owns the
                // service lifetime now; running the web host here would be
                // a duplicate process competing for port 8080.
                exitAfterHook = true;
            })
            .OnBeforeUninstallFastCallback(_ =>
            {
                // Uninstall via Add/Remove Programs (or veloapp-uninstall):
                // stop and delete the service before Velopack removes the
                // binaries. If the service is still running when Velopack
                // tries to delete its files, the uninstall fails with
                // "file in use".
                LogManager.GetCurrentClassLogger().Info("Velopack OnBeforeUninstall: stopping and removing service");
                ServiceManager.UnregisterAsync().GetAwaiter().GetResult();
            })
            .OnAfterUpdateFastCallback(v =>
            {
                // After Velopack's updater applies the file swap it
                // respawns the new exe -- but as an ordinary process,
                // not as the SCM-managed service. The previous service
                // process exited gracefully (StopApplication, exit 0)
                // which doesn't trigger SCM's failure-restart actions,
                // so SCM still believes the service is "Stopped." If we
                // continued through Main here we'd run a web host as a
                // detached LocalSystem process, and on the next reboot
                // SCM wouldn't know to start anything.
                //
                // Defensive fix: tell SCM to start the service, then
                // exit. SCM spawns the service in its proper context
                // (the new exe, the same registered binPath, fresh
                // service-mode lifecycle) and the SCM-spawned instance
                // becomes the running service. Our just-started
                // post-update process exits cleanly without binding
                // 8080, so there's no port collision.
                var hookLog = LogManager.GetCurrentClassLogger();
                hookLog.Info($"Updated to {v}; signalling SCM to start the service.");

                var start = Cli.Wrap("sc")
                    .WithArguments(new[] { "start", ServiceManager.ServiceName })
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteAsync().GetAwaiter().GetResult();

                if (start.ExitCode != 0)
                {
                    // 1056 = service already running. That's fine -- just
                    // means SCM beat us to it (or kept the old process
                    // alive somehow). Log and proceed.
                    hookLog.Info("sc start exit code {0} (1056 = already running, ignored).",
                        start.ExitCode);
                }

                exitAfterHook = true;
            })
            .Run();

        if (exitAfterHook)
        {
            return;
        }

        if (!IsAdministrator())
        {
            Console.WriteLine("You must run this program as an administrator.");
            return;
        }

        // Set the current directory to the directory where the executable is located
        // which is meaningful when this is started as a service.
        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

        // ----------------------------------------------------------------
        // Block accidental interactive launch.
        // ----------------------------------------------------------------
        // Velopack's MSI runs a post-install "launch the app" step, and a
        // curious operator might also double-click RadioService.exe from
        // the install dir. In our deployment shape, neither is correct:
        // the binary is meant to run as a Windows Service under SCM, and
        // any other launch produces a non-SCM-managed process that grabs
        // port 8080 outside SCM's view -- which races the SCM-launched
        // service for the bind, leaves SCM thinking the service is
        // Stopped while a rogue process serves requests, and breaks
        // every administrative gesture (sc start, Get-Service, restart
        // on reboot) that depends on SCM being the source of truth.
        //
        // Detection conditions, all required:
        //   * No command-line args -- /install, /uninstall, /configure
        //     are still expected to work, and Velopack hook args have
        //     already been consumed-and-exited by VelopackApp.Run() above.
        //   * Not running as a Windows service -- when SCM launched us,
        //     we want to proceed with the host as normal.
        //   * The service IS registered with SCM -- if it isn't, the
        //     operator may legitimately be running the portable build
        //     interactively, or running `dotnet run` during dev. Don't
        //     block those.
        //
        // The operator's path to the UI is the desktop shortcut (a .url
        // pointing at http://localhost:8080/), not the exe.
        if (args.Length == 0
            && !Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService()
            && await IsServiceRegisteredAsync())
        {
            // No console output: under MSI auto-launch we don't have a
            // user-attached console, and creating one would just flicker
            // a window. NLog is configured by this point and is what an
            // operator (or support tech) would look at if they wonder
            // why their double-click did nothing.
            LogManager.GetCurrentClassLogger().Info(
                "Interactive launch with no args; service is registered. Exiting -- " +
                "the service is owned by SCM. Use the desktop shortcut or browse " +
                "to http://localhost:8080/ to access the UI.");
            return;
        }

        if (args is { Length: 1 })
        {
            try
            {
                string executablePath =
                    Path.Combine(AppContext.BaseDirectory, service_executable_name);

                if (args[0].ToLower() is "/install")
                {
                    var ok = await ServiceManager.RegisterAsync(executablePath);
                    if (!ok) Console.WriteLine("Service registration failed. See logs.");
                }
                else if (args[0].ToLower() is "/uninstall")
                {
                    await ServiceManager.UnregisterAsync();
                }
                else if (args[0].ToLower() is "/configure")
                {
                    var result = await Cli.Wrap("Configurator.exe").ExecuteAsync();
                }
                else
                {
                    Console.WriteLine("Invalid argument. Use /install, /uninstall, or /configure.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error ocurred. See more info below.\n{ex}");
            }

            return;
        }

        // ----------------------------------------------------------------
        // Web host: BackgroundService (the pump) + Blazor Server (the UI)
        // share a process. Bound to localhost only — the embedded UI is
        // for the operator on this box, not the network.
        // ----------------------------------------------------------------
        var builder = WebApplication.CreateBuilder(args);

        builder.WebHost.ConfigureKestrel(opts =>
        {
            // 127.0.0.1 keeps Windows Firewall happy and prevents accidental
            // exposure to the LAN. Configurable later.
            opts.ListenLocalhost(8080);
        });

        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = service_name;
        });

        // Conditional to suppress violation of CA1416
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            LoggerProviderOptions.RegisterProviderOptions<
            EventLogSettings, EventLogLoggerProvider>(builder.Services);
        }

        // Shared pump state: written by RadioDataPumpService, observed by Blazor pages.
        builder.Services.AddSingleton<PumpStatus>();
        builder.Services.AddSingleton<ConfigService>();
        builder.Services.AddSingleton<PumpController>();
        builder.Services.AddSingleton<UpdateService>();
        builder.Services.AddSingleton<LogService>();
        builder.Services.AddSingleton<DiagnosticBundleService>();

        builder.Services.AddHostedService<RadioDataPumpService>();
        builder.Services.AddHostedService<HeartbeatService>();

        builder.Services.AddRazorPages();
        builder.Services.AddServerSideBlazor();

        // -------------------------------------------------------------
        // No authentication / authorization on the embedded UI.
        // -------------------------------------------------------------
        // We previously required Windows Negotiate + local-Administrators
        // membership. That sounded right on paper but caused real friction:
        // UAC token filtering means a regular admin user (the typical
        // operator) authenticates with a token where the Administrators
        // group is marked "use for deny only", so the role check returns
        // false and the operator gets a 403 from their own browser. The
        // workarounds (run the browser elevated, or replace IsInRole with
        // a SID-based group walk) were each fiddly enough that the user
        // experience for end operators wouldn't be reliable.
        //
        // The remaining defenses are:
        //
        //   * Kestrel binds 127.0.0.1 only (see ConfigureKestrel above),
        //     so the UI is unreachable from the LAN. A remote attacker
        //     would need code execution on the box already.
        //
        //   * The Origin middleware below rejects any browser request
        //     whose Origin header isn't localhost, blocking cross-site
        //     WebSocket hijacking and CSRF from a malicious page running
        //     in the operator's browser.
        //
        // What's NOT defended: a non-admin local user on the same box
        // (e.g. a kiosk account, a logged-in guest) can browse to the UI
        // and operate the service. Our deployment model assumes the
        // operator who installed the service is the same person using
        // it, on a box where untrusted local accounts don't exist. If
        // that assumption stops holding for a customer, the right fix
        // is to put the UI behind a reverse proxy with proper auth, not
        // to revive the in-process Negotiate dance.

        var app = builder.Build();

        // -------------------------------------------------------------
        // Origin enforcement
        // -------------------------------------------------------------
        // Defense against cross-site WebSocket hijacking and CSRF: a
        // browser tab on a malicious site could open a WebSocket to
        // http://localhost:8080/_blazor and ride a logged-in operator's
        // ambient Windows credentials. Reject any request whose Origin
        // header isn't localhost (or empty, which is non-browser tooling
        // like curl).
        //
        // Same-origin browser requests always include Origin matching
        // the page's URL, so legitimate UI traffic always passes.
        app.Use(async (ctx, next) =>
        {
            var origin = ctx.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin)
                && !origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase)
                && !origin.StartsWith("http://127.0.0.1:",  StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync("Cross-origin requests are not permitted.");
                return;
            }
            await next();
        });

        app.UseStaticFiles();
        app.UseRouting();
        app.MapBlazorHub();

        // Diagnostic bundle download endpoint (used by the "Download
        // diagnostic bundle" button on the Status page). Streams a fresh
        // zip on every request -- no caching -- so the bundle reflects
        // the current state of the service. Localhost-only by virtue of
        // Kestrel's bind config; no auth otherwise.
        //
        // Uses FileBufferingWriteStream so the zip stays in RAM up to a
        // 32 MB threshold and spills to disk above that. The radioservice
        // log can be hundreds of MB on long-running installs; buffering
        // the whole zip in memory would cause the service to spike RAM
        // (and potentially OOM on small boxes) every time someone clicks
        // the download button.
        app.MapGet("/api/diagnostic-bundle", async (HttpContext ctx, DiagnosticBundleService bundler) =>
        {
            ctx.Response.ContentType = "application/zip";
            ctx.Response.Headers.ContentDisposition =
                $"attachment; filename=\"{bundler.SuggestedFilename()}\"";

            await using var buffer = new Microsoft.AspNetCore.WebUtilities.FileBufferingWriteStream(
                memoryThreshold: 32 * 1024 * 1024,        // 32 MB before spilling to a temp file
                bufferLimit:     2L * 1024 * 1024 * 1024); // 2 GB hard cap
            bundler.BuildBundle(buffer);
            await buffer.DrainBufferAsync(ctx.Response.Body, ctx.RequestAborted);
        });

        app.MapFallbackToPage("/_Host");

        app.Run();
    }
}
