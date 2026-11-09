using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.General.Components;

namespace RSBot.General
{
    public class GeneralManager
    {
        private static int _reloginSeq;
        public static bool IsClientless =>
            Game.Clientless && Kernel.Proxy != null && Kernel.Proxy.IsConnectedToAgentserver;
        public static bool IsConnected => Kernel.Proxy != null && Kernel.Proxy.IsConnectedToAgentserver;

        public GeneralManager()
        {
            SubscribeEvents();
        }

        private void SubscribeEvents()
        {
            ClientlessManager.RegionalAuthHandler = HandleRegionalAuth;

            EventManager.SubscribeEvent("OnAgentServerConnected", OnAgentServerConnected);
            EventManager.SubscribeEvent("OnAgentServerDisconnected", OnAgentServerDisconnected);
            EventManager.SubscribeEvent("OnGatewayServerDisconnected", OnGatewayServerDisconnected);
            EventManager.SubscribeEvent("OnEnterGame", OnEnterGame);
            EventManager.SubscribeEvent("OnExitClient", OnExitClient);
            EventManager.SubscribeEvent("OnProfileChanged", OnProfileChanged);
        }

        private static async Task<bool> HandleRegionalAuth()
        {
            if (Game.ClientType == GameClientType.RuSro)
                return await RuSroAuthService.Auth();
            else if (Game.ClientType == GameClientType.Japanese)
                return await JSROAuthService.GetTokenAsync();
            return true;
        }

        private void OnAgentServerConnected()
        {
            Interlocked.Increment(ref _reloginSeq);
            AutoLogin.Pending = false;
            EventManager.FireEvent("OnAutoLoginAborted");
        }

        private async void OnAgentServerDisconnected()
        {
            try
            {
                Kernel.Bot.Stop();

                // Skiped: Cuz managing from ClientlessManager
                if (Game.Clientless)
                    return;

                ClientManager.Kill();

                if (GlobalConfig.Get<bool>("RSBot.General.EnableAutomatedLogin"))
                {
                    var reloginSeq = Interlocked.Increment(ref _reloginSeq);

                    EventManager.FireEvent("OnAutoReloginStarted");

                    int delay = 10000;
                    if (GlobalConfig.Get("RSBot.General.EnableWaitAfterDC", false))
                        delay = GlobalConfig.Get<int>("RSBot.General.WaitAfterDC") * 60 * 1000;

                    Log.Warn($"Attempting relogin in {delay / 1000} seconds...");
                    await Task.Delay(delay);

                    if (reloginSeq != Volatile.Read(ref _reloginSeq))
                        return;

                    var userAuthenticated = await HandleRegionalAuth();
                    if (!userAuthenticated)
                    {
                        Log.Warn("Regional auth failed!");
                        return;
                    }

                    await StartClientProcess();
                    return;
                }

                EventManager.FireEvent("OnClientDisconnected");
            }
            catch (Exception error)
            {
                Log.Error("[General] Agent-server disconnect handler failed: " + error);
            }
        }

