using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects.Party;

namespace RSBot.Party
{
    public class PartyManager
    {
        public PartyManager()
        {
            SubscribeEvents();
        }

        private void SubscribeEvents()
        {
            EventManager.SubscribeEvent("OnEnterGame", OnEnterGame);
            EventManager.SubscribeEvent("OnDeletePartyEntry", OnDeletePartyEntry);
            EventManager.SubscribeEvent("OnPartyMemberLeave", new Action<PartyMember>(OnPartyMemberLeave));
            EventManager.SubscribeEvent("OnPartyMemberBanned", new Action<PartyMember>(OnPartyMemberBanned));
            EventManager.SubscribeEvent("OnPartyDismiss", OnPartyDismiss);
            EventManager.SubscribeEvent("OnAgentServerDisconnected", OnPartyDismiss);
            EventManager.SubscribeEvent("OnPartyLeaderChange", OnPartyData);
            EventManager.SubscribeEvent("OnPartyData", OnPartyData);
        }

        private async void OnEnterGame()
        {
            var sessionProxy = Kernel.Proxy;
            if (sessionProxy == null)
                return;

            try
            {
                await Task.Delay(5000);

                if (
                    !Game.Ready
                    || !ReferenceEquals(Kernel.Proxy, sessionProxy)
                    || !sessionProxy.IsConnectedToAgentserver
                    || Bundle.Container.PartyMatching?.Config == null
                )
                    return;

                var matching = Bundle.Container.PartyMatching;
                if (matching.Config.AutoReform && !matching.HasMatchingEntry)
                    matching.Create();
            }
            catch (Exception error)
            {
                Log.Error("[Party] Enter-game auto-reform handler failed: " + error);
            }
        }

        private void OnPartyMemberLeave(PartyMember member)
        {
            if (Bundle.Container.PartyMatching.Config.AutoReform)
                if (
                    Game.Party != null
                    && !Bundle.Container.PartyMatching.HasMatchingEntry
                    && Game.Party.Members?.Count < Game.Party.Settings.MaxMember
                )
                    Bundle.Container.PartyMatching.Create();
        }

        private void OnPartyMemberBanned(PartyMember member)
        {
            if (member.Name != Game.Player.Name)
            {
                if (Bundle.Container.PartyMatching.Config.AutoReform)
                    if (
                        Game.Party != null
                        && !Bundle.Container.PartyMatching.HasMatchingEntry
                        && Game.Party.Members?.Count < Game.Party.Settings.MaxMember
                    )
                        Bundle.Container.PartyMatching.Create();
            }
            else
                OnPartyDismiss();
        }

        public static void OnPartyDismiss()
        {
            if (!Game.Ready)
                return;

            Bundle.Container.PartyMatching.HasMatchingEntry = false;
            OnDeletePartyEntry();
        }

        private static void OnDeletePartyEntry()
        {
            Bundle.Container.PartyMatching.Id = 0;

            if (Game.Ready && Bundle.Container.PartyMatching.Config.AutoReform)
                Bundle.Container.PartyMatching.Create();
        }

        public void OnPartyData()
        {
            try
            {
                if (Game.Party.Members == null)
                {
                    OnPartyDismiss();
                    return;
                }
            }
            catch { }
        }

        private static void SetAndSave<T>(string key, T value)
        {
            PlayerConfig.Set(key, value);
            PlayerConfig.Save();
        }

        public static string[] GetBuffingGroups()
        {
            return PlayerConfig.GetArray<string>("RSBot.Party.Buffing.Groups");
        }

        public static void SaveBuffingGroups(IEnumerable<string> buffs)
        {
            PlayerConfig.SetArray("RSBot.Party.Buffing.Groups", buffs);
            PlayerConfig.Save();
        }

        public static string GetBuffingMembers()
        {
            return PlayerConfig.Get("RSBot.Party.Buffing", string.Empty);
        }

