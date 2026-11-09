using System;
using System.Collections.Generic;
using System.Linq;
using RSBot.Core;
using RSBot.Core.Components;
using RSBot.Core.Event;
using RSBot.Core.Objects;
using RSBot.Core.Objects.Party;
using RSBot.Core.Objects.Skill;
using RSBot.Core.Objects.Spawn;

namespace RSBot.Training.Bundle.PartyBuffing;

internal class PartyBuffingBundle : IBundle
{
    /// <summary>
    ///     <c>true</c> if refreshing this bundle; otherwise <c>false</c>
    /// </summary>
    private bool _refreshing;

    /// <summary>
    ///     The buffing party members
    /// </summary>
    private List<BuffingPartyMember> BuffingPartyMembers;

    /// <summary>
    ///     Initialize the instance of <seealso cref="PartyBuffingBundle" />
    /// </summary>
    public PartyBuffingBundle()
    {
        EventManager.SubscribeEvent("OnPartyBuffSettingsChanged", OnPartyBuffSettingsChanged);
        EventManager.SubscribeEvent("OnSkillUpgraded", new Action<SkillInfo, SkillInfo>(OnSkillUpgraded));
        EventManager.SubscribeEvent("OnWithdrawSkill", new Action<SkillInfo, SkillInfo>(OnSkillWithdrawn));
    }

    /// <summary>
    ///     Invoke the bundle
    /// </summary>
    public void Invoke()
    {
        if (_refreshing)
            return;

        if (Game.Player.HasActiveVehicle)
            return;

        var selectedGroup = PlayerConfig.Get("RSBot.Party.Buffing.SelectedGroup", "Default");

        SpawnManager.TryGetEntities<SpawnedPlayer>(
            p => BuffingPartyMembers.Any(s => s.Group == selectedGroup && s.Name == p.Name),
            out var members
        );

        foreach (var member in members)
        {
            var buffingMember = BuffingPartyMembers.Find(p => p.Name == member.Name);
            if (buffingMember == null)
                continue;

            if (buffingMember.Buffs.Count == 0)
                continue;

            //if party member is dead, dont try to buff
            if (member.State.LifeState == LifeState.Dead)
                continue;

            Log.Status($"Buffing party");

            var activeBuffs = member.State.ActiveBuffs;

            foreach (var buff in buffingMember.Buffs)
            {
                var skill = Game.Player.Skills.GetSkillInfoById(buff);

                if (skill == null || skill.HasCooldown)
                    continue;

                if (
                    !skill.Record.TargetGroup_Ally
                    && skill.Record.TargetGroup_Party
                    && !(Game.Party?.Members?.Any(p => p.Name == member.Name) ?? false)
                )
                {
                    continue;
                }

                var isActive = member.State.HasActiveBuff(skill, out var info);
                if (isActive && skill.Isbugged && info.Isbugged)
                {
                    Log.Notify($"The buff on {member.Name} [{skill.Token}-{skill.Record?.GetRealName()}] expired");

                    skill?.Reset();
                    continue;
                }

                if (isActive)
                    continue;

                Log.Status($"Buffing {skill.Record?.GetRealName()} party member {member.Name}");
                skill.Cast(member.UniqueId, true);
            }
        }
    }

