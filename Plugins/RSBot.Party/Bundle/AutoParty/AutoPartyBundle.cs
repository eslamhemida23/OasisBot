using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects.Party;
using RSBot.Core.Objects.Spawn;
using RSBot.Party.Bundle.PartyMatching.Objects;

namespace RSBot.Party.Bundle.AutoParty;

internal class AutoPartyBundle
{
    /// <summary>
    ///     Last tick for checking party members
    /// </summary>
    private int _lastPartyListingCacheTick;

    /// <summary>
    ///     Last tick for checking party members
    /// </summary>
    private int _lastTick;

    /// <summary>
    ///     Party entiries cache
    /// </summary>
    private List<PartyEntry> _partyEntriesCache;

    private int _partyListRefreshInProgress;
    private int _partyJoinInProgress;

    /// <summary>
    ///     Initialize this instance
    /// </summary>
    public AutoPartyBundle()
    {
        EventManager.SubscribeEvent("OnTick", OnTick);
    }

    /// <summary>
    ///     Gets or sets the configuration.
    /// </summary>
    /// <value>
    ///     The configuration.
    /// </value>
    public AutoPartyConfig Config { get; set; }

    private void ApplyPartySettingsFromConfig()
    {
        if (!Game.Party.IsInParty)
            Game.Party.Settings = new PartySettings(
                Config.ExperienceAutoShare,
                Config.ItemAutoShare,
                Config.AllowInvitations
            );
    }

    /// <summary>
    ///     Refreshes this instance.
    /// </summary>
    public void Refresh()
    {
        Config = new AutoPartyConfig
        {
            PlayerList = PlayerConfig
                .GetArray<string>("RSBot.Party.AutoPartyList")
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            InviteAll = PlayerConfig.Get<bool>("RSBot.Party.InviteAll"),
            AcceptAll = PlayerConfig.Get<bool>("RSBot.Party.AcceptAll"),
            AcceptFromList = PlayerConfig.Get<bool>("RSBot.Party.AcceptList"),
            InviteFromList = PlayerConfig.Get<bool>("RSBot.Party.InviteList"),
            OnlyAtTrainingPlace = PlayerConfig.Get<bool>("RSBot.Party.AtTrainingPlace"),
            ExperienceAutoShare = PlayerConfig.Get<bool>("RSBot.Party.EXPAutoShare", true),
            ItemAutoShare = PlayerConfig.Get<bool>("RSBot.Party.ItemAutoShare", true),
            AllowInvitations = PlayerConfig.Get<bool>("RSBot.Party.AllowInvitations", true),
            AcceptIfBotIsStopped = PlayerConfig.Get<bool>("RSBot.Party.AcceptIfBotStopped"),
            LeaveIfMasterNot = PlayerConfig.Get<bool>("RSBot.Party.LeaveIfMasterNot"),
            LeaveIfMasterNotName = PlayerConfig.Get<string>("RSBot.Party.LeaveIfMasterNotName"),
            CenterPosition =
                (Kernel.Bot?.Botbase != null)
                    ? Kernel.Bot.Botbase.Area.Position
                    : (Game.Player?.Position ?? new RSBot.Core.Objects.Position(0, 0, 0)),
            AutoJoinByName = PlayerConfig.Get("RSBot.Party.AutoJoin.ByName", false),
            AutoJoinByTitle = PlayerConfig.Get("RSBot.Party.AutoJoin.ByTitle", false),
            AutoJoinByNameContent = PlayerConfig.Get("RSBot.Party.AutoJoin.Name", string.Empty),
            AutoJoinByTitleContent = PlayerConfig.Get("RSBot.Party.AutoJoin.Title", string.Empty),
            AlwaysFollowThePartyMaster = PlayerConfig.Get("RSBot.Party.AlwaysFollowPartyMaster", false),
        };

        ApplyPartySettingsFromConfig();
    }

    public void OnTick()
    {
        if (!Game.Ready || Game.Player == null || Game.Party == null)
            return;

        var elapsed = Kernel.TickCount - _lastTick;
        if (elapsed > 5000)
        {
            CheckForAutoPartyJoin();
            CheckForPlayers();

            _lastTick = Kernel.TickCount;
        }
    }

