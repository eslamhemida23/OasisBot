using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RSBot.Core.Event;

public class EventManager
{
    private static readonly List<(string name, Delegate handler, bool synchronous)> _listeners = new();
    private static readonly object _listenersLock = new();

    /// <summary>
    ///     Registers the event.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="handler">The handler.</param>
    public static void SubscribeEvent(string name, Delegate handler)
    {
        SubscribeEvent(name, handler, false);
    }

    /// <summary>
    ///     Registers an event handler that can opt into ordered execution on the firing thread.
    /// </summary>
    public static void SubscribeEvent(string name, Delegate handler, bool synchronous)
    {
        if (handler == null)
            return;

        lock (_listenersLock)
            _listeners.Add((name, handler, synchronous));
    }

    /// <summary>
    ///     Registers the event.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="handler">The handler.</param>
    public static void SubscribeEvent(string name, Action handler)
    {
        SubscribeEvent(name, handler, false);
    }

    /// <summary>
    ///     Registers an action handler that can opt into ordered execution on the firing thread.
    /// </summary>
    public static void SubscribeEvent(string name, Action handler, bool synchronous)
    {
        if (handler == null)
            return;

        lock (_listenersLock)
            _listeners.Add((name, handler, synchronous));
    }

    /// <summary>
    ///     Fires the event.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="parameters">The parameters.</param>
    public static void FireEvent(string name, params object[] parameters)
    {
        (Delegate Handler, bool Synchronous)[] targets;

        lock (_listenersLock)
        {
            targets = _listeners
                .Where(listener =>
                    listener.name == name &&
                    listener.handler.Method.GetParameters().Length == parameters.Length)
                .Select(listener => (listener.handler, listener.synchronous))
                .ToArray();
        }

        foreach (var target in targets)
        {
            if (Thread.CurrentThread.Name == "Network.PacketProcessor" && !target.Synchronous)
                _ = Task.Run(() => InvokeTarget(name, target.Handler, parameters));
            else
                InvokeTarget(name, target.Handler, parameters);
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
