using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using service;
using worker;
using CliWrap;
using System.Runtime.InteropServices;

const string service_name = "Gundi Radio Service";
const string service_executable_name = "RadioService.exe";


if (args is { Length: 1 })
{
    try
    {
        string executablePath =
            Path.Combine(AppContext.BaseDirectory, service_executable_name);

        if (args[0] is "/Install")
        {
            var result = await Cli.Wrap("sc")
                .WithArguments(new[] {
                    "create",
                    service_name,
                    $"binPath={executablePath}",
                    "start=auto",
                    $"displayname={service_name}"})
                .WithValidation(CommandResultValidation.None).ExecuteAsync();

            int[] good_return_codes = {
                0, // success 
                1073 // a service with that name already exists
                };
            if (!good_return_codes.Contains(result.ExitCode))
            {
                Console.WriteLine($"I could not create the service {result.ExitCode}");
                return;
            }
            
            result = await Cli.Wrap("sc").WithArguments(new[] {
                    "failure",
                    service_name,
                    "reset=0",
                    "actions=restart/60000/restart/120000/restart/180000" }).ExecuteAsync();

            result = await Cli.Wrap("sc").WithArguments(new[] {
                    "description",
                    service_name,
                    "A Gundi/EarthRanger service that reads radio location data from KAS20 database." }).ExecuteAsync();

            result = await Cli.Wrap("sc").WithArguments(new[] {
                    "start",
                    service_name,
                    }).ExecuteAsync();

        }
        else if (args[0] is "/Uninstall")
        {
            await Cli.Wrap("sc")
                .WithArguments(new[] { "stop", service_name })
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync();

            await Cli.Wrap("sc")
                .WithArguments(new[] { "delete", service_name })
                .ExecuteAsync();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
    }

    return;
}


var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = service_name;
});

// Conditional to suppress violation of CA1416
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { 
    LoggerProviderOptions.RegisterProviderOptions<
    EventLogSettings, EventLogLoggerProvider>(builder.Services);
}

builder.Services.Configure<RadioServiceConfiguration>(builder.Configuration.GetSection("RadioDataPumpConfiguration"));

builder.Services.AddHostedService<RadioDataPumpService>();

IHost host = builder.Build();
host.Run();
