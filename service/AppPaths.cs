namespace service;

/// <summary>
/// Single source of truth for filesystem paths the service uses for
/// per-install operator data — config and pump state. These live in
/// %PROGRAMDATA% rather than next to the binaries because the binaries
/// directory (<c>current\</c> under a Velopack install) gets wholesale
/// replaced on every update; anything left there is wiped.
///
/// %PROGRAMDATA%\GundiRadioService\
///     appsettings.json   live route configuration written by ConfigService
///     state.json         pump cursor written by StateHandler
///
/// LocalSystem (the service account) has read+write to %PROGRAMDATA%
/// without any special ACL setup. The directory survives uninstall, so
/// reinstalling restores the operator's config without retyping —
/// matching the convention used by Postgres, SQL Server, and most
/// other Windows services that own per-machine data.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// Subfolder under %PROGRAMDATA%. Keep in sync with the install
    /// title used by Velopack (vpk pack --packTitle).
    /// </summary>
    public const string AppFolderName = "GundiRadioService";

    public const string ConfigFileName = "appsettings.json";
    public const string StateFileName = "state.json";

    /// <summary>
    /// The data directory itself. Created lazily by callers that write —
    /// reading callers should tolerate it not existing yet (means a fresh
    /// install where nothing has been saved).
    /// </summary>
    public static string DataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppFolderName);

    public static string ConfigFilePath => Path.Combine(DataDirectory, ConfigFileName);
    public static string StateFilePath  => Path.Combine(DataDirectory, StateFileName);

    /// <summary>
    /// Ensure <see cref="DataDirectory"/> exists. Idempotent. LocalSystem
    /// can always create %PROGRAMDATA% subfolders, so we don't need to
    /// guard against permission errors — if this throws, something is
    /// catastrophically wrong and the caller's surrounding error handling
    /// will surface it.
    /// </summary>
    public static void EnsureDataDirectoryExists()
    {
        Directory.CreateDirectory(DataDirectory);
    }
}
