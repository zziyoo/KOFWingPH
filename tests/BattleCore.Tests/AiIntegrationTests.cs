using NewKOF.BattleCore.AI;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Data;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Match;
using NewKOF.BattleCore.Team;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;
namespace NewKOF.BattleCore.Tests;

public class AiIntegrationTests
{
    private static readonly FighterCatalog Catalog = FighterDataLoader.LoadCatalog(new[]{"Kai","Rei"}.Select(name=>
        new FighterDataDocument(name,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"data","fighters",name+".json")))));
    private sealed record Tick(FrameInput Input,FighterState P1,FighterState P2,RoundState Round,
        BattleEvent[] Events,AiPolicyState A,AiPolicyState B);
    private static (List<Tick> Trace,BattleState Final) Run(double fps,int batch=1,BattleState? snapshot=null,
        AiPolicyState? a=null,AiPolicyState? b=null,int end=10000)
    {
        var roster=Catalog.CreateRoster("Kai","Rei"); var trace=new List<Tick>(); AiInputSource source=null!;
        var initial=snapshot ?? new BattleState(0,new[]{FighterState.Create(0,1000000,-120,Facing.Right),FighterState.Create(1,1000000,120,Facing.Left)},15);
        BattleSession session=null!;
        session=new BattleSession(initial,simulation:new BattleSimulation(roster),eventsCommitted:(input,before,events)=> {
            trace.Add(new(input,session.CurrentState.Fighter(0),session.CurrentState.Fighter(1),session.CurrentState.Round,
                events.ToArray(),source.PolicyState(0)!,source.PolicyState(1)!));
        });
        source=new AiInputSource(()=>session.CurrentState,roster,new InputBuffer(),
            new AiController(AiDifficultyProfile.For(AiDifficulty.Hard),AiTactic.Offensive),
            new AiController(AiDifficultyProfile.For(AiDifficulty.Expert),AiTactic.Balanced),42,state1:a,state2:b);
        int budget=0;
        while(session.CurrentState.Frame<end && budget++<100000) {
            int remaining=end-session.CurrentState.Frame;
            if(fps==0) session.StepExact(Math.Min(batch,remaining),inputSource:source);
            else session.Advance(Math.Min(1d/fps,remaining*FixedStepScheduler.FixedStepSeconds),inputSource:source);
        }
        Assert.Equal(end,session.CurrentState.Frame); return (trace,session.CurrentState);
    }
    private static void Equal(Tick a,Tick b) {
        Assert.Equal(a.Input,b.Input); Assert.Equal(a.P1,b.P1); Assert.Equal(a.P2,b.P2); Assert.Equal(a.Round,b.Round);
        Assert.Equal(a.Events,b.Events); Assert.True(a.A.ContentEquals(b.A)); Assert.True(a.B.ContentEquals(b.B));
    }
    [Fact] public void OffensiveMirrorDoesNotLoopFullHealthProjectileClashes() {
        var roster=Catalog.CreateRoster("Kai","Rei");
        var session=new BattleSession(simulation:new BattleSimulation(roster));
        var source=new AiInputSource(()=>session.CurrentState,roster,new InputBuffer(),
            new AiController(AiDifficultyProfile.For(AiDifficulty.Expert),AiTactic.Offensive),
            new AiController(AiDifficultyProfile.For(AiDifficulty.Expert),AiTactic.Offensive),AiRandom.DeriveSeed(12345,0,1,0));
        session.StepExact(900,inputSource:source);
        Assert.True(session.CurrentState.Fighter(0).Health<1000 || session.CurrentState.Fighter(1).Health<1000);
    }
    [Fact] public void SeededTeamsUseStableAvailableSlotsAndAllowDuplicates() {
        var a=AiTeamBuilder.Create("P1",new[]{"Kai","Rei"},42);
        var b=AiTeamBuilder.Create("P1",new[]{"Rei","Kai"},42);
        Assert.Equal(a,b);
        var repeated=AiTeamBuilder.Create("P2",new[]{"Kai"},1);
        Assert.Equal(3,repeated.Count);
        Assert.All(repeated.Members,m=>Assert.Equal("Kai",m.FighterId));
        Assert.Throws<ArgumentException>(()=>AiTeamBuilder.Create("P",Array.Empty<string>(),0));
    }
    [Fact] public void SnapshotCannotBeRestoredIntoOtherSeatOrRound() {
        var state=BattleState.CreateDefault();var roster=Catalog.CreateRoster("Kai","Kai");
        var source=new AiInputSource(()=>state,roster,new InputBuffer(),new AiController(AiDifficultyProfile.For(AiDifficulty.Normal)));
        Assert.Throws<ArgumentException>(()=>new AiInputSource(()=>state,roster,new InputBuffer(),player2:new AiController(AiDifficultyProfile.For(AiDifficulty.Normal)),state2:source.PolicyState(0)));
        Assert.Throws<ArgumentException>(()=>new AiInputSource(()=>state,roster,new InputBuffer(),new AiController(AiDifficultyProfile.For(AiDifficulty.Normal)),state1:source.PolicyState(0),scope:2));
    }
    [Theory] [InlineData(0,1)] [InlineData(0,7)] [InlineData(30,1)] [InlineData(60,1)] [InlineData(144,1)]
    public void LiveDecisions_AllExecutionPathsAgreeFor10000ActiveFrames(double fps,int batch) {
        var expected=Run(0); var actual=Run(fps,batch);
        Assert.Equal(10000,actual.Trace.Count);
        for(int i=0;i<actual.Trace.Count;i++) Equal(expected.Trace[i],actual.Trace[i]);
        Assert.Contains(actual.Trace,t=>t.Events.Any(e=>e.Type==BattleEventType.Hit || e.Type==BattleEventType.ProjectileHit || e.Type==BattleEventType.ThrowHit));
        Assert.True(actual.Final.Fighter(0).Health<1000000 || actual.Final.Fighter(1).Health<1000000);
    }
    [Fact] public void StrategySnapshotAndInputOnlyReplayProduceSameCommittedResults() {
        var first=Run(0,end:200); var rest=Run(0,snapshot:first.Final,a:first.Trace[^1].A,b:first.Trace[^1].B,end:500);
        var reference=Run(0,end:500);
        for(int i=0;i<rest.Trace.Count;i++) Equal(reference.Trace[i+200],rest.Trace[i]);
        var roster=Catalog.CreateRoster("Kai","Rei"); var replay=new BattleSimulation(roster);
        var state=new BattleState(0,new[]{FighterState.Create(0,1000000,-120,Facing.Right),FighterState.Create(1,1000000,120,Facing.Left)},15);
        foreach(var tick in reference.Trace) {
            var result=replay.Step(state,tick.Input); state=result.State;
            Assert.Equal(tick.P1,state.Fighter(0)); Assert.Equal(tick.P2,state.Fighter(1)); Assert.Equal(tick.Events,result.Events);
        }
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void RealAiVersusAiCompletesWholeDuelOrTeamMatch(bool team) {
        var events=new List<BattleEvent>(); int frames=0;
        if(!team) {
            var match=new MatchSession(new MatchRules(roundTimeSeconds:15),(a,b)=>Catalog.CreateRoster(a ?? "Kai",b ?? "Rei"),
                framePresented:f=>events.AddRange(f.Events));
            match.SelectFighters("Kai","Rei");match.StartMatch();
            while(match.State.Phase!=MatchPhase.MatchFinished && frames<15000) {
                var source=new AiInputSource(()=>match.CurrentBattleState!,Catalog.CreateRoster("Kai","Rei"),match.RoundInputBuffer,
                    new AiController(AiDifficultyProfile.For(AiDifficulty.Hard)),new AiController(AiDifficultyProfile.For(AiDifficulty.Expert)),(uint)match.State.RoundNumber);
                while(match.State.IsRoundLive && frames++<15000) match.StepExact(1,source);
                if(match.State.Phase==MatchPhase.BetweenRounds) match.StartNextRound();
            }
            Assert.Equal(MatchPhase.MatchFinished,match.State.Phase);
        } else {
            TeamDefinition Team(string id)=>new(id,new[]{new TeamMemberDefinition("Kai",1000),new TeamMemberDefinition("Rei",1000),new TeamMemberDefinition("Kai",1000)});
            var match=new TeamMatchSession(new TeamMatchRules(15),Catalog.CreateRoster,framePresented:f=>events.AddRange(f.Events));
            match.SelectTeams(Team("P1"),Team("P2"));match.StartMatch();
            while(match.State.Phase!=TeamMatchPhase.MatchFinished && frames<15000) {
                var roster=Catalog.CreateRoster(match.State.P1!.ActiveFighterId,match.State.P2!.ActiveFighterId);
                var source=new AiInputSource(()=>match.CurrentBattleState!,roster,match.BoutInputBuffer,
                    new AiController(AiDifficultyProfile.For(AiDifficulty.Hard)),new AiController(AiDifficultyProfile.For(AiDifficulty.Expert)),(uint)match.State.BoutNumber);
                while(match.State.IsBoutLive && frames++<15000) match.StepExact(1,source);
                if(match.State.Phase==TeamMatchPhase.BetweenBouts) match.StartNextBout();
            }
            Assert.Equal(TeamMatchPhase.MatchFinished,match.State.Phase);
        }
        Assert.Contains(events,e=>e.Type==BattleEventType.RoundEnded);
        Assert.Contains(events,e=>e.Type is BattleEventType.Hit or BattleEventType.ProjectileHit or BattleEventType.ThrowHit);
    }
}
