using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using service;
using worker;
using CliWrap;
using System.Runtime.InteropServices;
using System.Security.Principal;

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
        const string service_name = "Gundi Radio Service";
        const string service_executable_name = "RadioService.exe";


        if (!IsAdministrator())
        {
            Console.WriteLine("You must run this program as an administrator.");
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
                        Console.WriteLine($"I could not create the service (exit code: {result.ExitCode})");
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
                else if (args[0].ToLower() is "/uninstall")
                {
                    await Cli.Wrap("sc")
                        .WithArguments(new[] { "stop", service_name })
                        .WithValidation(CommandResultValidation.None)
                        .ExecuteAsync();

                    await Cli.Wrap("sc")
                        .WithArguments(new[] { "delete", service_name })
                        .ExecuteAsync();
                }
                else if (args[0].ToLower() is "/configure")
                {
                    AppSettingsManager appSettingsManager = new AppSettingsManager("appsettings.json");

                    var settings = appSettingsManager.LoadValue();
                    var config = settings.RadioServiceConfiguration;


                    var readerTypes = new Dictionary<int, string>
                    {
                        { 1, RadioServiceConfiguration.ReaderType.KAS20.ToString() },
                        { 2, RadioServiceConfiguration.ReaderType.SmartDispatchPlus.ToString() },
                        { 3, RadioServiceConfiguration.ReaderType.SmartOneDispatch.ToString() }
                    };

                    while (true)
                    {
                        Console.Write($"\nEnter the destination [{config.destination}]: ");
                        string? val = Console.ReadLine().Trim();
                        config.destination = val != "" ? val : config.destination;

                        Console.Write($"\nEnter your Gundi API Key [{config.gundi_apikey}]: ");
                        val = Console.ReadLine().Trim();
                        config.gundi_apikey = val != "" ? val : config.gundi_apikey;


                        Console.Write($"\nChoose a source system [{config.reader_type}]:\n");
                        foreach (var readerType in readerTypes)
                        {
                            Console.WriteLine($"{readerType.Key}. {readerType.Value}");
                        }
                        while (true)
                        {
                            if (int.TryParse(Console.ReadLine(), out int choice) && readerTypes.ContainsKey(choice))
                            {
                                Console.WriteLine($"You chose \"{readerTypes[choice]}\".");
                                config.reader_type = readerTypes[choice];
                                break;
                            }
                            else
                            {
                                Console.WriteLine("That's not a valid choice. Please choose a number from the list above.");
                            }
                        }

                        config.database_server ??= "localhost";
                        Console.Write($"\nEnter your {config.reader_type} Database Server [{config.database_server}]: ");
                        val = Console.ReadLine().Trim();
                        config.database_server = val != "" ? val : config.database_server;

                        config.database_name ??= "KAS20";
                        Console.Write($"\nEnter your {config.reader_type} Database Name [{config.database_name}]: ");
                        val = Console.ReadLine().Trim();
                        config.database_name = val != "" ? val : config.database_name;

                        List<string> things = new List<string> { RadioServiceConfiguration.ReaderType.SmartDispatchPlus.ToString(), 
                                                   RadioServiceConfiguration.ReaderType.SmartOneDispatch.ToString() };
                        if (things.Contains(config.reader_type))
                        {
                            config.database_schema ??= "public";
                            Console.Write($"\nEnter your {config.reader_type} database schema [{config.database_schema}]: ");
                            val = Console.ReadLine().Trim();
                            config.database_schema = val != "" ? val : config.database_schema;
                        }
                        else
                        {
                            config.database_schema = null;
                        }

                        if (config.reader_type == RadioServiceConfiguration.ReaderType.KAS20.ToString())
                        {
                            config.kas20_system_id ??= "1";
                            Console.Write($"\nEnter your {config.reader_type} System ID [{config.kas20_system_id}]: ");
                            val = Console.ReadLine().Trim();
                            config.kas20_system_id = val != "" ? val : config.kas20_system_id;
                        }
                        else
                        {
                            config.kas20_system_id = null;
                        }   

                        config.database_user ??= "KAS20Admin";
                        Console.Write($"\nEnter your {config.reader_type} Database User [{config.database_user}]: ");
                        val = Console.ReadLine().Trim();
                        config.database_user = val != "" ? val : config.database_user;

                        config.database_password ??= "a password";
                        Console.Write($"\nEnter your {config.reader_type} Database Password [{config.database_password}]: ");
                        val = Console.ReadLine().Trim();
                        config.database_password = val != "" ? val : config.database_password;

                        Console.WriteLine("\nPress enter to save and exit.\nType 'r' to redo, or 'q' to exit without saving.");
                        val = Console.ReadLine().Trim();
                        if (val == "q")
                        {
                            return;
                        }
                        else if (val == "r")
                        {
                            Console.Clear();
                            continue;
                        }
                        break;
                    }

                    appSettingsManager.SaveValue();

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error ocurred. See more info below.\n{ex}");
            }

            return;
        }

        var builder = Host.CreateApplicationBuilder(args);

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

        builder.Services.AddHostedService<RadioDataPumpService>();

        IHost host = builder.Build();
        host.Run();
    }
}