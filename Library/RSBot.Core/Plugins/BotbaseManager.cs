using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RSBot.Core.Event;

namespace RSBot.Core.Plugins;

public class BotbaseManager
{
    /// <summary>
    ///     Gets the extension directory.
    /// </summary>
    /// <value>
    ///     The extension directory.
    /// </value>
    public string DirectoryPath => Path.Combine(Kernel.BasePath, "Data", "Bots");

    /// <summary>
    ///     Gets the extensions.
    /// </summary>
    /// <value>
    ///     The extensions.
    /// </value>
    public Dictionary<string, IBotbase> Bots { get; private set; }
    public Dictionary<string, IBotbaseView> BotsViews { get; private set; }

    /// <summary>
    ///     Loads the assemblies.
    /// </summary>
    public bool LoadAssemblies(bool isHeadless = false)
    {
        if (Bots != null)
            return false;

        try
        {
            Bots = new Dictionary<string, IBotbase>();
            BotsViews = new Dictionary<string, IBotbaseView>();

            foreach (var file in Directory.GetFiles(DirectoryPath, "*.dll"))
            {
                var result = GetExtensionsFromAssembly(file);

                foreach (var bot in result.bots)
                {
                    Bots[bot.Key] = bot.Value;
                    bot.Value.Register();
                }

                if (!isHeadless)
                {
                    foreach (var view in result.views)
                    {
                        BotsViews[view.Key] = view.Value;
                        Log.Debug($"Loaded botbase [{view.Value.Name}]");
                    }
                }
            }

            EventManager.FireEvent("OnLoadBotbases");

            return true;
        }
        catch (Exception ex)
        {
            WriteBootError("The botbase manager encountered a problem.", ex);
            return false;
        }
    }


    private static void WriteBootError(string message, Exception error)
    {
        var logDirectory = Path.Combine(Kernel.BasePath, "Data", "Logs");
        var logPath = Path.Combine(logDirectory, "boot-error.log");
        var content = $"{message}{Environment.NewLine}{error}";

        try
        {
            // The Logs folder is not guaranteed to be included in a clean BotUpdate package.
            Directory.CreateDirectory(logDirectory);
            File.WriteAllText(logPath, content);
        }
        catch (Exception logError)
        {
            // Do not allow a secondary logging failure to hide the original startup error.
            try
            {
                Log.Error($"{content}{Environment.NewLine}Could not write boot-error.log: {logError}");
            }
            catch
            {
                try
                {
                    File.WriteAllText(Path.Combine(Kernel.BasePath, "boot-error.log"),
                        $"{content}{Environment.NewLine}Could not write Data/Logs/boot-error.log: {logError}");
                }
                catch
                {
                    // Startup diagnostics must never throw over the original error.
                }
            }
        }
    }

    /// <summary>
    ///     Gets the extensions from assembly.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns></returns>
    private static (
        Dictionary<string, IBotbase> bots,
        Dictionary<string, IBotbaseView> views
    ) GetExtensionsFromAssembly(string file)
    {
        var bots = new Dictionary<string, IBotbase>();
        var views = new Dictionary<string, IBotbaseView>();

        var assembly = Assembly.LoadFrom(file);

        try
        {
            var types = assembly.GetTypes();

            foreach (var type in types.Where(t => t.IsPublic && !t.IsAbstract))
            {
                object instance = null;

                if (typeof(IBotbase).IsAssignableFrom(type))
                {
                    instance = Activator.CreateInstance(type);
                    var bot = (IBotbase)instance;
                    bots[bot.Name] = bot;
                }

                if (typeof(IBotbaseView).IsAssignableFrom(type))
                {
                    instance ??= Activator.CreateInstance(type);
                    var view = (IBotbaseView)instance;
                    views[view.Name] = view;
                }
            }
        }
        catch
        {
            /* ignore, it's an invalid botbase */
        }

        return (bots, views);
    }
}