        public static void SaveBuffingPartyMembers(string members)
        {
            SetAndSave("RSBot.Party.Buffing", members);
            EventManager.FireEvent("OnPartyBuffSettingsChanged");
        }

        public static void SaveAutoPartyPlayerList(IEnumerable<string> players)
        {
            PlayerConfig.SetArray("RSBot.Party.AutoPartyList", players);
            PlayerConfig.Save();

            Bundle.Container.Refresh();
        }

        public static void SaveCommandPlayersList(IEnumerable<string> players)
        {
            PlayerConfig.SetArray("RSBot.Party.Commands.PlayersList", players);
            PlayerConfig.Save();

            Bundle.Container.Refresh();
        }

        public static string GetSelectedGroup()
        {
            return PlayerConfig.Get("RSBot.Party.Buffing.SelectedGroup", "Default");
        }

        public static void LeaveParty()
        {
            Game.Party?.Leave();
        }

        public static void SetPartyExpAutoShare(bool enabled)
        {
            SetAndSave("RSBot.Party.EXPAutoShare", enabled);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetPartyItemAutoShare(bool enabled)
        {
            SetAndSave("RSBot.Party.ItemAutoShare", enabled);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetPartyAllowInvites(bool enabled)
        {
            SetAndSave("RSBot.Party.AllowInvitations", enabled);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetPartyAutoAcceptInvitesAll(bool enabled)
        {
            SetAndSave("RSBot.Party.AcceptAll", enabled);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetPartyAutoAcceptInvitesFromList(bool enabled)
        {
            SetAndSave("RSBot.Party.AcceptList", enabled);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetPartyAutoInviteAll(bool enabled)
        {
            SetAndSave("RSBot.Party.InviteAll", enabled);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetPartyAutoInviteList(bool enabled)
        {
            SetAndSave("RSBot.Party.InviteList", enabled);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetPartyAutoAcceptAtTrainingPlace(bool enabled)
        {
            SetAndSave("RSBot.Party.AtTrainingPlace", enabled);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetPartyAutoAcceptIfBotStopped(bool enabled)
        {
            SetAndSave("RSBot.Party.AcceptIfBotStopped", enabled);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetPartyAutoLeaveIfMasterNot(bool enabled)
        {
            SetAndSave("RSBot.Party.LeaveIfMasterNot", enabled);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetPartyAutoLeaveMaster(string masterName)
        {
            SetAndSave("RSBot.Party.LeaveIfMasterNotName", masterName);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetPartyCommandListenFromMaster(bool enabled)
        {
            SetAndSave("RSBot.Party.Commands.ListenFromMaster", enabled);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetPartyCommandListenFromList(bool enabled)
        {
            SetAndSave("RSBot.Party.Commands.ListenOnlyList", enabled);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetFollowMaster(bool enabled)
        {
            SetAndSave("RSBot.Party.AlwaysFollowPartyMaster", enabled);

            Bundle.Container.Refresh();
        }

        public static void SetPartyAutoJoinByName(bool enabled)
        {
            SetAndSave("RSBot.Party.AutoJoin.ByName", enabled);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetPartyAutoJoinByTitle(bool enabled)
        {
            SetAndSave("RSBot.Party.AutoJoin.ByTitle", enabled);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetPartyAutoJoinName(string name)
        {
            SetAndSave("RSBot.Party.AutoJoin.Name", name);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void SetPartyAutoJoinTitle(string title)
        {
            SetAndSave("RSBot.Party.AutoJoin.Title", title);
            Bundle.Container.AutoParty.Refresh();
        }

        public static void BanishMemberFromParty(string memberName)
        {
            if (Game.Party.IsLeader)
                Game.Party.GetMemberByName(memberName)?.Banish();
        }

        public static void DeletePartyMatchingEntry()
        {
            if (!Game.Ready)
                return;
            Bundle.Container.PartyMatching.Config.AutoReform = false;
            Bundle.Container.PartyMatching.Delete();
        }
    }
}
