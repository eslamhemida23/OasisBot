using System;
using Python.Runtime;
using RSBot.Python.Components.API.Interface;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Python.Components.API.Core.Party;

public class PartyAPI : IPythonPlugin
{
    public string ModuleName => "party";

    public void Init(RSBot.Python.Views.Main main) { }

    public PyList get_members()
    {
        using (Py.GIL())
        {
            var result = new PyList();
            if (Game.Player == null || !Game.Party.IsInParty || Game.Party.Members == null)
                return result;

            foreach (var member in Game.Party.Members)
            {
                if (member == null)
                    continue;

                var item = new PyDict();
                item.SetItem("uid", new PyInt(member.MemberId));
                item.SetItem("player_id", new PyInt(member.Player?.UniqueId ?? 0));
                item.SetItem("name", new PyString(member.Name ?? string.Empty));
                item.SetItem("level", new PyInt(member.Level));
                item.SetItem("guild", new PyString(member.Guild ?? string.Empty));

                var encodedHpMp = member.HealthMana.ToString("X2");
                var hpPercentage = Convert.ToByte(encodedHpMp[0].ToString(), 16) * 10;
                var mpPercentage = Convert.ToByte(encodedHpMp[1].ToString(), 16) * 10;
                item.SetItem("hp_percentage", new PyInt(hpPercentage));
                item.SetItem("mp_percentage", new PyInt(mpPercentage));

                item.SetItem("x", new PyFloat(member.Player?.Position.X ?? 0));
                item.SetItem("y", new PyFloat(member.Player?.Position.Y ?? 0));
                item.SetItem("z", new PyFloat(member.Player?.Position.ZOffset ?? 0));
                item.SetItem("region", new PyInt(member.Player?.Position.Region.Id ?? 0));
                item.SetItem("distance", new PyFloat(member.Player?.DistanceToPlayer ?? -1));

                result.Append(item);
            }

            return result;
        }
    }

    public bool is_in_party()
    {
        return Game.Player != null && Game.Party.IsInParty;
    }

    public bool is_leader()
    {
        return Game.Player != null && Game.Party.IsInParty && Game.Party.IsLeader;
    }

    public string get_leader_name()
    {
        return Game.Player == null ? string.Empty : Game.Party.Leader?.Name ?? string.Empty;
    }

    public bool leave_party()
    {
        if (Game.Player == null || !Game.Party.IsInParty)
            return false;

        Game.Party.Leave();
        return true;
    }

    public bool invite(uint playerUniqueId)
    {
        if (Game.Player == null || playerUniqueId == 0)
            return false;

        Game.Party.Invite(playerUniqueId);
        return true;
    }

    public bool invite_player(string playerName)
    {
        if (Game.Player == null || string.IsNullOrWhiteSpace(playerName))
            return false;

        if (!SpawnManager.TryGetEntity<SpawnedPlayer>(
            p => string.Equals(p.Name, playerName, StringComparison.OrdinalIgnoreCase),
            out var target
        ))
            return false;

        Game.Party.Invite(target.UniqueId);
        return true;
    }

    public bool banish(string playerName)
    {
        if (Game.Player == null || !Game.Party.IsLeader || string.IsNullOrWhiteSpace(playerName))
            return false;

        var member = Game.Party.GetMemberByName(playerName);
        if (member == null)
            return false;

        member.Banish();
        return true;
    }
}
