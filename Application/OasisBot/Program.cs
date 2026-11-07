using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommandLine;
using CommandLine.Text;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Components.Command;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using RSBot.Views;

namespace RSBot;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    public static bool ManagerControlled { get; private set; }

    private static string _managerDiagnosticsPath;
    private static string _managerPipeName;
    private static Main _managerMainForm;
    private static SplashScreen _managerSplashScreen;
    private static volatile bool _managerStopRequested;

    public static bool ManagerStopRequested => _managerStopRequested;

    public static void WriteManagerDiagnostic(string message)
    {
        if (!ManagerControlled || string.IsNullOrWhiteSpace(_managerDiagnosticsPath))
            return;

        try
        {
            var directory = Path.GetDirectoryName(_managerDiagnosticsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.AppendAllText(
                _managerDiagnosticsPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}"
            );
        }
        catch
        {
            // Diagnostics must never prevent the bot from starting or shutting down.
        }
    }

    private static void InitializeManagerDiagnostics()
    {
        if (!ManagerControlled)
            return;

        _managerDiagnosticsPath = Environment.GetEnvironmentVariable("OASISBOT_MANAGER_LOG_PATH");
        _managerPipeName = Environment.GetEnvironmentVariable("OASISBOT_MANAGER_PIPE");

        if (!string.IsNullOrWhiteSpace(_managerDiagnosticsPath))
        {
            WriteManagerDiagnostic($"Process started. PID={Environment.ProcessId}");
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                WriteManagerDiagnostic("Unhandled exception: " + e.ExceptionObject);

            Application.ThreadException += (_, e) =>
                WriteManagerDiagnostic("UI thread exception: " + e.Exception);

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                WriteManagerDiagnostic("Unobserved task exception: " + e.Exception);
                e.SetObserved();
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                WriteManagerDiagnostic("ProcessExit event fired.");

            Application.ApplicationExit += (_, _) =>
                WriteManagerDiagnostic("ApplicationExit event fired.");
        }

        if (!string.IsNullOrWhiteSpace(_managerPipeName))
            StartManagerCommandServer();
    }

    private static void StartManagerCommandServer()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        _managerPipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous
                    );

                    await server.WaitForConnectionAsync().ConfigureAwait(false);

                    using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, true);
                    using var writer = new StreamWriter(server, Encoding.UTF8, 1024, true)
                    {
                        AutoFlush = true
                    };

                    var command = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.Equals(command, "STOP", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteManagerDiagnostic("Manager STOP command received.");
                        await writer.WriteLineAsync("STOPPING").ConfigureAwait(false);
                        RequestManagerStop();
                    }
                    else
                    {
                        await writer.WriteLineAsync("UNKNOWN_COMMAND").ConfigureAwait(false);
                    }
                }
                catch (Exception error)
                {
                    WriteManagerDiagnostic("Manager command pipe error: " + error.Message);
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
        });
    }

    private static void RequestManagerStop()
    {
        _managerStopRequested = true;

        _ = Task.Run(async () =>
        {
            // The main window handle may not exist yet while the splash screen is
            // closing and Application.Run is about to start. Keep the STOP request
            // pending until either window can receive it.
            for (var attempt = 0; attempt < 100; attempt++)
            {
                try
                {
                    if (_managerMainForm != null && !_managerMainForm.IsDisposed && _managerMainForm.IsHandleCreated)
                    {
                        _managerMainForm.BeginInvoke(new Action(() =>
                        {
                            if (!_managerMainForm.IsDisposed)
                                _managerMainForm.Close();
                        }));
                        return;
                    }

                    if (
                        _managerSplashScreen != null
                        && !_managerSplashScreen.IsDisposed
                        && _managerSplashScreen.IsHandleCreated
                    )
                    {
                        _managerSplashScreen.BeginInvoke(new Action(() =>
                        {
                            if (!_managerSplashScreen.IsDisposed)
                                _managerSplashScreen.Close();
                        }));
                        return;
                    }
                }
                catch (Exception error)
                {
                    WriteManagerDiagnostic("Could not dispatch Manager STOP to the UI: " + error.Message);
                }

                await Task.Delay(100).ConfigureAwait(false);
            }

            if (_managerStopRequested)
                ShutdownBeforeMainWindow();
        });
    }

    private static void ShutdownBeforeMainWindow()
    {
        WriteManagerDiagnostic("Manager STOP requested before the main window message loop.");

        try { GlobalConfig.Save(); } catch (Exception error) { WriteManagerDiagnostic("Global settings save failed: " + error.Message); }
        try { PlayerConfig.Save(); } catch (Exception error) { WriteManagerDiagnostic("Player settings save failed: " + error.Message); }
        try { ClientManager.Kill(); } catch (Exception error) { WriteManagerDiagnostic("Game client shutdown failed: " + error.Message); }
        try { Kernel.Proxy?.Shutdown(); } catch (Exception error) { WriteManagerDiagnostic("Proxy shutdown failed: " + error.Message); }

        Environment.Exit(0);
    }

    public static string AssemblyTitle = Assembly
        .GetExecutingAssembly()
        .GetCustomAttribute<AssemblyProductAttribute>()
        ?.Product;

    public static string AssemblyVersion =
        $"v{Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version}";

    public static string AssemblyDescription = Assembly
        .GetExecutingAssembly()
        .GetCustomAttribute<AssemblyDescriptionAttribute>()
        ?.Description;

    public class CommandLineOptions
    {
        [Option('c', "character", Required = false, HelpText = "Set the character name to use.")]
        public string Character { get; set; }

        [Option('p', "profile", Required = false, HelpText = "Set the profile name to use.")]
        public string Profile { get; set; }

        [Option("launch-client", Required = false, HelpText = "Start with client")]
        public bool LaunchClient { get; set; }

        [Option("launch-clientless", Required = false, HelpText = "Start clientless")]
        public bool LaunchClientless { get; set; }

        [Option("headless", Required = false, HelpText = "Start the bot without graphical user interface")]
        public bool Headless { get; set; }

        [Option("manager-controlled", Required = false, HelpText = "Enable orderly shutdown when controlled by OasisBot Manager")]
        public bool ManagerControlled { get; set; }
    }

    private static void DisplayHelp(ParserResult<CommandLineOptions> result)
    {
        var helpText = HelpText.AutoBuild(
            result,
            h =>
            {
                h.AdditionalNewLineAfterOption = false;
                h.AddDashesToOption = true;
                return HelpText.DefaultParsingErrorsHandler(result, h);
            }
        );
        Console.WriteLine(helpText);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        var parser = new Parser(with => with.HelpWriter = null);
        var parserResult = parser.ParseArguments<CommandLineOptions>(args);

        bool isHeadless = false;

        parserResult
            .WithParsed(options =>
            {
                RunOptions(options);
                isHeadless = options.Headless;
            })
            .WithNotParsed(errs =>
            {
                DisplayHelp(parserResult);
                var isHelp = errs.Any(e =>
                    e.Tag == ErrorType.HelpRequestedError || e.Tag == ErrorType.VersionRequestedError
                );
                Environment.Exit(isHelp ? 0 : 1);
            });

        InitializeManagerDiagnostics();

        //CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        // We need "." instead of "," while saving float numbers
        // Also client data is "." based float digit numbers
        CultureInfo.CurrentCulture = new CultureInfo("en-US");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (isHeadless)
        {
            RunHeadless();
        }
        else
        {
            FreeConsole();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            Main mainForm = new Main();
            _managerMainForm = mainForm;

            SplashScreen splashScreen = new SplashScreen(mainForm);
            _managerSplashScreen = splashScreen;
            WriteManagerDiagnostic("Opening initialization splash screen.");
            splashScreen.ShowDialog();
            _managerSplashScreen = null;

            if (_managerStopRequested)
            {
                ShutdownBeforeMainWindow();
                return;
            }

            WriteManagerDiagnostic("Initialization splash screen closed; entering main window.");
            Application.Run(mainForm);
            WriteManagerDiagnostic("Main window message loop returned.");
        }
    }

    private static void RunHeadless()
    {
        EventManager.SubscribeEvent(
            "OnAddLog",
            (string message, LogLevel level) => Terminal.WriteLog($"[{level}] {message}")
        );
        EventManager.SubscribeEvent("OnChangeStatusText", (string status) => Terminal.WriteLog($"[Status] {status}"));

        BotCL.Initialize(ProfileManager.SelectedProfile);

        bool running = true;
        while (running)
        {
            var inputLine = Terminal.ReadLine();
            if (string.IsNullOrWhiteSpace(inputLine))
                continue;

            var input = inputLine.Split(',');
            if (input == null || input.Length == 0)
                continue;

            var command = input[0].ToLowerInvariant();
            var args = input.Skip(1).ToArray();

            if (command == "exit" || command == "quit" || command == "bye")
            {
                running = false;
                continue;
            }

            CLIManager.Execute(command, args);
        }
    }

    private static void RunOptions(CommandLineOptions options)
    {
        ManagerControlled = options.ManagerControlled;
        if (ManagerControlled)
            _managerDiagnosticsPath = Environment.GetEnvironmentVariable("OASISBOT_MANAGER_LOG_PATH");

        if (options.LaunchClient)
        {
            Kernel.LaunchMode = "client";
            Log.Debug("Launching with client dictated by launch paramaters");
        }
        else if (options.LaunchClientless)
        {
            Kernel.LaunchMode = "clientless";
            Log.Debug("Launching client as clientless dictated by launch paramaters");
        }

        if (!string.IsNullOrEmpty(options.Profile))
        {
            var profile = options.Profile;
            if (ManagerControlled)
            {
                if (!ProfileManager.EnsureProfile(profile, "Default"))
                    throw new InvalidOperationException($"Could not prepare manager profile: {profile}");

                WriteManagerDiagnostic($"Manager profile selected: {profile}");
            }
            else if (ProfileManager.ProfileExists(profile))
                ProfileManager.SetSelectedProfile(profile);
            else
                ProfileManager.Add(profile);

            ProfileManager.IsProfileLoadedByArgs = true;
            Log.Debug($"Selected profile by args: {profile}");
        }

        if (!string.IsNullOrEmpty(options.Character))
        {
            var character = options.Character;
            ProfileManager.SelectedCharacter = character;
            Log.Debug($"Selected character by args: {character}");
        }
    }
}
