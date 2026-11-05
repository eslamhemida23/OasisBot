using System;
using Python.Runtime;
using RSBot.Python.Components.API.Interface;
using RSBot.Core;
using RSBot.Core.Objects.Cos;

namespace RSBot.Python.Components.API.Core.Pets;

public class PetsAPI : IPythonPlugin
{
    public string ModuleName => "pets";

    public void Init(RSBot.Python.Views.Main main) { }

    private static bool IsConnected => Game.Player != null && Kernel.Proxy?.IsConnectedToAgentserver == true;

    private static PyDict BuildPet(Cos pet, string kind)
    {
        using (Py.GIL())
        {
            var result = new PyDict();
            if (pet == null)
                return result;

            result.SetItem("type", new PyString(kind));
            result.SetItem("uid", new PyInt(pet.UniqueId));
            result.SetItem("name", new PyString(pet.Name ?? string.Empty));
            result.SetItem("level", new PyInt(pet.Level));
            result.SetItem("hp", new PyInt(pet.Health));
            result.SetItem("max_hp", new PyInt(pet.MaxHealth));
            result.SetItem("owner_uid", new PyInt(pet.OwnerUniqueId));
            return result;
        }
    }

    public PyDict get_active_pets()
    {
        using (Py.GIL())
        {
            var result = new PyDict();
            var player = Game.Player;
            if (player == null)
                return result;

            if (player.Growth != null)
                result.SetItem("growth", BuildPet(player.Growth, "growth"));
            if (player.Fellow != null)
                result.SetItem("fellow", BuildPet(player.Fellow, "fellow"));
            if (player.Transport != null)
                result.SetItem("transport", BuildPet(player.Transport, "transport"));
            if (player.JobTransport != null)
                result.SetItem("job_transport", BuildPet(player.JobTransport, "job_transport"));
            if (player.AbilityPet != null)
                result.SetItem("ability", BuildPet(player.AbilityPet, "ability"));
            if (player.Vehicle != null)
                result.SetItem("vehicle", BuildPet(player.Vehicle, "vehicle"));

            return result;
        }
    }

    public bool summon_growth()
    {
        return IsConnected && Game.Player.SummonGrowth();
    }

    public bool summon_fellow()
    {
        return IsConnected && Game.Player.SummonFellow();
    }

    public bool summon_vehicle()
    {
        return IsConnected && Game.Player.SummonVehicle();
    }

    public bool summon_ability_pet()
    {
        return IsConnected && Game.Player.SummonAbilityPet();
    }

    public bool revive_growth()
    {
        return IsConnected && Game.Player.ReviveGrowth();
    }

    public bool revive_fellow()
    {
        return IsConnected && Game.Player.ReviveFellow();
    }

    public bool dismount()
    {
        return IsConnected && (Game.Player?.Vehicle?.Dismount() ?? false);
    }

    public bool terminate(string petType)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(petType))
            return false;

        return petType.Trim().ToLowerInvariant() switch
        {
            "growth" => Game.Player.Growth?.Terminate() ?? false,
            "fellow" => Game.Player.Fellow?.Terminate() ?? false,
            "transport" => Game.Player.Transport?.Terminate() ?? false,
            "job_transport" => Game.Player.JobTransport?.Terminate() ?? false,
            "ability" => Game.Player.AbilityPet?.Terminate() ?? false,
            "vehicle" => Game.Player.Vehicle?.Dismount() ?? false,
            _ => false
        };
    }

    public bool use_hp_potion(string petType)
    {
        if (Game.Player == null || string.IsNullOrWhiteSpace(petType))
            return false;

        return petType.Trim().ToLowerInvariant() switch
        {
            "growth" => Game.Player.Growth?.UseHealthPotion() ?? false,
            "fellow" => Game.Player.Fellow?.UseHealthPotion() ?? false,
            "transport" => Game.Player.Transport?.UseHealthPotion() ?? false,
            "job_transport" => Game.Player.JobTransport?.UseHealthPotion() ?? false,
            _ => false
        };
    }

    public bool use_hunger_potion()
    {
        if (!IsConnected)
            return false;

        if (Game.Player.Growth != null)
            return Game.Player.Growth.UseHungerPotion();

        if (Game.Player.Fellow != null)
            return Game.Player.Fellow.UseSatietyPotion();

        return false;
    }
}
