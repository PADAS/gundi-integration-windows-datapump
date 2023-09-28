using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using service;
using worker;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
          
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "Gundi Radio Service";
        });

        LoggerProviderOptions.RegisterProviderOptions<
            EventLogSettings, EventLogLoggerProvider>(builder.Services);

        builder.Services.Configure<RadioDataPumpConfiguration>(builder.Configuration.GetSection("RadioDataPumpConfiguration"));

        builder.Services.AddHostedService<RadioDataPumpService>();

        IHost host = builder.Build();
        host.Run();
    }
}