        private async void OnGatewayServerDisconnected()
        {
            try
            {
                AutoLogin.Pending = false;
                EventManager.FireEvent("OnAutoLoginAborted");

                var disconnectedProxy = Kernel.Proxy;
                if (disconnectedProxy == null)
                    return;

                var wasClientless = Game.Clientless;
                var autoLoginEnabled = GlobalConfig.Get<bool>("RSBot.General.EnableAutomatedLogin");

                Log.Warn(
                    $"Gateway connection dropped. AgentConnected={disconnectedProxy.IsConnectedToAgentserver}, " +
                    $"SwitchingToAgent={disconnectedProxy.IsSwitchingToAgentserver}, " +
                    $"Clientless={wasClientless}, AutomatedLogin={autoLoginEnabled}."
                );

                if (!disconnectedProxy.IsConnectedToAgentserver && !disconnectedProxy.IsSwitchingToAgentserver)
                {
                    if (autoLoginEnabled)
                    {
                        var reloginSeq = Interlocked.Increment(ref _reloginSeq);

                        EventManager.FireEvent("OnAutoReloginStarted");

                        // Gateway disconnect can happen briefly during Gateway -> Agent switch.
                        await Task.Delay(2000);

                        if (
                            reloginSeq != Volatile.Read(ref _reloginSeq)
                            || !ReferenceEquals(Kernel.Proxy, disconnectedProxy)
                        )
                            return;

                        if (disconnectedProxy.IsConnectedToAgentserver || disconnectedProxy.IsSwitchingToAgentserver)
                            return;

                        EventManager.FireEvent("OnAutoReloginOngoing");

                        Log.StatusLang("Ready");
                        disconnectedProxy.Shutdown();

                        int delay = 10000;
                        if (GlobalConfig.Get("RSBot.General.EnableWaitAfterDC", false))
                            delay = GlobalConfig.Get<int>("RSBot.General.WaitAfterDC") * 60 * 1000;

                        Log.Warn(
                            "Gateway is still disconnected. Automatic login will restart the " +
                            $"{(wasClientless ? "clientless connection" : "Silkroad client")} in {delay / 1000} seconds."
                        );
                        await Task.Delay(delay);

                        if (
                            reloginSeq != Volatile.Read(ref _reloginSeq)
                            || !ReferenceEquals(Kernel.Proxy, disconnectedProxy)
                        )
                            return;

                        var userAuthenticated = await HandleRegionalAuth();
                        if (!userAuthenticated)
                        {
                            Log.Warn("Regional authentication failed; automatic relogin was cancelled.");
                            return;
                        }

                        if (
                            reloginSeq != Volatile.Read(ref _reloginSeq)
                            || !ReferenceEquals(Kernel.Proxy, disconnectedProxy)
                        )
                            return;

                        if (wasClientless)
                        {
                            Game.Clientless = true;
                            Log.Notify("Restarting the clientless gateway connection...");
                            Game.Start();
                        }
                        else
                        {
                            if (ClientManager.IsRunning)
                            {
                                Log.Warn("Closing the disconnected Silkroad client before automatic relogin.");
                                ClientManager.Kill();
                            }

                            await StartClientProcess();
                        }

                        return;
                    }

                    EventManager.FireEvent("OnClientDisconnected");

                    Log.StatusLang("Ready");
                    disconnectedProxy.Shutdown();

                    Game.Clientless = false;
                }
            }
            catch (Exception error)
            {
                Log.Error("[General] Gateway-server disconnect handler failed: " + error);
            }
        }

        private async void OnEnterGame()
        {
            try
            {
                var sessionProxy = Kernel.Proxy;
                if (sessionProxy == null)
                    return;

                const int readinessTimeoutMs = 60000;
                const int pollIntervalMs = 100;
                var attempts = readinessTimeoutMs / pollIntervalMs;

                for (var attempt = 0; attempt < attempts && !Game.Ready; attempt++)
                {
                    if (
                        !ReferenceEquals(Kernel.Proxy, sessionProxy)
                        || !sessionProxy.IsConnectedToAgentserver
                    )
                        return;

                    await Task.Delay(pollIntervalMs);
                }

                if (
                    !Game.Ready
                    || Game.Player == null
                    || !ReferenceEquals(Kernel.Proxy, sessionProxy)
                    || !sessionProxy.IsConnectedToAgentserver
                )
                {
                    Log.Warn("[General] Enter-game initialization did not become ready within 60 seconds; ignoring the stale event.");
                    return;
                }

                var startBot = GlobalConfig.Get<bool>("RSBot.General.StartBot");
                var useReturnScroll = GlobalConfig.Get<bool>("RSBot.General.UseReturnScroll");

                if (useReturnScroll)
                    Game.Player.UseReturnScroll();

                if (startBot)
                    Kernel.Bot.Start();
            }
            catch (Exception error)
            {
                Log.Error("[General] Enter-game handler failed: " + error);
            }
        }

        private void OnExitClient()
        {
            Log.StatusLang("Ready");

            if (Game.Clientless)
                return;

            var proxy = Kernel.Proxy;
            if (proxy == null)
                return;

            if (!GlobalConfig.Get<bool>("RSBot.General.StayConnected"))
            {
                proxy.Shutdown();
            }
            else
            {
                if (!proxy.IsConnectedToAgentserver)
                    return;

                if (!ClientlessManager.TryGoClientless())
                    return;

                EventManager.FireEvent("OnSwitchToClientless");

                Log.NotifyLang("ClientlessModeActivated");
            }
        }

