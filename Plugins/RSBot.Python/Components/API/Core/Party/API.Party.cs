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
