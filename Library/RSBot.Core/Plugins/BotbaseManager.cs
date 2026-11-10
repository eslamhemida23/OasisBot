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
                    try
                    {
                        Bots[bot.Key] = bot.Value;
                        bot.Value.Register();
                        Log.Notify($"[BotbaseManager] Loaded botbase [{bot.Key}] from [{Path.GetFileName(file)}].");
                    }
                    catch (Exception error)
                    {
                        Bots.Remove(bot.Key);
                        Log.Error($"[BotbaseManager] Could not register botbase [{bot.Key}] from [{file}]: {error}");
                    }
                }

                if (!isHeadless)
                {
                    foreach (var view in result.views)
                    {
                        BotsViews[view.Key] = view.Value;
                        Log.Debug($"Loaded botbase view [{view.Value.Name}]");
                    }
                }
            }

            Log.Notify($"[BotbaseManager] Loaded {Bots.Count} botbases and {BotsViews.Count} botbase views.");
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


    private static Assembly LoadBotbaseAssembly(string file)
    {
        var requestedName = AssemblyName.GetAssemblyName(file);
        var requestedSimpleName = requestedName.Name;

        var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(loaded =>
            string.Equals(loaded.GetName().Name, requestedSimpleName, StringComparison.OrdinalIgnoreCase));

        if (alreadyLoaded != null)
        {
            var loadedName = alreadyLoaded.GetName();
            var loadedLocation = GetAssemblyLocation(alreadyLoaded);

            // Botbase output folders can contain shared dependencies (for example SDUI.dll).
            // The main application has already loaded those from its root directory. They are
            // not botbases, so do not try to load the second copy from Data/Bots.
            if (!ContainsBotbaseTypes(alreadyLoaded))
            {
                Log.Debug(
                    $"Skipping already loaded dependency [{requestedSimpleName}] in botbase folder: [{file}]. " +
                    $"The application already loaded it from [{loadedLocation}]."
                );
                return null;
            }

            if (!string.Equals(loadedName.FullName, requestedName.FullName, StringComparison.OrdinalIgnoreCase))
            {
                throw new FileLoadException(
                    $"Cannot load botbase '{file}'. A botbase assembly with the same name is already loaded " +
                    $"with a different identity: '{loadedName.FullName}' from '{loadedLocation}'. " +
                    $"Requested identity: '{requestedName.FullName}'."
                );
            }

            if (!IsSameAssemblyFile(file, alreadyLoaded))
            {
                throw new FileLoadException(
                    $"Cannot load botbase '{file}'. A botbase assembly with the same identity is already loaded " +
                    $"from a different file: '{loadedLocation}'. The files have different contents."
                );
            }

            Log.Debug($"Reusing already loaded botbase assembly [{requestedSimpleName}] from [{loadedLocation}].");
            return alreadyLoaded;
        }

        try
        {
            return Assembly.LoadFrom(file);
        }
        catch (FileLoadException error)
        {
            // Close the race where another component loads the same assembly after the
            // initial check but before Assembly.LoadFrom executes.
            alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(loaded =>
                string.Equals(loaded.GetName().Name, requestedSimpleName, StringComparison.OrdinalIgnoreCase));

            if (alreadyLoaded != null &&
                string.Equals(alreadyLoaded.GetName().FullName, requestedName.FullName, StringComparison.OrdinalIgnoreCase) &&
                IsSameAssemblyFile(file, alreadyLoaded))
            {
                Log.Debug($"Reusing already loaded botbase assembly [{requestedSimpleName}] after a concurrent load.");
                return alreadyLoaded;
            }

            throw new FileLoadException($"Failed to load botbase assembly '{file}'.", error);
        }
    }

    private static bool ContainsBotbaseTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes().Any(type =>
                type.IsPublic &&
                !type.IsAbstract &&
                (typeof(IBotbase).IsAssignableFrom(type) || typeof(IBotbaseView).IsAssignableFrom(type)));
        }
        catch
        {
            return false;
        }
    }

    private static string GetAssemblyLocation(Assembly assembly)
    {
        try
        {
            return string.IsNullOrWhiteSpace(assembly.Location) ? "<unknown location>" : assembly.Location;
        }
        catch
        {
            return "<unknown location>";
        }
    }

    private static bool IsSameAssemblyFile(string file, Assembly loadedAssembly)
    {
        try
        {
            var loadedPath = GetAssemblyLocation(loadedAssembly);
            if (loadedPath == "<unknown location>" || !File.Exists(loadedPath))
                return false;

            if (string.Equals(
                Path.GetFullPath(file),
                Path.GetFullPath(loadedPath),
                StringComparison.OrdinalIgnoreCase))
                return true;

            using var requestedStream = File.OpenRead(file);
            using var loadedStream = File.OpenRead(loadedPath);

            var requestedHash = System.Security.Cryptography.SHA256.HashData(requestedStream);
            var loadedHash = System.Security.Cryptography.SHA256.HashData(loadedStream);

            return requestedHash.SequenceEqual(loadedHash);
        }
        catch
        {
            return false;
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

        Assembly assembly;
        try
        {
            assembly = LoadBotbaseAssembly(file);
        }
        catch (Exception error)
        {
            Log.Error($"[BotbaseManager] Could not load botbase assembly [{file}]: {error}");
            return (bots, views);
        }

        if (assembly == null)
            return (bots, views);

        var types = GetLoadableTypes(assembly, file);
        foreach (var type in types.Where(t => t != null && t.IsPublic && !t.IsAbstract))
        {
            object instance = null;

            try
            {
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
            catch (Exception error)
            {
                Log.Error($"[BotbaseManager] Could not create botbase type [{type.FullName}] from [{file}]: {error}");
            }
        }

        if (bots.Count == 0 && views.Count == 0)
            Log.Warn($"[BotbaseManager] No botbase types were found in [{file}].");

        return (bots, views);
    }

    private static Type[] GetLoadableTypes(Assembly assembly, string file)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException error)
        {
            foreach (var loaderError in error.LoaderExceptions.Where(exception => exception != null))
                Log.Warn($"[BotbaseManager] Type load warning in [{file}]: {loaderError}");

            return error.Types.Where(type => type != null).ToArray();
        }
        catch (Exception error)
        {
            Log.Warn($"[BotbaseManager] Could not inspect types in [{file}]: {error}");
            return Array.Empty<Type>();
        }
    }
}