        private void OnProfileChanged()
        {
            Accounts.Load();
        }

        private async Task StartClientProcess()
        {
            // A relogin callback can race with another startup/disconnect callback.
            // Never replace the proxy or report a launch failure while the prior client is alive.
            if (ClientManager.IsRunning)
            {
                Log.Warn(
                    "Automatic client start was skipped because the previous Silkroad client process is still running. " +
                    "The existing client and proxy session were left untouched."
                );
                return;
            }

            EventManager.FireEvent("OnClientProcessStarted");
            Log.Notify("Preparing the game proxy before starting the Silkroad client...");

            try
            {
                Game.Start();

                if (Kernel.Proxy == null || Kernel.Proxy.Port == 0)
                    throw new System.InvalidOperationException("The game proxy did not initialize a local listening port.");

                Log.Notify($"Game proxy is ready on local port {Kernel.Proxy.Port}. Starting the Silkroad client process...");

                var startedResult = await Task.Run(async () =>
                    await ClientManager.Start().ConfigureAwait(false)
                ).ConfigureAwait(false);

                if (!startedResult)
                {
                    if (ClientManager.IsRunning)
                    {
                        Log.Warn(
                            "ClientManager.Start skipped a duplicate launch because a Silkroad client is still running. " +
                            "Keeping the existing proxy session active."
                        );
                        return;
                    }

                    Log.Error("ClientManager.Start returned false and no Silkroad client process is running.");
                    OnExitClient();
                    Log.WarnLang("ClientStartingError");
                    return;
                }

                Log.Notify("Silkroad client process launch completed. Waiting for the client to connect to the local proxy.");
            }
            catch (System.Exception error)
            {
                Log.Error("Automatic Silkroad client restart failed: " + error);

                if (!ClientManager.IsRunning)
                {
                    OnExitClient();
                    Log.WarnLang("ClientStartingError");
                }
                else
                {
                    Log.Warn("The existing Silkroad client is still running; its proxy session will not be shut down.");
                }
            }
        }

        public static void ChangeSilkroadPath(string path)
        {
            GlobalConfig.Set("RSBot.SilkroadDirectory", Path.GetDirectoryName(path));
            GlobalConfig.Set("RSBot.SilkroadExecutable", Path.GetFileName(path));
        }

        public static void GoClientless()
        {
            if (Game.Clientless)
                return;

            if (!ClientlessManager.TryGoClientless())
                return;
            ClientManager.Kill();

            EventManager.FireEvent("OnSwitchToClientless");
        }

        public static async Task StartClientlessAsync()
        {
            await Task.Run(async () =>
            {
                if (Game.Clientless)
                    return;

                var started = false;
                Game.Clientless = true;
                try
                {
                    Log.StatusLang("StartingClientless");
                    EventManager.FireEvent("OnClientlessProcessStarted");

                    var userAuthenticated = await HandleRegionalAuth();
                    if (!userAuthenticated)
                    {
                        Log.Warn("Regional auth failed!");
                        return;
                    }

                    Game.Start();
                    started = true;
                }
                finally
                {
                    if (!started)
                    {
                        Game.Clientless = false;
                        EventManager.FireEvent("OnClientDisconnected");
                    }
                }
            });
        }

        public static async Task DisconnectAsync()
        {
            var proxy = Kernel.Proxy;
            await Task.Run(() =>
            {
                Game.Clientless = false;
                AutoLogin.Pending = false;
                EventManager.FireEvent("OnAutoLoginAborted");
                EventManager.FireEvent("OnAutoReloginOngoing");
                proxy?.Shutdown();
            });
        }

        public async Task StartClientAsync()
        {
            var userAuthenticated = await HandleRegionalAuth();

            if (userAuthenticated)
            {
                await StartClientProcess();
            }
        }

        public static void KillClient()
        {
            if (!IsClientless)
            {
                ClientManager.Kill();
            }
        }
    }
}
