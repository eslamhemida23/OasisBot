using System;
using Python.Runtime;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Extensions;
using RSBot.Core.Network;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Spawn;
using RSBot.Python.Components.API.Interface;
using RSBot.Python.Views;

namespace RSBot.Python.Components.API.Core.NPC;

public class NpcAPI : IPythonPlugin
{
    public string ModuleName => "npc";

    public void Init(Main main) { }

    private static PyDict BuildNpc(SpawnedNpc npc)
    {
        using (Py.GIL())
        {
            var result = new PyDict();
            if (npc == null)
                return result;

            result.SetItem("uid", new PyInt(npc.UniqueId));
            result.SetItem("model", new PyInt(npc.Id));
            result.SetItem("servername", new PyString(npc.Record?.CodeName ?? string.Empty));
            result.SetItem("name", new PyString(npc.Record?.GetRealName() ?? string.Empty));
            result.SetItem("x", new PyFloat(npc.Position.X));
            result.SetItem("y", new PyFloat(npc.Position.Y));
            result.SetItem("z", new PyFloat(npc.Position.ZOffset));
            result.SetItem("region", new PyInt(npc.Position.Region.Id));
            result.SetItem("distance", new PyFloat(npc.DistanceToPlayer));
            result.SetItem("talk_flag", new PyInt(npc.Talk.Flag));
            result.SetItem("custom_name", new PyString(npc.Talk.CustomName ?? string.Empty));
            result.SetItem("custom_type", new PyInt(npc.Talk.CustomType));
            result.SetItem("custom_id", new PyInt(npc.Talk.CustomId));
            result.SetItem("option_flags", new PyString($"0x{npc.Talk.OptionFlags:X16}"));

            var options = new PyList();
            if (npc.Talk.Options != null)
            {
                foreach (var option in npc.Talk.Options)
                    options.Append(new PyInt(option));
            }
            result.SetItem("options", options);

            return result;
        }
    }

    public PyDict get_npc(uint uniqueId)
    {
        if (Game.Player == null || !SpawnManager.TryGetEntity<SpawnedNpc>(uniqueId, out var npc))
            return new PyDict();

        return BuildNpc(npc);
    }

    public PyDict get_dialog_state()
    {
        using (Py.GIL())
        {
            var result = new PyDict();
            var state = Game.Player?.State.DialogState;
            if (state == null)
            {
                result.SetItem("is_in_dialog", new PyBool(false));
                return result;
            }

            result.SetItem("is_in_dialog", new PyBool(state.IsInDialog));
            result.SetItem("npc_uid", new PyInt(state.Npc?.UniqueId ?? 0));
            result.SetItem("npc_name", new PyString(state.Npc?.Record?.GetRealName() ?? string.Empty));
            result.SetItem("talk_option", new PyInt((byte)state.TalkOption));
            result.SetItem("is_speciality_time", new PyBool(state.IsSpecialityTime));
            return result;
        }
    }

    public bool select(uint uniqueId)
    {
        if (Game.Player == null || Kernel.Proxy?.IsConnectedToAgentserver != true)
            return false;

        return SpawnManager.TryGetEntity<SpawnedNpc>(uniqueId, out var npc) && npc.TrySelect();
    }

    public bool deselect(uint uniqueId)
    {
        if (Game.Player == null || Kernel.Proxy?.IsConnectedToAgentserver != true)
            return false;

        return SpawnManager.TryGetEntity<SpawnedNpc>(uniqueId, out var npc) && npc.TryDeselect();
    }

    public bool talk(uint uniqueId, int option, int timeoutMs = 10000)
    {
        if (Game.Player == null ||
            Kernel.Proxy?.IsConnectedToAgentserver != true ||
            option < byte.MinValue || option > byte.MaxValue ||
            !Enum.IsDefined(typeof(TalkOption), (byte)option) ||
            timeoutMs < 1000 || timeoutMs > 30000)
            return false;

        if (!SpawnManager.TryGetEntity<SpawnedNpc>(uniqueId, out var npc))
            return false;

        if (!npc.TrySelect())
            return false;

        var packet = new Packet(0x7046);
        packet.WriteUInt(npc.UniqueId);
        packet.WriteByte((byte)option);

        var callback = new AwaitCallback(
            response => response.ReadByte() == 1
                ? AwaitCallbackResult.Success
                : AwaitCallbackResult.Fail,
            0xB046
        );

        PacketManager.SendPacket(packet, PacketDestination.Server, callback);
        callback.AwaitResponse(timeoutMs);

        return callback.IsCompleted;
    }

    public bool talk_by_name(string npcName, int option, int timeoutMs = 10000)
    {
        if (Game.Player == null || string.IsNullOrWhiteSpace(npcName))
            return false;

        if (!SpawnManager.TryGetEntity<SpawnedNpc>(
            npc => string.Equals(npc.Record?.CodeName, npcName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(npc.Record?.GetRealName(), npcName, StringComparison.OrdinalIgnoreCase),
            out var target))
            return false;

        return talk(target.UniqueId, option, timeoutMs);
    }
}
