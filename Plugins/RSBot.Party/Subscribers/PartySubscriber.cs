using System.Linq;
using RSBot.Core;
using RSBot.Core.Event;
using RSBot.Party.Bundle;

namespace RSBot.Party.Subscribers;

internal class PartySubscriber
{
    /// <summary>
    ///     Gets the subscribed events.
    /// </summary>
    /// <returns></returns>
    public static void SubscribeEvents()
    {
        EventManager.SubscribeEvent("OnPartyRequest", OnPartyRequest, true);
        EventManager.SubscribeEvent("OnEnterGame", OnEnterGame, true);
        EventManager.SubscribeEvent("OnLoadCharacter", OnLoadCharacter, true);
    }

    private static void OnEnterGame()
    {
        // A character/client restart can leave the previous session's Party members
        // cached in Game.Party. Clear that snapshot before loading this character's
        // settings, otherwise the auto-invite loop may incorrectly skip the old names.
        Game.Party?.Clear();

        // PlayerConfig has already been switched to the selected character by
        // CharacterSelectionJoinRequest. Reset and initialize the OnTick-backed party
        // service now so the first invite scan uses this character's own saved profile.
        Container.Refresh();
    }

    private static void OnLoadCharacter()
    {
        // Refresh again once Game.Player and the real training position are available.
        Container.Refresh();
        EventManager.FireEvent("OnPartyConfigLoaded");
    }

    /// <summary>
    ///     Checks the request.
    /// </summary>
    /// <returns></returns>
    private static bool CheckRequest()
    {
        var config = Container.AutoParty?.Config;
        if (config == null || Game.Player == null || Game.Party == null)
            return false;

        //Check for the pending request
        if (!Game.Party.HasPendingRequest)
            return false;

        Log.NotifyLang("PartyPlayerInvite", Game.AcceptanceRequest.Player.Name);

        //Check if we are near the training place
        if (
            config.OnlyAtTrainingPlace
            && Game.Player.Movement.Source.DistanceTo(config.CenterPosition) > 50
        )
            return false;

        //Check if the inviting player matches out party list
        if (
            config.AcceptFromList
            && config.PlayerList?.Any(name =>
                string.Equals(name?.Trim(), Game.AcceptanceRequest.Player.Name, System.StringComparison.OrdinalIgnoreCase)) == true
        )
            return true;

        return config.AcceptAll;
    }

    /// <summary>
    ///     Will be fired when the player is being invited to a party
    /// </summary>
    private static void OnPartyRequest()
    {
        var config = Container.AutoParty?.Config;
        if (config == null || Game.Party == null || !Game.Party.HasPendingRequest)
            return;

        if (!Kernel.Bot.Running && !config.AcceptIfBotIsStopped)
            return;

        if (CheckRequest())
            Game.AcceptanceRequest.Accept();
        else
            Game.AcceptanceRequest.Refuse();
    }
}
