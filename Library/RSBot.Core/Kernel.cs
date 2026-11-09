using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core.Components;
using RSBot.Core.Components.Command;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Core.Plugins;

namespace RSBot.Core;

public static class Kernel
{
    /// <summary>
    ///     The updater token source
    /// </summary>
    private static CancellationTokenSource _updaterTokenSource;
    private static Task _updaterTask;

    /// <summary>
    ///     Gets or sets the botbase manager.
    /// </summary>
    /// <value>
    ///     The botbase manager.
    /// </value>
    public static BotbaseManager BotbaseManager { get; private set; }

    /// <summary>
    ///     Gets the plugin manager.
    /// </summary>
    /// <value>
    ///     The plugin manager.
    /// </value>
    public static PluginManager PluginManager { get; private set; }

    /// <summary>
    ///     Gets the proxy.
    /// </summary>
    /// <value>
    ///     The proxy.
    /// </value>
    public static Proxy Proxy { get; set; }

    /// <summary>
    ///     Gets or sets the bot.
    /// </summary>
    /// <value>
    ///     The bot.
    /// </value>
    public static Bot Bot { get; set; }

    /// <summary>
    ///     The application language
    /// </summary>
    public static string Language { get; set; }

    /// <summary>
    ///     Launch mode set by command line arguments (launch-client, launch-clientless)
    /// </summary>
    public static string LaunchMode { get; set; }

    /// <summary>
    ///     Get environment fixed tick count
    /// </summary>
    public static int TickCount => Environment.TickCount & int.MaxValue;

    /// <summary>
    ///     Get environment base directory
    /// </summary>
    public static string BasePath => AppDomain.CurrentDomain.BaseDirectory;

    /// <summary>
    /// Returns a value indicating if the NavMeshApi should be used or not.
    /// </summary>
    public static bool EnableCollisionDetection
    {
        get => GlobalConfig.Get("RSBot.EnableCollisionDetection", false);
        set => GlobalConfig.Set("RSBot.EnableCollisionDetection", value);
    }

    /// <summary>
    /// Returns a value indicating if this is a debug environment.
    /// </summary>
    public static bool Debug
    {
        get
        {
#if DEBUG
            return true;
#endif
            return GlobalConfig.Get("RSBot.DebugEnvironment", false);
        }
        set => GlobalConfig.Set("RSBot.DebugEnvironment", value);
    }

    /// <summary>
    ///     Initializes this instance.
    /// </summary>
    public static void Initialize()
    {
        Bot = new Bot();
        BotbaseManager = new BotbaseManager();
        PluginManager = new PluginManager();

        //Network handlers/hooks
        RegisterNetworkHandlers();
        RegisterNetworkHooks();

        //CLI Commands
        CLIManager.Register(new StartBotCLICommand());
        CLIManager.Register(new StopBotCLICommand());
        CLIManager.Register(new StatusCLICommand());

        _updaterTokenSource = new CancellationTokenSource();

        // ComponentUpdaterAsync is asynchronous and yields every 10ms; a dedicated
        // LongRunning thread only executes its synchronous prefix and is then wasted.
        _updaterTask = Task.Run(() => ComponentUpdaterAsync(_updaterTokenSource.Token));
    }

    private static async Task ComponentUpdaterAsync(CancellationToken cancellationToken)
    {
        var lastTick = Environment.TickCount64;
        var lastClockTick = lastTick;
        var lastErrorLogTick = long.MinValue;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var now = Environment.TickCount64;

            if (now - lastClockTick >= 1000)
            {
                lastClockTick = now;
                try
                {
                    EventManager.FireEvent("OnClock");
                }
                catch (Exception error)
                {
                    Log.Error("Component updater clock event failed: " + error);
                }
            }

            // Game.Ready can change between a disconnect and the next component tick.
            // Do not dereference a player that has already been cleared by the disconnect path.
            if (!Game.Ready || Game.Player == null)
            {
                lastTick = now;
                continue;
            }

            try
            {
                var elapsedMilliseconds = Math.Max(0, now - lastTick);
                var elapsed = (int)Math.Min(elapsedMilliseconds, int.MaxValue);

                Game.Player.Update(elapsed);
                Game.Player.Transport?.Update(elapsed);
                Game.Player.JobTransport?.Update(elapsed);
                Game.Player.AbilityPet?.Update(elapsed);
                Game.Player.Growth?.Update(elapsed);
                Game.Player.Fellow?.Update(elapsed);

                SpawnManager.Update(elapsed);

                EventManager.FireEvent("OnTick");

                lastTick = Environment.TickCount64;
            }
            catch (Exception error)
            {
                // Reset the delta so one bad tick cannot produce an enormous movement/update
                // interval on the next iteration. Back off and rate-limit the log to avoid
                // a persistent fault generating 100 exceptions and log writes per second.
                lastTick = Environment.TickCount64;

                var errorTick = lastTick;
                if (errorTick - lastErrorLogTick >= 5000)
                {
                    Log.Fatal(error);
                    lastErrorLogTick = errorTick;
                }

                try
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    ///     Registers the network handler.
    /// </summary>
    private static void RegisterNetworkHandlers()
    {
        var type = typeof(IPacketHandler);
        var types = AppDomain
            .CurrentDomain.GetAssemblies()
            .SelectMany(s => s.GetTypes())
            .Where(p => type.IsAssignableFrom(p) && !p.IsInterface)
            .ToArray();

        foreach (var handler in types)
        {
            var instance = (IPacketHandler)Activator.CreateInstance(handler);

            PacketManager.RegisterHandler(instance);
        }
    }

    /// <summary>
    ///     Registers the network hooks.
    /// </summary>
    private static void RegisterNetworkHooks()
    {
        var type = typeof(IPacketHook);
        var types = AppDomain
            .CurrentDomain.GetAssemblies()
            .SelectMany(s => s.GetTypes())
            .Where(p => type.IsAssignableFrom(p) && !p.IsInterface)
            .ToArray();

        foreach (var hook in types)
        {
            var instance = (IPacketHook)Activator.CreateInstance(hook);

            PacketManager.RegisterHook(instance);
        }
    }
}
