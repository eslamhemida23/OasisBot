using System;
using System.Linq;
using RSBot.Core.Event;
using RSBot.Core.Objects.Skill;

namespace RSBot.Core.Network.Handler.Agent.Skill;

internal class SkillLearnResponse : IPacketHandler
{
    /// <summary>
    ///     Gets or sets the opcode.
    /// </summary>
    /// <value>
    ///     The opcode.
    /// </value>
    public ushort Opcode => 0xB0A1;

    /// <summary>
    ///     Gets or sets the destination.
    /// </summary>
    /// <value>
    ///     The destination.
    /// </value>
    public PacketDestination Destination => PacketDestination.Client;

    /// <summary>
    ///     Handles the packet.
    /// </summary>
    /// <param name="packet">The packet.</param>
    public void Invoke(Packet packet)
    {
        var result = packet.ReadByte();
        if (result != 1)
            return;

        var skillId = packet.ReadUInt();
        var skill = Game.ReferenceManager.GetRefSkill(skillId);

        var knownSkills = Game.Player.Skills.KnownSkills;
        var existingSkill = Game.Player.Skills.GetSkillRecordByName(skill.GetRealName());

        // Skill rank names can change between books/levels. The exact-name lookup alone
        // then treats a rank-up as a new skill and leaves the previous skill ID behind.
        if (existingSkill == null && !string.IsNullOrWhiteSpace(skill.Basic_Group))
        {
            var sameFamily = knownSkills
                .Where(known =>
                    known.Record != null
                    && !string.IsNullOrWhiteSpace(known.Record.Basic_Group)
                    && string.Equals(
                        known.Record.Basic_Group,
                        skill.Basic_Group,
                        StringComparison.OrdinalIgnoreCase
                    ))
                .ToArray();

            if (sameFamily.Length == 1)
                existingSkill = sameFamily[0];
        }

        if (existingSkill == null)
        {
            var skillInfo = new SkillInfo(skillId, true);
            knownSkills.Add(skillInfo);
            EventManager.FireEvent("OnSkillLearned", skillInfo);
            return;
        }

        if (existingSkill.Id == skillId)
        {
            existingSkill.Enabled = true;
            return;
        }

        var oldSkill = existingSkill;
        var upgradedSkill = new SkillInfo(skillId, existingSkill.Enabled)
        {
            Token = existingSkill.Token
        };

        var existingIndex = knownSkills.IndexOf(existingSkill);
        if (existingIndex >= 0)
            knownSkills[existingIndex] = upgradedSkill;
        else
            knownSkills.Add(upgradedSkill);

        EventManager.FireEvent("OnSkillUpgraded", oldSkill, upgradedSkill);
    }
}
