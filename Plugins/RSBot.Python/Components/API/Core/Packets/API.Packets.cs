using System;
using System.Collections.Generic;
using System.Linq;
using Python.Runtime;
using RSBot.Core;
using RSBot.Core.Event;
using RSBot.Core.Network;
using RSBot.Python.Components.API.Interface;
using RSBot.Python.Views;

namespace RSBot.Python.Components.API.Core.Packets;

public class PacketsAPI : IPythonPlugin
{
    private sealed record HandlerRegistration(
        int Id,
        ushort Opcode,
        string Direction,
        string CallbackName
    );

    private readonly object _sync = new();
    private readonly Dictionary<int, HandlerRegistration> _handlers = new();
    private int _nextHandlerId;
    private Main _main;

    public string ModuleName => "packets";

    public void Init(Main main)
    {
        _main = main;

        // Subscribe once during API initialization. Python registrations only change
        // the local filter table, avoiding runtime mutation of PacketManager's lists.
        EventManager.SubscribeEvent("OnClientPacketReceive", OnClientPacketReceive);
        EventManager.SubscribeEvent("OnServerPacketReceive", OnServerPacketReceive);
    }

    public int add_handler(int opcode, string direction, string callbackName)
    {
        if (opcode < 0 || opcode > ushort.MaxValue ||
            string.IsNullOrWhiteSpace(callbackName))
            return 0;

        var normalizedDirection = (direction ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedDirection != "client_to_server" &&
            normalizedDirection != "server_to_client")
            return 0;

        lock (_sync)
        {
            var duplicate = _handlers.Values.FirstOrDefault(handler =>
                handler.Opcode == opcode &&
                handler.Direction == normalizedDirection &&
                handler.CallbackName == callbackName);

            if (duplicate != null)
                return duplicate.Id;

            var id = ++_nextHandlerId;
            if (id <= 0)
            {
                _nextHandlerId = 1;
                id = 1;
                while (_handlers.ContainsKey(id))
                    id++;
                _nextHandlerId = id;
            }

            _handlers[id] = new HandlerRegistration(
                id,
                (ushort)opcode,
                normalizedDirection,
                callbackName.Trim()
            );

            return id;
        }
    }

    public bool remove_handler(int handlerId)
    {
        if (handlerId <= 0)
            return false;

        lock (_sync)
            return _handlers.Remove(handlerId);
    }

    public int remove_all_handlers()
    {
        lock (_sync)
        {
            var count = _handlers.Count;
            _handlers.Clear();
            return count;
        }
    }

    public PyList get_handlers()
    {
        using (Py.GIL())
        {
            var result = new PyList();
            HandlerRegistration[] snapshot;

            lock (_sync)
                snapshot = _handlers.Values.OrderBy(handler => handler.Id).ToArray();

            foreach (var handler in snapshot)
            {
                var item = new PyDict();
                item.SetItem("id", new PyInt(handler.Id));
                item.SetItem("opcode", new PyInt(handler.Opcode));
                item.SetItem("direction", new PyString(handler.Direction));
                item.SetItem("callback", new PyString(handler.CallbackName));
                result.Append(item);
            }

            return result;
        }
    }

    private void OnClientPacketReceive(Packet packet)
    {
        Dispatch(packet, "client_to_server");
    }

    private void OnServerPacketReceive(Packet packet)
    {
        Dispatch(packet, "server_to_client");
    }

    private void Dispatch(Packet packet, string direction)
    {
        if (packet == null || _main == null)
            return;

        HandlerRegistration[] matches;
        lock (_sync)
        {
            matches = _handlers.Values
                .Where(handler => handler.Opcode == packet.Opcode && handler.Direction == direction)
                .ToArray();
        }

        if (matches.Length == 0)
            return;

        var dataHex = BitConverter.ToString(packet.GetBytes()).Replace("-", " ");
        foreach (var handler in matches)
            _main.DispatchPythonEvent(handler.CallbackName, (int)packet.Opcode, direction, dataHex);
    }

    public bool send_server_hex(int opcode, string dataHex, bool encrypted = false)
    {
        return SendHex(opcode, dataHex, encrypted, PacketDestination.Server);
    }

    public bool send_client_hex(int opcode, string dataHex, bool encrypted = false)
    {
        return SendHex(opcode, dataHex, encrypted, PacketDestination.Client);
    }

    private static bool SendHex(int opcode, string dataHex, bool encrypted, PacketDestination destination)
    {
        if (opcode < 0 || opcode > ushort.MaxValue || dataHex == null || Kernel.Proxy == null)
            return false;

        if (destination == PacketDestination.Server && !Kernel.Proxy.IsConnectedToAgentserver)
            return false;

        if (destination == PacketDestination.Client && Game.Clientless)
            return false;

        try
        {
            var compact = new string(dataHex.Where(character =>
                !char.IsWhiteSpace(character) && character != '-' && character != ':'
            ).ToArray());

            if (compact.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                compact = compact[2..];

            if (compact.Length % 2 != 0)
                return false;

            var data = Convert.FromHexString(compact);
            var packet = new Packet((ushort)opcode, encrypted);
            packet.WriteBytes(data);
            PacketManager.SendPacket(packet, destination);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Log.Warn($"[Python-API] Packet send failed: {ex.Message}");
            return false;
        }
    }
}