    /// <summary>
    ///     Refresh the bundle
    /// </summary>
    public void Refresh()
    {
        _refreshing = true;

        try
        {
            var members = ReadBuffingMembers();
            var migrated = MigrateLegacySkillIds(members);

            BuffingPartyMembers = members;

            if (migrated)
            {
                SaveBuffingMembers(members);
                Log.Notify("[Party] Updated saved party-buff skill references to the currently learned skill IDs.");
                EventManager.FireEvent("OnPartyBuffSkillsChanged");
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    public void Stop()
    {
        //Nothing to do here
    }

    private static List<BuffingPartyMember> ReadBuffingMembers()
    {
        var members = new List<BuffingPartyMember>();
        var settings = PlayerConfig.Get("RSBot.Party.Buffing", string.Empty);

        foreach (var item in settings.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                members.Add(new BuffingPartyMember(item));
            }
            catch (Exception error)
            {
                Log.Warn("[Party] Ignoring a malformed party-buff member setting: " + error.Message);
            }
        }

        return members;
    }

    private static void SaveBuffingMembers(IEnumerable<BuffingPartyMember> members)
    {
        PlayerConfig.Set(
            "RSBot.Party.Buffing",
            string.Concat(members.Select(member => member.Serialize()))
        );
        PlayerConfig.Save();
    }

    private static bool MigrateLegacySkillIds(IEnumerable<BuffingPartyMember> members)
    {
        var knownSkills = Game.Player?.Skills?.KnownSkills;
        if (knownSkills == null || knownSkills.Count == 0)
            return false;

        var changed = false;

        foreach (var member in members)
        {
            for (var index = 0; index < member.Buffs.Count; index++)
            {
                var savedId = member.Buffs[index];
                if (knownSkills.Any(skill => skill.Id == savedId))
                    continue;

                var oldRecord = Game.ReferenceManager?.GetRefSkill(savedId);
                if (oldRecord == null || string.IsNullOrWhiteSpace(oldRecord.Basic_Group))
                    continue;

                var candidates = knownSkills
                    .Where(skill =>
                        skill.Record != null
                        && (skill.Record.TargetGroup_Party || skill.Record.TargetGroup_Ally)
                        && string.Equals(
                            skill.Record.Basic_Group,
                            oldRecord.Basic_Group,
                            StringComparison.OrdinalIgnoreCase
                        ))
                    .ToList();

                // Some skill books share the same buff series with a more specific
                // Basic_Group. Use the same overlap/family relationship as the party UI,
                // but only migrate when there is exactly one unambiguous learned skill.
                if (candidates.Count == 0 && oldRecord.Action_Overlap != 0)
                {
                    candidates = knownSkills
                        .Where(skill =>
                            skill.Record != null
                            && (skill.Record.TargetGroup_Party || skill.Record.TargetGroup_Ally)
                            && skill.Record.Action_Overlap == oldRecord.Action_Overlap
                            && oldRecord.Basic_Group.StartsWith(
                                skill.Record.Basic_Group ?? string.Empty,
                                StringComparison.OrdinalIgnoreCase
                            ))
                        .ToList();
                }

                if (candidates.Count != 1 || candidates[0].Id == 0)
                    continue;

                member.Buffs[index] = candidates[0].Id;
                changed = true;
            }

            // Avoid duplicate IDs after two old entries resolve to the same learned skill.
            var unique = member.Buffs.Distinct().ToList();
            if (unique.Count != member.Buffs.Count)
            {
                member.Buffs = unique;
                changed = true;
            }
        }

        return changed;
    }

    private void OnSkillUpgraded(SkillInfo oldSkill, SkillInfo newSkill)
    {
        UpdateSavedSkillReference(oldSkill, newSkill, removeWhenUnchanged: false);
    }

    private void OnSkillWithdrawn(SkillInfo oldSkill, SkillInfo newSkill)
    {
        UpdateSavedSkillReference(oldSkill, newSkill, removeWhenUnchanged: true);
    }

    private void UpdateSavedSkillReference(SkillInfo oldSkill, SkillInfo newSkill, bool removeWhenUnchanged)
    {
        if (oldSkill == null || oldSkill.Id == 0)
            return;

        var oldId = oldSkill.Id;
        var newId = newSkill?.Id ?? 0;
        if (!removeWhenUnchanged && (newId == 0 || newId == oldId))
            return;

        var members = ReadBuffingMembers();
        var changed = false;

        foreach (var member in members)
        {
            var index = member.Buffs.IndexOf(oldId);
            if (index < 0)
                continue;

            if (newId == 0 || (removeWhenUnchanged && newId == oldId))
            {
                member.Buffs.RemoveAt(index);
            }
            else
            {
                member.Buffs[index] = newId;
            }

            var unique = member.Buffs.Distinct().ToList();
            member.Buffs = unique;
            changed = true;
        }

        if (!changed)
            return;

        SaveBuffingMembers(members);
        Refresh();
        EventManager.FireEvent("OnPartyBuffSkillsChanged");

        Log.Notify(
            removeWhenUnchanged && newId == oldId
                ? $"[Party] Removed withdrawn skill {oldId} from assigned party buffs."
                : $"[Party] Updated assigned party buffs from skill {oldId} to {newId}."
        );
    }

    /// <summary>
    ///     Update the <see cref="BuffingPartyMembers" /> list when settings changed from another plugins
    /// </summary>
    private void OnPartyBuffSettingsChanged()
    {
        Refresh();
    }
}
