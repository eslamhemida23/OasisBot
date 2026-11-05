using System;
using System.Linq;
using Python.Runtime;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Network;
using RSBot.Core.Objects.Spawn;
using RSBot.Python.Components.API.Interface;

namespace RSBot.Python.Components.API.Core.Teleport;

public class TeleportAPI : IPythonPlugin
{
    public string ModuleName => "teleport";

    public void Init(RSBot.Python.Views.Main main) { }

    public PyList get_links(uint portalUniqueId)
    {
        using (Py.GIL())
        {
            var result = new PyList();
            if (Game.Player == null ||
                !SpawnManager.TryGetEntity<SpawnedPortal>(portalUniqueId, out var portal) ||
                portal.Links == null)
                return result;

            foreach (var link in portal.Links)
            {
                var target = link.Target;
                if (target == null)
                    continue;

                var item = new PyDict();
                item.SetItem("id", new PyInt(target.ID));
                item.SetItem("codename", new PyString(target.CodeName ?? string.Empty));
                item.SetItem("name", new PyString(target.ZoneName ?? string.Empty));
                item.SetItem("region", new PyInt(target.GenRegionID));
                item.SetItem("x", new PyInt(target.GenPos_X));
                item.SetItem("y", new PyInt(target.GenPos_Y));
                item.SetItem("z", new PyInt(target.GenPos_Z));
                item.SetItem("fee", new PyInt(link.Fee));
                result.Append(item);
            }

            return result;
        }
    }

    public PyList get_destinations()
    {
        using (Py.GIL())
        {
            var result = new PyList();
            if (Game.Player == null)
                return result;

            foreach (var destination in Game.ReferenceManager.TeleportData)
            {
                if (destination == null)
                    continue;

                var item = new PyDict();
                item.SetItem("id", new PyInt(destination.ID));
                item.SetItem("codename", new PyString(destination.CodeName ?? string.Empty));
                item.SetItem("name", new PyString(destination.ZoneName ?? string.Empty));
                item.SetItem("region", new PyInt(destination.GenRegionID));
                item.SetItem("x", new PyInt(destination.GenPos_X));
                item.SetItem("y", new PyInt(destination.GenPos_Y));
                item.SetItem("z", new PyInt(destination.GenPos_Z));
                result.Append(item);
            }

            return result;
        }
    }

    public bool teleport(string npcCodeName, uint destinationId, int timeoutMs = 30000)
    {
        if (Game.Player == null ||
            Kernel.Proxy?.IsConnectedToAgentserver != true ||
            string.IsNullOrWhiteSpace(npcCodeName) ||
            destinationId == 0 ||
            timeoutMs < 1000 || timeoutMs > 120000)
            return false;

        if (!SpawnManager.TryGetEntity<SpawnedBionic>(
            entity => string.Equals(entity.Record?.CodeName, npcCodeName, StringComparison.OrdinalIgnoreCase),
            out var teleporter))
            return false;

        if (!teleporter.TrySelect())
            return false;

        var packet = new Packet(0x705A);
        packet.WriteUInt(teleporter.UniqueId);
        packet.WriteByte(0x02);
        packet.WriteUInt(destinationId);

        var responseOpcode = (ushort)(Game.ClientType == GameClientType.Rigid ? 0x3077 : 0x3012);
        var callback = new AwaitCallback(null, responseOpcode);
        PacketManager.SendPacket(packet, PacketDestination.Server, callback);
        callback.AwaitResponse(timeoutMs);

        return callback.IsCompleted;
    }

    public bool teleport_by_name(string npcCodeName, string destinationCodeName, int timeoutMs = 30000)
    {
        if (string.IsNullOrWhiteSpace(destinationCodeName))
            return false;

        var destination = Game.ReferenceManager.TeleportData.FirstOrDefault(item =>
            string.Equals(item.CodeName, destinationCodeName, StringComparison.OrdinalIgnoreCase));

        if (destination == null)
            return false;

        return teleport(npcCodeName, destination.ID, timeoutMs);
    }
}
