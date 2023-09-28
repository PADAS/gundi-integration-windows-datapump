namespace service;

using NLog;
using DataPump;

using System.Threading.Tasks;
using worker;

using IniParser;
using IniParser.Model;

public class RadioDataPumpService : BackgroundService
{
    private readonly ILogger<RadioDataPumpService> _logger;
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly IConfiguration _configuration;
    private IniData _iniData;

    public RadioDataPumpService(ILogger<RadioDataPumpService> logger, IConfiguration c)
    {
        _logger = logger;
        _configuration = c;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        RadioDataPumpConfiguration config = new RadioDataPumpConfiguration();

        /* I really like IConfiguration that can bind to appsettings.json, but
         * using a settings.ini file makes installation quite a bit simpler. */
        _configuration.GetSection("RadioDataPumpConfiguration").Bind(config);

        // Use an INI file for settings.
        //var parser = new FileIniDataParser();
        //_iniData = parser.ReadFile("settings.ini");

        //logger.Info("Settings.gundi_api: " + _iniData["gundi"]["gundi_api"]);

        //config.destination = _iniData["gundi"]["destination"];
        //config.intervalMs = _iniData["gundi"]["intervalMs"];
        //config.gundi_apikey = _iniData["gundi"]["apikey"];

        //var db_server = _iniData["kas20"]["server"];
        //var db_database = _iniData["kas20"]["database"];    
        //var user_id = _iniData["kas20"]["user_id"];
        //var password = _iniData["kas20"]["password"];
        //config.connectionString = $"Data Source={db_server};Initial Catalog={db_database};User ID={user_id};Password={password};TrustServerCertificate=True;";
        //config.kas20_system_id = _iniData["kas20"]["system_id"];    

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

                var dataPump = new RadioDataPump(config.intervalMs == null ? 5000 : int.Parse(config.intervalMs));

                if (config.gundi_apikey != "")
                {
                    logger.Info("Gundi API key is set. Adding Gundi data pump.");
                    var val = await dataPump.Run(
                        new KAS20DataReader(config.connectionString, int.Parse(config.kas20_system_id)),
                        new GundiDataWriter(config.destination, config.gundi_apikey),
                        stoppingToken);
                }
                else 
                {
                    logger.Info("Using EarthRanger. Adding Gundi data pump.");
                    var val = await dataPump.Run(
                        new KAS20DataReader(config.connectionString, int.Parse(config.kas20_system_id)),
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
