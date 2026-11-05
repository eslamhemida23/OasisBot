using System;
using Python.Runtime;
using RSBot.Python.Components.API.Interface;
using RSBot.Core;
using RSBot.Core.Objects;

namespace RSBot.Python.Components.API.Core.Player;

public class PlayerAPI : IPythonPlugin
{
    public string ModuleName => "player";

    public void Init(RSBot.Python.Views.Main main) { }

    private static int Percentage(int current, int maximum)
    {
        if (maximum <= 0)
            return 0;

        return Math.Clamp((int)Math.Round(current * 100d / maximum), 0, 100);
    }

    public PyDict get_status()
    {
        using (Py.GIL())
        {
            var result = new PyDict();
            var player = Game.Player;
            if (player == null)
                return result;

            result.SetItem("name", new PyString(player.Name ?? string.Empty));
            result.SetItem("hp", new PyInt(player.Health));
            result.SetItem("mp", new PyInt(player.Mana));
            result.SetItem("max_hp", new PyInt(player.MaximumHealth));
            result.SetItem("max_mp", new PyInt(player.MaximumMana));
            result.SetItem("hp_percentage", new PyInt(Percentage(player.Health, player.MaximumHealth)));
            result.SetItem("mp_percentage", new PyInt(Percentage(player.Mana, player.MaximumMana)));
            result.SetItem("is_dead", new PyBool(player.State.LifeState == LifeState.Dead));
            result.SetItem("is_berzerking", new PyBool(player.Berzerking));
            result.SetItem("can_berzerk", new PyBool(player.CanEnterBerzerk));
            result.SetItem("level", new PyInt(player.Level));
            result.SetItem("x", new PyFloat(player.Position.X));
            result.SetItem("y", new PyFloat(player.Position.Y));
            result.SetItem("z", new PyFloat(player.Position.ZOffset));
            result.SetItem("region", new PyInt(player.Position.Region.Id));
            return result;
        }
    }

    public bool is_dead()
    {
        return Game.Player == null || Game.Player.State.LifeState == LifeState.Dead;
    }

    public bool move_to(float x, float y, ushort region, bool wait = false)
    {
        if (Game.Player == null)
            return false;

        try
        {
            return Game.Player.MoveTo(new Position(x, y, new Region(region)), wait);
        }
        catch (Exception ex)
        {
            Log.Warn($"[Python-API] player.move_to failed: {ex.Message}");
            return false;
        }
    }

    public bool use_return_scroll()
    {
        return Game.Player != null && Game.Player.UseReturnScroll();
    }

    public bool use_hp_potion()
    {
        return Game.Player != null && Game.Player.UseHealthPotion();
    }

    public bool use_mp_potion()
    {
        return Game.Player != null && Game.Player.UseManaPotion();
    }

    public bool use_berserk()
    {
        if (Game.Player == null || !Game.Player.CanEnterBerzerk)
            return false;

        Game.Player.EnterBerzerkMode();
        return true;
    }
}
