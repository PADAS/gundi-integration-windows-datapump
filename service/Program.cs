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

        var app = builder.Build();

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
