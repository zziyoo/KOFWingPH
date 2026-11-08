using NewKOF.BattleCore.AI;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Data;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Match;
using NewKOF.BattleCore.Team;
using NewKOF.BattleCore.State;
using NewKOF.BattleCore.Presentation;
using NewKOF.BattleCore.Simulation;
using Xunit;
namespace NewKOF.BattleCore.Tests;
public class AiMatchDeterminismTests
{
    private static readonly FighterCatalog Catalog=FighterDataLoader.LoadCatalog(new[]{"Kai","Rei"}.Select(id=>new FighterDataDocument(id,
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"data","fighters",id+".json")))));
    private sealed record Entry(int Scope,FrameInput Input,BattleState State,BattleEvent[] Events,string Match,AiPolicyState P1,AiPolicyState P2);
    private static List<Entry> Run(bool team,double fps,int batch)
    {
        var entries=new List<Entry>(); AiInputSource source=null!;
        void Capture(PresentationFrame frame,string match)=>entries.Add(new(frame.Scope,source.LastInput!.Value,frame.StateAfter,
            frame.Events.ToArray(),match,source.PolicyState(0)!,source.PolicyState(1)!));
        AiInputSource Source(Func<BattleState> state,FightRoster roster,InputBuffer buffer,int scope)=>new(state,roster,buffer,
            new AiController(AiDifficultyProfile.For(AiDifficulty.Hard),AiTactic.Offensive),
            new AiController(AiDifficultyProfile.For(AiDifficulty.Expert),AiTactic.Defensive),AiRandom.DeriveSeed(42,0,scope,0),scope:scope);
        int safety=0;
        if(!team) {
            MatchSession match=null!;
            match=new MatchSession(new MatchRules(roundTimeSeconds:5),(a,b)=>Catalog.CreateRoster(a??"Kai",b??"Rei"),
                framePresented:f=>Capture(f,match.State.ToString()));
            match.SelectFighters("Kai","Rei");match.StartMatch();
            while(match.State.Phase!=MatchPhase.MatchFinished && safety++<20000) {
                source=Source(()=>match.CurrentBattleState!,Catalog.CreateRoster("Kai","Rei"),match.RoundInputBuffer,match.State.RoundNumber);
                while(match.State.IsRoundLive && safety++<20000) {
                    if(fps==0) match.StepExact(batch,source); else match.Advance(1d/fps,source);
                }
                if(match.State.Phase==MatchPhase.BetweenRounds) match.StartNextRound();
            }
            Assert.Equal(MatchPhase.MatchFinished,match.State.Phase);
        } else {
            TeamDefinition Team(string id)=>new(id,new[]{new TeamMemberDefinition("Kai",1000),new TeamMemberDefinition("Rei",1000),new TeamMemberDefinition("Kai",1000)});
            TeamMatchSession match=null!;
            match=new TeamMatchSession(new TeamMatchRules(5),Catalog.CreateRoster,framePresented:f=>Capture(f,match.State.ToString()));
            match.SelectTeams(Team("P1"),Team("P2"));match.StartMatch();
            while(match.State.Phase!=TeamMatchPhase.MatchFinished && safety++<20000) {
                var roster=Catalog.CreateRoster(match.State.P1!.ActiveFighterId,match.State.P2!.ActiveFighterId);
                source=Source(()=>match.CurrentBattleState!,roster,match.BoutInputBuffer,match.State.BoutNumber);
                while(match.State.IsBoutLive && safety++<20000) {
                    if(fps==0) match.StepExact(batch,source); else match.Advance(1d/fps,source);
                }
                if(match.State.Phase==TeamMatchPhase.BetweenBouts) match.StartNextBout();
            }
            Assert.Equal(TeamMatchPhase.MatchFinished,match.State.Phase);
        }
        return entries;
    }
    [Theory]
    [InlineData(false,0,7)] [InlineData(false,30,1)] [InlineData(false,60,1)] [InlineData(false,144,1)]
    [InlineData(true,0,7)] [InlineData(true,30,1)] [InlineData(true,60,1)] [InlineData(true,144,1)]
    public void LiveMatchDecisionsAndEveryCommittedMatchStateAgree(bool team,double fps,int batch)
    {
        var expected=Run(team,0,1); var actual=Run(team,fps,batch);
        Assert.Equal(expected.Count,actual.Count);
        for(int i=0;i<actual.Count;i++) {
            var a=expected[i];var b=actual[i];
            Assert.Equal(a.Scope,b.Scope);Assert.Equal(a.Input,b.Input);Assert.Equal(a.Match,b.Match);
            Assert.Equal(a.State.Frame,b.State.Frame);Assert.Equal(a.State.Fighters,b.State.Fighters);
            Assert.Equal(a.State.Projectiles,b.State.Projectiles);Assert.Equal(a.State.PendingThrow,b.State.PendingThrow);
            Assert.Equal(a.State.NextProjectileId,b.State.NextProjectileId);Assert.Equal(a.State.RoundTimerRemainingFrames,b.State.RoundTimerRemainingFrames);
            Assert.Equal(a.State.Round,b.State.Round);Assert.Equal(a.Events,b.Events);
            Assert.True(a.P1.ContentEquals(b.P1));Assert.True(a.P2.ContentEquals(b.P2));
        }
    }
}
