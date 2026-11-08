using NewKOF.BattleCore.Team;
namespace NewKOF.BattleCore.AI;
public static class AiTeamBuilder
{
    public static TeamDefinition Create(string teamId,IEnumerable<string> available,uint seed)
    {
        ArgumentNullException.ThrowIfNull(available);
        var ids=available.Distinct(StringComparer.Ordinal).OrderBy(id=>id,StringComparer.Ordinal).ToArray();
        if(ids.Length==0 || ids.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("No valid AI roster candidates.");
        var members=new TeamMemberDefinition[3];
        for(int slot=0;slot<3;slot++) { seed=AiRandom.Next(seed); members[slot]=new TeamMemberDefinition(ids[seed%(uint)ids.Length],1000); }
        return new TeamDefinition(teamId,members);
    }
}
