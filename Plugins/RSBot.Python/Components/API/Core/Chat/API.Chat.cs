using System;
using RSBot.Core;
using RSBot.Python.Components.API.Interface;
using RSBot.Core.Extensions;
using RSBot.Core.Network;
using RSBot.Core.Objects;

namespace RSBot.Python.Components.API.Core.Chat;

public class ChatAPI : IPythonPlugin
{
    public string ModuleName => "chat";

    public void Init(RSBot.Python.Views.Main main) { }

    public bool send_message(int channel, string message, string receiver = null)
    {
        if (Game.Player == null || string.IsNullOrWhiteSpace(message))
            return false;

        var type = (ChatType)channel;
        if (type != ChatType.All && type != ChatType.Private &&
            type != ChatType.Party && type != ChatType.Guild && type != ChatType.Union)
            return false;

        if (type == ChatType.Private && string.IsNullOrWhiteSpace(receiver))
            return false;

        try
        {
            var packet = new Packet(0x7025);
            packet.WriteByte((byte)type);
            packet.WriteByte(1);

            if (Game.ClientType > GameClientType.Vietnam)
                packet.WriteByte(0);

            if (Game.ClientType >= GameClientType.Chinese_Old)
                packet.WriteByte(0);

            if (type == ChatType.Private)
                packet.WriteString(receiver);

            if (type == ChatType.Union)
                message = $"-{Game.Player.GuildName}): " + message;

            packet.WriteConditonalString(message);
            PacketManager.SendPacket(packet, PacketDestination.Server);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"[Python-API] chat.send_message failed: {ex.Message}");
            return false;
        }
    }

    public bool send_all(string message) => send_message((int)ChatType.All, message);
    public bool send_party(string message) => send_message((int)ChatType.Party, message);
    public bool send_guild(string message) => send_message((int)ChatType.Guild, message);
    public bool send_union(string message) => send_message((int)ChatType.Union, message);
    public bool send_private(string receiver, string message) =>
        send_message((int)ChatType.Private, message, receiver);

    public bool send_global(string message)
    {
        if (Game.Player == null || string.IsNullOrWhiteSpace(message))
            return false;

        var item = Game.Player.Inventory.GetItem(new TypeIdFilter(3, 3, 3, 5));
        if (item == null)
            return false;

        try
        {
            var packet = new Packet(0x704C);
            packet.WriteByte(item.Slot);

            if (Game.ClientType > GameClientType.Vietnam)
            {
                packet.WriteInt(item.Record.Tid);
                packet.WriteByte(0);
            }
            else
            {
                packet.WriteUShort((ushort)item.Record.Tid);
            }

            packet.WriteConditonalString(message);
            PacketManager.SendPacket(packet, PacketDestination.Server);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"[Python-API] chat.send_global failed: {ex.Message}");
            return false;
        }
    }
}
