using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RSBot.Core.Components.Command;

namespace RSBot.Core.Components;

public static class CommandManager
{
    private static List<ICommandExecutor> _commands;

    /// <summary>
    ///     Initializes the command manager.
    /// </summary>
    public static void Initialize()
    {
        _commands = new List<ICommandExecutor>(16);

        var type = typeof(ICommandExecutor);
        var types = AppDomain
            .CurrentDomain.GetAssemblies()
            .SelectMany(GetLoadableTypes)
            .Where(p => p != null && type.IsAssignableFrom(p) && !p.IsInterface)
            .ToArray();

        foreach (var handler in types)
        {
            var instance = (ICommandExecutor)Activator.CreateInstance(handler);

            _commands.Add(instance);
        }

        Log.Debug($"[CommandManager] Found and registered {_commands.Count} commands");
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException error)
        {
            // One optional plugin dependency must not prevent unrelated command types
            // from being discovered and registered.
            foreach (var loaderError in error.LoaderExceptions.Where(exception => exception != null))
                Log.Warn($"[CommandManager] Could not load a type from [{assembly.FullName}]: {loaderError.Message}");

            return error.Types.Where(type => type != null);
        }
        catch (Exception error)
        {
            Log.Warn($"[CommandManager] Could not inspect assembly [{assembly.FullName}]: {error.Message}");
            return Array.Empty<Type>();
        }
    }

    /// <summary>
    ///     Executes the given command
    /// </summary>
    /// <param name="command"></param>
    /// <param name="silent"></param>
    /// <returns></returns>
    public static bool Execute(string command, bool silent = false)
    {
        if (string.IsNullOrEmpty(command) || command == "none")
            return true;

        Log.Notify($"[CommandManager] Executing command {command}");

        var executor = GetExecutor(command);

        return executor != null && executor.Execute(silent);
    }

    /// <summary>
    ///     Gets a command executor instance by a command name
    /// </summary>
    /// <param name="commandName"></param>
    /// <returns></returns>
    public static ICommandExecutor GetExecutor(string commandName)
    {
        return _commands.FirstOrDefault(c => c.CommandName == commandName);
    }

    /// <summary>
    ///     Returns a list of all available command descriptions.
    ///     Key = Command name
    ///     Value = Command description
    /// </summary>
    /// <returns></returns>
    public static Dictionary<string, string> GetCommandDescriptions()
    {
        var result = new Dictionary<string, string>(16) { { "none", "No action" } };

        foreach (var command in _commands)
            result.Add(command.CommandName, command.CommandDescription);

        return result;
    }
}
