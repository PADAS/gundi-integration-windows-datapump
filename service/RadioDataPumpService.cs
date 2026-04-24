using service;
using worker;

namespace service;

using NLog;
using DataPump;

using System.Threading.Tasks;
using worker;

public class SupportedReader
{
    public string Name { get; set; }
    public RadioServiceConfiguration.ReaderType Type { get; set; }

    public override string ToString()
    {
        return Name;
    }
}


public class RouteConfiguration
{
    public string? Hostname { get; set; } = "localhost";
    public string? Username { get; set; } = "";
    public string? Password { get; set; } = "";
    public string? DatabaseName { get; set; } = "";
    public SupportedReader DatabaseType { get; set; }
    public string? DatabaseSchema { get; set; } = "";
    public string? intervalMs { get; set; } = "5000";

    public List<GundiConnection> gundiConnections { get; set; }

    public RouteConfiguration()
    {
        gundiConnections = new List<GundiConnection>();
    }

}

public class RadioDataPumpService : BackgroundService
{
    private readonly ILogger<RadioDataPumpService> _logger;
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly IConfiguration _configuration;

    public static List<SupportedReader> supportedReaders = new List<SupportedReader>
            {
                new SupportedReader { Name = "Smart Dispatch Plus", Type = RadioServiceConfiguration.ReaderType.SmartDispatchPlus},
                new SupportedReader { Name = "Smart One Dispatch", Type = RadioServiceConfiguration.ReaderType.SmartOneDispatch },
                new SupportedReader { Name = "Kenwood KAS20", Type = RadioServiceConfiguration.ReaderType.KAS20 },
                new SupportedReader { Name = "TRBOnet", Type = RadioServiceConfiguration.ReaderType.TrbonetPlus }
            };

public RadioDataPumpService(ILogger<RadioDataPumpService> logger, IConfiguration c)
    {
        _logger = logger;
        _configuration = c;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        var config = new RadioServiceConfiguration();
        _configuration.GetSection("RadioServiceConfiguration").Bind(config);

        var routeConfiguration = new RouteConfiguration();
        _configuration.GetSection("RouteConfiguration").Bind(routeConfiguration);

        if (routeConfiguration.DatabaseType == null)
        {
            logger.Error("Database type not configured. Please run \"radioservice.exe /configure\" as a Windows Administrator.");
            Environment.Exit(1);
        }
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                await Task.Delay(1000, stoppingToken);

                var logconfig = LogManager.Configuration ?? new NLog.Config.LoggingConfiguration();

                logger.Info("Data pump service started.");
                logger.Info("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:sszzz"));


                IDataReader reader;
                IDataWriter data_writer;
                if (routeConfiguration.DatabaseType.Type == RadioServiceConfiguration.ReaderType.KAS20) {
                    reader = new KAS20DataReader(routeConfiguration.Hostname, routeConfiguration.DatabaseName, 
                        routeConfiguration.Username, routeConfiguration.Password);
                }
                else if (routeConfiguration.DatabaseType.Type == RadioServiceConfiguration.ReaderType.SmartDispatchPlus)
                {
                    reader = new SmartDispatchPlusV1Reader(
                        routeConfiguration.Hostname, routeConfiguration.DatabaseName,
                        routeConfiguration.Username, routeConfiguration.Password, routeConfiguration.DatabaseSchema);
                }
                else if (routeConfiguration.DatabaseType.Type == RadioServiceConfiguration.ReaderType.SmartOneDispatch)
                {
                    reader = new SmartOneDispatchReader(
                        routeConfiguration.Hostname, routeConfiguration.DatabaseName,
                        routeConfiguration.Username, routeConfiguration.Password, routeConfiguration.DatabaseSchema);
                }
                else if (routeConfiguration.DatabaseType.Type == RadioServiceConfiguration.ReaderType.TrbonetPlus)
                {
                    reader = new TrbonetPlusDataReader(
                        routeConfiguration.Hostname, routeConfiguration.DatabaseName,
                        routeConfiguration.Username, routeConfiguration.Password);
                }
                else
                {
                    logger.Error("Stubbornly refusing to run because I'm not configured with a database type.");
                    Environment.Exit(1);
                    return;
                }

                logger.Info("Starting up");


                var grouped_writer = new GroupedDataWriter();
                routeConfiguration.gundiConnections.ForEach(gundiConnection =>
                    {
                        var w = new GundiV2DataWriter(gundiConnection.Destination, gundiConnection.ApiKey);
                        gundiConnection.GroupAliases.ForEach(groupAlias =>
                        {
                            w.AddMatchingGroup(groupAlias.guid);
                        });
                        grouped_writer.AddWriter(w);
                    }
                );
                data_writer = grouped_writer;
                var dataPump = new RadioDataPump(routeConfiguration.intervalMs == null ? 5000 : int.Parse(routeConfiguration.intervalMs));


                var val = await dataPump.Run(reader, data_writer, stoppingToken);

                logger.Info("Data pump service finished.");
                logger.Info("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:sszzz"));
                

            }
        }
        catch (TaskCanceledException)
        {
            // When the stopping token is canceled, for example, a call made from services.msc,
            // we shouldn't exit with a non-zero exit code. In other words, this is expected...
        }
        catch (Exception ex) {
            _logger.LogError(ex, "{Message}", ex.Message);

            // Terminates this process and returns an exit code to the operating system.
            // This is required to avoid the 'BackgroundServiceExceptionBehavior', which
            // performs one of two scenarios:
            // 1. When set to "Ignore": will do nothing at all, errors cause zombie services.
            // 2. When set to "StopHost": will cleanly stop the host, and log errors.
            //
            // In order for the Windows Service Management system to leverage configured
            // recovery options, we need to terminate the process with a non-zero exit code.
            Environment.Exit(1);

        }
    }
}
