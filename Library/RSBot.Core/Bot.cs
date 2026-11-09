using System;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Plugins;

namespace RSBot.Core;

public class Bot
{
    private readonly object _lock = new();
    private Task _workerTask;

    /// <summary>
    ///     Gets or sets a value indicating whether this <see cref="Bot" /> is running.
    /// </summary>
    /// <value>
    ///     <c>true</c> if running; otherwise, <c>false</c>.
    /// </value>
    public volatile bool Running;

    /// <summary>
    ///     Gets or sets to the <see cref="CancellationToken" />
    /// </summary>
    public CancellationTokenSource TokenSource;

    /// <summary>
    ///     Gets the base.
    /// </summary>
    /// <value>
    ///     The base.
    /// </value>
    public IBotbase Botbase { get; private set; }
    public IBotbaseView BotbaseView { get; private set; }

    /// <summary>
    ///     Sets the botbase.
    /// </summary>
    /// <param name="botBase">The bot base.</param>
    public void SetBotbase(IBotbase botBase)
    {
        Botbase = botBase;

        EventManager.FireEvent("OnSetBotbase", botBase);
    }

    public void SetBotbaseView(IBotbaseView botBaseView)
    {
        BotbaseView = botBaseView;
        EventManager.FireEvent("OnSetBotbaseView", botBaseView);
    }

    /// <summary>
    ///     Starts this instance.
    /// </summary>
    public void Start()
    {
        CancellationTokenSource tokenSource;

        lock (_lock)
        {
            if (Running || Botbase == null || (_workerTask != null && !_workerTask.IsCompleted))
                return;

            tokenSource = new CancellationTokenSource();
            TokenSource = tokenSource;
            Running = true;
            _workerTask = Task.Run(() => RunAsync(tokenSource), tokenSource.Token);
        }
    }

    private async Task RunAsync(CancellationTokenSource tokenSource)
    {
        var token = tokenSource.Token;

        try
        {
            EventManager.FireEvent("OnStartBot");
            Botbase.Start();

            while (!token.IsCancellationRequested)
            {
                if (Game.Ready)
                    Botbase.Tick();

                await Task.Delay(100, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Fatal(ex);
            Log.Error("Bot worker stopped unexpectedly. Running bot cleanup before returning to idle state.");

            // An exception in Botbase.Start/Tick must not leave scripts, pickup, shopping
            // or botbase-specific workers running after the main worker has exited.
            try
            {
                Stop();
            }
            catch (Exception cleanupError)
            {
                Log.Fatal(new InvalidOperationException("Bot cleanup after a worker failure also failed.", cleanupError));
            }
        }
        finally
        {
            if (ReferenceEquals(TokenSource, tokenSource))
                Running = false;
        }
    }

    /// <summary>
    ///     Stops this instance.
    /// </summary>
    public void Stop()
    {
        CancellationTokenSource tokenSource;

        lock (_lock)
        {
            if (Botbase == null || !Running)
                return;

            Running = false;
            tokenSource = TokenSource;
        }

        if (tokenSource != null && !tokenSource.IsCancellationRequested)
            tokenSource.Cancel();

        Log.Notify($"Stopping bot {Botbase.Name}");

        RunCleanupStep("OnStopBot event", () => EventManager.FireEvent("OnStopBot"));
        RunCleanupStep("action cancellation", CancelActionOnStop);
        Game.SelectedEntity = null;

        RunCleanupStep("script manager", ScriptManager.Stop);
        RunCleanupStep("shopping manager", ShoppingManager.Stop);
        RunCleanupStep("pickup manager", PickupManager.Stop);
        RunCleanupStep("botbase", Botbase.Stop);

        Log.Notify($"Stopped bot {Botbase.Name}");
        Log.Status("Bot stopped");
    }

    private static void RunCleanupStep(string component, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception error)
        {
            Log.Error($"Bot cleanup failed in {component}: {error}");
        }
    }

    private void CancelActionOnStop()
    {
        var player = Game.Player;
        if (player == null || !player.InAction)
            return;

        _ = CancelActionOnStopAsync();

        async Task CancelActionOnStopAsync()
        {
            const int attempts = 5;
            const int retryDelay = 1000;

            for (var i = 0; i < attempts; i++)
            {
                if (Running || !Game.Ready || !ReferenceEquals(Game.Player, player) || !player.InAction)
                    return;

                try
                {
                    SkillManager.CancelAction(0);
                }
                catch (Exception error)
                {
                    Log.Error("Could not cancel the active player action while stopping the bot: " + error);
                    return;
                }

                if (i == attempts - 1)
                    return;

                await Task.Delay(retryDelay).ConfigureAwait(false);
            }
        }
    }
}
