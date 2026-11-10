using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RSBot.Core.Event;
using RSBot.Core.Network;

namespace RSBot.Core.Plugins;

public class PluginManager
{
    /// <summary>
    ///     Gets the extension directory.
    /// </summary>
    /// <value>
    ///     The extension directory.
    /// </value>
    public string InitialDirectory => Path.Combine(Kernel.BasePath, "Data", "Plugins");

    /// <summary>
    ///     Gets the extensions.
    /// </summary>
    /// <value>
    ///     The extensions.
    /// </value>
    public Dictionary<string, IPlugin> Extensions { get; private set; }
    public Dictionary<string, IPluginView> ExtensionsViews { get; private set; }

    /// <summary>
    ///     Loads the assemblies.
    /// </summary>
    public bool LoadAssemblies(bool isHeadless = false)
    {
        if (Extensions != null)
            return false;

        Extensions = new Dictionary<string, IPlugin>();
        ExtensionsViews = new Dictionary<string, IPluginView>();

        try
        {
            var files = Directory.GetFiles(InitialDirectory, "*.dll");
            foreach (var file in files)
            {
                // Older updates may have left a botbase DLL in Data/Plugins.
                // If the same DLL exists in Data/Bots, leave it for BotbaseManager;
                // loading it here would lock the wrong copy into the default context.
                if (HasMatchingBotbaseFile(file))
                {
                    Log.Warn(
                        $"Skipped botbase DLL in plugin directory: [{file}]. " +
                        "The botbase manager will load the copy from Data/Bots."
                    );
                    continue;
                }

                var (plugins, views) = GetExtensionsFromAssembly(file);

                foreach (var plugin in plugins)
                {
                    if (!Extensions.ContainsKey(plugin.Key))
                    {
                        Extensions.Add(plugin.Key, plugin.Value);
                    }
                }
                if (!isHeadless)
                {
                    foreach (var view in views)
                    {
                        if (!ExtensionsViews.ContainsKey(view.Key))
                        {
                            ExtensionsViews.Add(view.Key, view.Value);
                            Log.Debug($"Loaded view [{view.Value.InternalName}]");
                        }
                    }
                }
            }
            //order by index, not alphabeticaly
            ExtensionsViews = ExtensionsViews
                .OrderBy(entry => entry.Value.Index)
                .ToDictionary(x => x.Key, x => x.Value);

            EventManager.FireEvent("OnLoadPlugins");

            return true;
        }
        catch (Exception ex)
        {
            WriteBootError("The plugin manager encountered a problem.", ex);
            return false;
        }
    }


    private static bool HasMatchingBotbaseFile(string pluginFile)
    {
        var botbaseDirectory = Path.Combine(Kernel.BasePath, "Data", "Bots");
        var botbaseFile = Path.Combine(botbaseDirectory, Path.GetFileName(pluginFile));

        if (!File.Exists(botbaseFile))
            return false;

        try
        {
            var pluginName = AssemblyName.GetAssemblyName(pluginFile).Name;
            var botbaseName = AssemblyName.GetAssemblyName(botbaseFile).Name;

            return string.Equals(pluginName, botbaseName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error)
        {
            Log.Warn($"Could not compare plugin and botbase assemblies [{pluginFile}]: {error.Message}");
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
    private static (Dictionary<string, IPlugin>, Dictionary<string, IPluginView>) GetExtensionsFromAssembly(string file)
    {
        var plugins = new Dictionary<string, IPlugin>();
        var views = new Dictionary<string, IPluginView>();

        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(file);
        }
        catch (Exception error)
        {
            Log.Warn($"[PluginManager] Could not load plugin assembly [{file}]: {error}");
            return (plugins, views);
        }

        var assemblyTypes = GetLoadableTypes(assembly, file);
        foreach (var type in assemblyTypes.Where(t => t != null && t.IsPublic && !t.IsAbstract))
        {
            object instance = null;

            try
            {
                if (typeof(IPlugin).IsAssignableFrom(type))
                {
                    instance = Activator.CreateInstance(type);
                    var plugin = (IPlugin)instance;
                    plugins[plugin.InternalName] = plugin;
                    Log.Debug($"[PluginManager] Discovered plugin [{plugin.InternalName}] in [{Path.GetFileName(file)}].");
                }

                if (typeof(IPluginView).IsAssignableFrom(type))
                {
                    instance ??= Activator.CreateInstance(type);
                    var view = (IPluginView)instance;
                    views[view.InternalName] = view;
                    Log.Debug($"[PluginManager] Discovered view [{view.InternalName}] in [{Path.GetFileName(file)}].");
                }
            }
            catch (Exception error)
            {
                Log.Warn($"[PluginManager] Could not create plugin/view type [{type.FullName}] from [{file}]: {error}");
            }
        }

        if (plugins.Count == 0 && views.Count == 0)
        {
            Log.Warn($"[PluginManager] No plugin or view types were found in [{file}].");
            return (plugins, views);
        }

        var handlerType = typeof(IPacketHandler);
        var hookType = typeof(IPacketHook);

        foreach (var type in assemblyTypes.Where(p => p != null && handlerType.IsAssignableFrom(p) && !p.IsInterface))
        {
            try
            {
                PacketManager.RegisterHandler((IPacketHandler)Activator.CreateInstance(type));
            }
            catch (Exception error)
            {
                Log.Warn($"[PluginManager] Could not register packet handler [{type.FullName}] from [{file}]: {error}");
            }
        }

        foreach (var type in assemblyTypes.Where(p => p != null && hookType.IsAssignableFrom(p) && !p.IsInterface))
        {
            try
            {
                PacketManager.RegisterHook((IPacketHook)Activator.CreateInstance(type));
            }
            catch (Exception error)
            {
                Log.Warn($"[PluginManager] Could not register packet hook [{type.FullName}] from [{file}]: {error}");
            }
        }

        return (plugins, views);
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
                Log.Warn($"[PluginManager] Type load warning in [{file}]: {loaderError}");

            return error.Types.Where(type => type != null).ToArray();
        }
        catch (Exception error)
        {
            Log.Warn($"[PluginManager] Could not inspect types in [{file}]: {error}");
            return Array.Empty<Type>();
        }
    }
}
