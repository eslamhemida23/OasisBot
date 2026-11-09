using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RSBot.Core.Event;

public class EventManager
{
    private static readonly List<(string name, Delegate handler)> _listeners = new();
    private static readonly object _listenersLock = new();

    /// <summary>
    ///     Registers the event.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="handler">The handler.</param>
    public static void SubscribeEvent(string name, Delegate handler)
    {
        if (handler == null)
            return;

        lock (_listenersLock)
            _listeners.Add((name, handler));
    }

    /// <summary>
    ///     Registers the event.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="handler">The handler.</param>
    public static void SubscribeEvent(string name, Action handler)
    {
        if (handler == null)
            return;

        lock (_listenersLock)
            _listeners.Add((name, handler));
    }

    /// <summary>
    ///     Fires the event.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="parameters">The parameters.</param>
    public static void FireEvent(string name, params object[] parameters)
    {
        Delegate[] targets;

        lock (_listenersLock)
        {
            targets = _listeners
                .Where(listener =>
                    listener.name == name &&
                    listener.handler.Method.GetParameters().Length == parameters.Length)
                .Select(listener => listener.handler)
                .ToArray();
        }

        foreach (var target in targets)
        {
            if (Thread.CurrentThread.Name == "Network.PacketProcessor")
                _ = Task.Run(() => InvokeTarget(name, target, parameters));
            else
                InvokeTarget(name, target, parameters);
        }
    }

    private static void InvokeTarget(string eventName, Delegate target, object[] parameters)
    {
        try
        {
            target.DynamicInvoke(parameters);
        }
        catch (Exception error)
        {
            var cause = error is System.Reflection.TargetInvocationException { InnerException: not null } tie
                ? tie.InnerException
                : error;
            Log.Error($"Event handler failed. Event={eventName}, Handler={target.Method.DeclaringType?.FullName}.{target.Method.Name}: {cause}");
        }
    }
}
