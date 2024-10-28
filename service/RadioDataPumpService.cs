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

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                await Task.Delay(1000, stoppingToken);

                var logconfig = LogManager.Configuration ?? new NLog.Config.LoggingConfiguration();

                logger.Info("Data pump service started.");
                logger.Info("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:sszzz"));


                if (config.destination == null)
                {
                    logger.Info("destination is null. Stubbornly refusing to run.");
                    return;
                }

                if (config.connectionString == null)
                {
                    logger.Info("connectString is null. Stubbornly refusing to run.");
                    return;
                }


                IDataReader reader;
                if (config.reader_type == RadioServiceConfiguration.ReaderType.KAS20.ToString()) {
                    reader = new KAS20DataReader(config.database_server, config.database_name, config.database_user, config.database_password);
                }
                else if (config.reader_type == RadioServiceConfiguration.ReaderType.SmartDispatchPlus.ToString())
                {
                    reader = new SmartDispatchPlusV1Reader(config.database_server, config.database_name, config.database_user, config.database_password, config.database_schema);
                }
                else if (config.reader_type == RadioServiceConfiguration.ReaderType.SmartOneDispatch.ToString())
                {
                    reader = new SmartOneDispatchReader(config.database_server, config.database_name, config.database_user, config.database_password, config.database_schema);
                }
                else if (config.reader_type == RadioServiceConfiguration.ReaderType.TrbonetPlus.ToString())
                {
                    reader = new TrbonetPlusDataReader(config.database_server, config.database_name, config.database_user, config.database_password);
                }
                else
                {
                    logger.Error("Stubbornly refusing to run. Unknown reader type: " + config.reader_type);
                    return;
                }

                logger.Info("Starting up");

                logger.Info("destination: " + config.destination);
                var dataPump = new RadioDataPump(config.intervalMs == null ? 5000 : int.Parse(config.intervalMs));

                if (config.gundi_apikey != "")
                {
                    logger.Info("Gundi API key is set. Adding Gundi data writer.");

                    IDataWriter data_writer = new GundiV2DataWriter(config.destination, config.gundi_apikey);
                    var val = await dataPump.Run(
                        reader,
                        data_writer,
                        stoppingToken);
                }
                else 
                {
                    logger.Info("Using EarthRanger. Adding Gundi data pump.");
                    var val = await dataPump.Run(
                        reader,
                        new EarthRangerDataWriter(config.destination, config.earthranger_auth_token, config.earthranger_provider_key), stoppingToken);
                }

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