    /// <summary>
    ///     Checks for auto party join by condition
    /// </summary>
    private void CheckForAutoPartyJoin()
    {
        var config = Config;
        if (Game.Party.IsInParty || config == null)
            return;

        if (!config.AutoJoinByName && !config.AutoJoinByTitle)
            return;

        // Requesting pages and waiting for their responses must not block the 100ms
        // game component updater. Keep at most one refresh in flight.
        var elapsed = Kernel.TickCount - _lastPartyListingCacheTick;
        if (
            elapsed >= 60000
            && Interlocked.CompareExchange(ref _partyListRefreshInProgress, 1, 0) == 0
        )
        {
            _lastPartyListingCacheTick = Kernel.TickCount;
            var sessionProxy = Kernel.Proxy;
            Volatile.Write(ref _partyEntriesCache, new List<PartyEntry>());

            _ = Task.Run(() => RefreshPartyListCache(sessionProxy));
        }

        var entries = Volatile.Read(ref _partyEntriesCache);
        if (entries == null || entries.Count == 0)
            return;

        PartyEntry nameMatch = null;
        PartyEntry titleMatch = null;

        if (config.AutoJoinByName && !string.IsNullOrWhiteSpace(config.AutoJoinByNameContent))
        {
            nameMatch = entries.Find(p =>
                string.Equals(p.Leader, config.AutoJoinByNameContent, StringComparison.OrdinalIgnoreCase));
        }

        if (config.AutoJoinByTitle && !string.IsNullOrWhiteSpace(config.AutoJoinByTitleContent))
        {
            titleMatch = entries.Find(p =>
                !string.IsNullOrWhiteSpace(p.Title)
                && p.Title.Contains(config.AutoJoinByTitleContent, StringComparison.CurrentCultureIgnoreCase));
        }

        if (nameMatch == null && titleMatch == null)
            return;

        // Preserve the previous priority: try the configured leader name first,
        // then fall back to the title match if the first join request is rejected.
        if (Interlocked.CompareExchange(ref _partyJoinInProgress, 1, 0) != 0)
            return;

        var proxy = Kernel.Proxy;
        _ = Task.Run(() =>
        {
            try
            {
                if (
                    !Game.Ready
                    || Game.Party.IsInParty
                    || proxy == null
                    || !ReferenceEquals(proxy, Kernel.Proxy)
                )
                    return;

                if (nameMatch != null && Container.PartyMatching.Join(nameMatch.Id))
                    return;

                if (
                    titleMatch != null
                    && (nameMatch == null || titleMatch.Id != nameMatch.Id)
                    && Game.Ready
                    && !Game.Party.IsInParty
                    && ReferenceEquals(proxy, Kernel.Proxy)
                )
                    Container.PartyMatching.Join(titleMatch.Id);
            }
            catch (Exception error)
            {
                Log.Warn("[Party] Automatic party join failed: " + error);
            }
            finally
            {
                Interlocked.Exchange(ref _partyJoinInProgress, 0);
            }
        });
    }

    private void RefreshPartyListCache(RSBot.Core.Network.Proxy sessionProxy)
    {
        try
        {
            var refreshedEntries = new List<PartyEntry>(64);
            const int maxPagesPerRefresh = 64;
            byte expectedPageCount = 0;

            if (sessionProxy == null || !ReferenceEquals(sessionProxy, Kernel.Proxy) || !Game.Ready)
                return;

            for (var pageIndex = 0; pageIndex < maxPagesPerRefresh; pageIndex++)
            {
                if (
                    !Game.Ready
                    || Game.Party.IsInParty
                    || !ReferenceEquals(sessionProxy, Kernel.Proxy)
                )
                    return;

                var currentPage = Container.PartyMatching.RequestPartyList((byte)pageIndex);

                if (
                    currentPage == null
                    || currentPage.PageCount == 0
                    || currentPage.Parties == null
                )
                {
                    Log.Warn($"[Party] Auto-join party list page {pageIndex} was not received; stopping this refresh.");
                    break;
                }

                if (currentPage.Page != pageIndex)
                {
                    Log.Warn(
                        $"[Party] Auto-join received page {currentPage.Page} while requesting page {pageIndex}; " +
                        "stopping this refresh to avoid looping on an inconsistent response."
                    );
                    break;
                }

                if (expectedPageCount == 0)
                    expectedPageCount = currentPage.PageCount;
                else if (currentPage.PageCount != expectedPageCount)
                {
                    Log.Warn("[Party] Auto-join party list page count changed during refresh; keeping the pages received so far.");
                    break;
                }

                refreshedEntries.AddRange(currentPage.Parties);

                if (currentPage.Page >= currentPage.PageCount - 1)
                    break;

                if (pageIndex == maxPagesPerRefresh - 1)
                    Log.Warn($"[Party] Auto-join party list exceeded the {maxPagesPerRefresh}-page safety limit.");
            }

            if (
                Game.Ready
                && !Game.Party.IsInParty
                && ReferenceEquals(sessionProxy, Kernel.Proxy)
            )
                Volatile.Write(ref _partyEntriesCache, refreshedEntries);
        }
        catch (Exception error)
        {
            Log.Warn("[Party] Auto-join party list refresh failed: " + error);
        }
        finally
        {
            Interlocked.Exchange(ref _partyListRefreshInProgress, 0);
        }
    }

    /// <summary>
    ///     Checks for players that can be invited.
    /// </summary>
    public void CheckForPlayers()
    {
        var config = Config;
        if (config == null || Game.Player == null || Game.Party == null)
            return;

        if (
            Game.Party.IsInParty
            && !Game.Party.IsLeader
            && config.LeaveIfMasterNot
            && !string.IsNullOrWhiteSpace(config.LeaveIfMasterNotName)
        )
            if (config.LeaveIfMasterNotName != Game.Party.Leader.Name)
                Game.Party.Leave();

        ApplyPartySettingsFromConfig();

        if (!Game.Party.CanInvite || Game.Party.Settings == null)
            return;

        var limit = 8;
        if (!Game.Party.Settings.ExperienceAutoShare && !Game.Party.Settings.ItemAutoShare)
            limit = 4;

        if (Game.Party.Members?.Count > limit)
            return;

        if (config.OnlyAtTrainingPlace && Game.Player.Movement.Source.DistanceTo(config.CenterPosition) > 50)
            return;

        if (!SpawnManager.TryGetEntities<SpawnedPlayer>(out var players))
            return;

        foreach (var player in players)
        {
            if (
                Game.Party.Members?.Any(member =>
                    string.Equals(member.Name, player.Name, StringComparison.OrdinalIgnoreCase)) == true
            )
                continue;

            if (config.InviteAll)
            {
                Game.Party.Invite(player.UniqueId);
                continue;
            }

            if (
                config.InviteFromList
                && config.PlayerList?.Any(name =>
                    string.Equals(name?.Trim(), player.Name, StringComparison.OrdinalIgnoreCase)) == true
            )
                Game.Party.Invite(player.UniqueId);
        }
    }
}
