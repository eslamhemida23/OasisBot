using System;
using System.IO;
using System.Threading;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Objects;
using RSBot.Training.Bot;

namespace RSBot.Training.Bundle.Loop;

internal class LoopBundle : IBundle
{
    private const long WalkbackRetryDelayMs = 5000;
    private long _lastStartTick;

    /// <summary>
    ///     Gets the configuration.
    /// </summary>
    /// <value>
    ///     The configuration.
    /// </value>
    public LoopConfig Config { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether this <see cref="LoopBundle" /> is running.
    /// </summary>
    /// <value>
    ///     <c>true</c> if running; otherwise, <c>false</c>.
    /// </value>
    public bool Running { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether [townscript running].
    /// </summary>
    /// <value>
    ///     <c>true</c> if [townscript running]; otherwise, <c>false</c>.
    /// </value>
    public bool TownscriptRunning { get; private set; }

    /// <summary>
    ///     Invokes this instance.
    /// </summary>
    public void Invoke()
    {
        if (!Running)
            return;

        if (Config.UseVehicle && !Game.Player.HasActiveVehicle && !Game.Player.IsInDungeon)
        {
            Game.Player.SummonVehicle();

            //Wait for the vehicle to spawn
            Thread.Sleep(1000);
        }

        //We don't need to use buffs in town...
        if (Config.CastBuffs && !TownscriptRunning)
            Bundles.Buff.Invoke();
    }

    /// <summary>
    ///     Refreshes this instance.
    /// </summary>
    public void Refresh()
    {
        Config = new LoopConfig
        {
            WalkScript = PlayerConfig.Get<string>("RSBot.Walkback.File"),
            UseSpeedDrug = PlayerConfig.Get<bool>("RSBot.Training.checkUseSpeedDrug", true),
            UseVehicle = PlayerConfig.Get<bool>("RSBot.Training.checkUseMount", true),
            CastBuffs = PlayerConfig.Get<bool>("RSBot.Training.checkCastBuffs", true),
            UseReverse = PlayerConfig.Get<bool>("RSBot.Training.checkBoxUseReverse", false),
        };
    }

    public void Stop()
    {
        if (ScriptManager.Running)
            ScriptManager.Stop();

        if (ShoppingManager.Running)
            ShoppingManager.Stop();

        Running = false;
    }

    /// <summary>
    ///     Starts this instance.
    /// </summary>
    public void Start()
    {
        if (Running || !Kernel.Bot.Running)
            return;

        // If a walkback fails, TrainingBase.Tick can call Start again every 100 ms.
        // Back off briefly rather than repeatedly loading scripts / rebuilding paths.
        var now = Environment.TickCount64;
        if (_lastStartTick > 0 && now - _lastStartTick < WalkbackRetryDelayMs)
            return;

        _lastStartTick = now;
        Running = true;

        try
        {
            Refresh();
            CheckForTownScript();
        }
        finally
        {
            Running = false;
        }
    }

    /// <summary>
    ///     Checks for town script.
    /// </summary>
    public void CheckForTownScript()
    {
        if (ScriptManager.Running)
            return;

        var filename = Path.Combine(
            ScriptManager.InitialDirectory,
            "Towns",
            Game.Player.Movement.Source.Region + ".rbs"
        );

        //The player is in town, therefore, we need to run the town script first.
        if (!File.Exists(filename))
        {
            CheckForWalkbackScript();
            return;
        }

        if (PlayerConfig.Get<bool>("RSBot.Protection.checkStopBotOnReturnToTown"))
        {
            Log.Warn(
                "[Training] Automatic walkback was skipped because 'Stop bot on return to town' is enabled."
            );
            Kernel.Bot.Stop();
            return;
        }

        Log.NotifyLang("LoadingTownScript", filename);

        TownscriptRunning = true;

        ScriptManager.Load(filename);
        ScriptManager.RunScript(false);

        if (Running && Config.UseReverse)
        {
            var filter = new TypeIdFilter(3, 3, 3, 3);
            var item = Game.Player.Inventory.GetItem(filter);
            if (item != null)
                /*
                    2 => go to last recall point
                    3 => go to last died position
                    7 => select position on map

                    error codes:
                        85: Cannot find the place where you selected as recall point.
                        86: Cannot find the place where you died.
                 */
                if (item.UseTo(3))
                {
                    TownscriptRunning = false;
                    return;
                }
        }

        TownscriptRunning = false;

        Invoke();

        CheckForWalkbackScript(true);
    }

    /// <summary>
    ///     Checks whether the character has reached the configured training area.
    ///     The radius is the same boundary used by the Training movement bundle.
    /// </summary>
    private static bool IsAtTrainingArea(out double distance)
    {
        distance = double.MaxValue;

        if (Game.Player == null || Container.Bot == null)
            return false;

        var area = Container.Bot.Area;
        distance = Game.Player.Position.DistanceTo(area.Position);
        return distance <= area.Radius;
    }

    /// <summary>
    ///     Checks for walkback script. A saved walkback script is treated as a first
    ///     attempt, not proof that the current training coordinates have been reached.
    ///     If the script is stale or stops short, a fresh route is calculated from the
    ///     character's actual current position to the currently selected Training Area.
    /// </summary>
    public void CheckForWalkbackScript(bool startFromTown = false)
    {
        if (ScriptManager.Running || !Kernel.Bot.Running || Game.Player == null)
            return;

        if (IsAtTrainingArea(out var distance))
        {
            Log.Notify("[Training] Character is already inside the configured Training Area.");
            return;
        }

        var hasSavedWalkScript = !string.IsNullOrWhiteSpace(Config.WalkScript) && File.Exists(Config.WalkScript);

        if (hasSavedWalkScript)
        {
            Invoke();
            Log.NotifyLang("LoadingWalkScript", Config.WalkScript);

            ScriptManager.Load(Config.WalkScript);
            ScriptManager.RunScript(!startFromTown);

            if (!Kernel.Bot.Running || !Game.Ready || Game.Player == null || ScriptManager.Running)
                return;

            if (IsAtTrainingArea(out distance))
            {
                Log.Notify("[Training] Saved walkback script reached the configured Training Area.");
                return;
            }

            Log.Warn(
                $"[Training] Saved walkback script ended {distance:0.0} m from the current Training Area. " +
                "Calculating a fresh route to the configured coordinates..."
            );
        }
        else if (!string.IsNullOrWhiteSpace(Config.WalkScript))
        {
            Log.Warn(
                $"[Training] Saved walkback script was not found: {Config.WalkScript}. " +
                "Calculating a fresh route to the configured coordinates..."
            );
        }
        else
        {
            Log.Notify("[Training] No walkback script is configured. Calculating a route to the Training Area...");
        }

        if (!Kernel.Bot.Running || !Game.Ready || Game.Player == null)
            return;

        try
        {
            // Always calculate from the character's current position. This is important
            // after a town script or an old walkback script has moved the character.
            if (!NavigationManager.CalculatePathToTrainingArea())
            {
                IsAtTrainingArea(out distance);
                Log.Warn(
                    $"[Training] Could not calculate a route to the configured Training Area. " +
                    $"Current distance: {distance:0.0} m. Check the region/coordinates and NavLink data."
                );
                return;
            }

            var dynamicScript = NavigationManager.GenerateRBSFile();
            if (string.IsNullOrWhiteSpace(dynamicScript) || !File.Exists(dynamicScript))
            {
                Log.Warn("[Training] Navigation did not generate a usable walkback script.");
                return;
            }

            Config.WalkScript = dynamicScript;
            Invoke();

            Log.Notify(
                $"[Training] Following a freshly generated route to " +
                $"Region {Container.Bot.Area.Position.Region}, " +
                $"X {Container.Bot.Area.Position.X:0.0}, Y {Container.Bot.Area.Position.Y:0.0}."
            );

            ScriptManager.Load(dynamicScript);

            // The dynamic script was just generated from the current position. Do not
            // use FindNearestMoveCommandLine here, because it can skip its first waypoint.
            ScriptManager.RunScript(false);

            if (!Kernel.Bot.Running || !Game.Ready || Game.Player == null)
                return;

            if (IsAtTrainingArea(out distance))
            {
                Log.Notify("[Training] Walkback completed. Character reached the configured Training Area.");
            }
            else
            {
                Log.Warn(
                    $"[Training] Walkback script finished but the character is still {distance:0.0} m " +
                    "from the configured Training Area. The route will be retried after a short delay."
                );
            }
        }
        catch (Exception error)
        {
            Log.Error("[Training] Walkback failed while calculating or executing the route: " + error);
        }
    }
}
