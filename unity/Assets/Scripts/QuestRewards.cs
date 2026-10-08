using System.Text;

/// <summary>
/// WHAT A FINISHED QUEST PAYS, and the one place it is paid — server only. Called the moment a quest's last step is done
/// (<see cref="NetPlayer.QuestsMoved"/>), in the same frame, so the reward and the finished record go out in the same
/// save, written in one transaction: never a reward without the record that stops it being paid again, never the record
/// without the reward.
///
/// <para>Experience goes through the same door a kill's does (<see cref="Character.AddXP"/>), so the weapon in hand
/// earns its share of mastery exactly as it would from the fight.</para>
/// </summary>
public static class QuestRewards
{
    public static void Pay(Character c, QuestDef def, NetPlayer shell)
    {
        if (c == null || def == null) return;
        var r = def.reward;
        var said = new StringBuilder("Quest complete: ").Append(def.title).Append('.');
        if (r != null && r.Any)
        {
            said.Append(" You received ");
            bool first = true;
            if (r.experience > 0)
            {
                int levels = c.AddXP(r.experience, out int ringLevels);
                GameNetwork.TellOwnerTheyEarned(c, r.experience, 0, levels, ringLevels);
                Part(said, ref first, r.experience + " experience");
            }
            if (r.gold > 0)
            {
                c.AddGold(r.gold);
                Part(said, ref first, r.gold + " gold");
            }
            if (r.items != null)
                foreach (var it in r.items)
                {
                    var item = it != null ? ItemCatalog.Resolve(it.itemId) : null;
                    if (item == null || it.count <= 0) continue;
                    c.TakeIntoBag(item, it.count);
                    Part(said, ref first, it.count + " " + item.displayName);
                }
            said.Append('.');
            shell?.PushBagToOwner();
        }
        PlaySession.QuestFinished(c);
        GameLog.Info(LogCat.Loot, string.Concat(SpawnIds.MapOf(c), " ", SpawnIds.Label(c), " (",
                                                 GameNetwork.CharacterIdOf(c), ") finished quest ", def.id, " — ", said.ToString()));
        shell?.TellQuestLine(said.ToString());
    }

    static void Part(StringBuilder sb, ref bool first, string what)
    {
        if (!first) sb.Append(", ");
        sb.Append(what);
        first = false;
    }
}
