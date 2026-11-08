using NewKOF.BattleCore.AI;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Commands;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.Simulation;
using NewKOF.BattleCore.State;
using Xunit;
namespace NewKOF.BattleCore.Tests;

public class AiInputSourceTests
{
    private sealed class Policy : IAiPolicy {
        public AiDecision Decide(AiObservation o,AiPolicyState s) => new(InputBits.LightPunch,
            s.Observe(o) with { NextFrame=o.Frame+1, RandomState=s.RandomState+1 });
    }
    private static FightRoster Roster() {
        FighterDefinition Fighter(string id) => new(id,new[]{new CommandDefinition("A",Array.Empty<CommandDirection>(),InputBits.LightPunch)},
            new[]{new MoveDefinition("Jab","A",1,1,1)});
        return new(Fighter("Kai"),Fighter("Rei"));
    }
    [Theory] [InlineData(0)] [InlineData(1)]
    public void PrepareIsPureCommitOwnsBothSeats(int ai) {
        var state=BattleState.CreateDefault(); var roster=Roster(); var human=new InputBuffer();
        human.Sample(InputBits.Right,InputBits.Left);
        var source=new AiInputSource(()=>state,roster,human,ai==0?new Policy():null,ai==1?new Policy():null);
        var before=source.PolicyState(ai); var first=source.Prepare(0);
        Assert.Same(before,source.PolicyState(ai)); Assert.Empty(human.History);
        Assert.Equal(first,source.Prepare(0)); Assert.Equal(InputBits.LightPunch,first.ForPlayer(ai));
        Assert.Equal(ai==0?InputBits.Left:InputBits.Right,first.ForPlayer(1-ai));
        source.Commit(first); Assert.Equal(1,source.PolicyState(ai)!.NextFrame); Assert.Single(human.History);
        Assert.Throws<InvalidOperationException>(()=>source.Commit(first));
        Assert.Equal(0,state.Frame); Assert.Equal(1000,state.Fighter(ai).Health);
    }
    [Fact] public void BadCommitOrChangedHumanCannotPartiallyCommitAI() {
        var state=BattleState.CreateDefault(); var human=new InputBuffer();
        var source=new AiInputSource(()=>state,Roster(),human,new Policy(),new Policy());
        var prepared=source.Prepare(0);
        Assert.Throws<InvalidOperationException>(()=>source.Commit(prepared with { Player1=InputBits.HeavyPunch }));
        human.Sample(InputBits.Left,InputBits.None);
        Assert.Throws<InvalidOperationException>(()=>source.Commit(prepared));
        Assert.Empty(human.History); Assert.Equal(0,source.PolicyState(0)!.NextFrame); Assert.Equal(0,source.PolicyState(1)!.NextFrame);
    }
    [Fact] public void SimulationFailureRetainsSameCandidateAndSeed() {
        int frame=BattleEvent.MaxFrameForEventIds+1;
        var state=new BattleState(frame,BattleState.CreateDefault().Fighters,15); var roster=Roster();
        var human=new InputBuffer(); var source=new AiInputSource(()=>state,roster,human,new Policy());
        var session=new BattleSession(state,simulation:new BattleSimulation(roster));
        var candidate=source.Prepare(frame); var before=source.PolicyState(0);
        Assert.Throws<InvalidOperationException>(()=>session.StepExact(1,inputSource:source));
        Assert.Equal(candidate,source.Prepare(frame)); Assert.Same(before,source.PolicyState(0)); Assert.Empty(human.History);
        Assert.Same(state,session.CurrentState);
    }
    [Fact] public void RestoredStrategyContinuesSameInputsAndRejectsWrongFighter() {
        var roster=Roster(); var state=BattleState.CreateDefault(); var first=new AiInputSource(()=>state,roster,new InputBuffer(),new Policy(),new Policy(),42);
        var sim=new BattleSimulation(roster);
        for(int i=0;i<8;i++){var input=first.Prepare(state.Frame);var next=sim.Step(state,input).State;first.Commit(input);state=next;}
        var restored=new AiInputSource(()=>state,roster,new InputBuffer(),new Policy(),new Policy(),state1:first.PolicyState(0),state2:first.PolicyState(1));
        Assert.Equal(first.Prepare(state.Frame),restored.Prepare(state.Frame));
        Assert.Throws<ArgumentException>(()=>new AiInputSource(()=>state,roster,new InputBuffer(),new Policy(),state1:new AiPolicyState(0,1,"Missing")));
    }
    [Fact] public void PauseAndNoBudgetDoNotPrepareOrAdvancePolicies() {
        var session=new BattleSession(); var source=new AiInputSource(()=>session.CurrentState,Roster(),new InputBuffer(),new Policy());
        Assert.Equal(0,session.Advance(0,inputSource:source)); Assert.Equal(0,source.PolicyState(0)!.NextFrame); Assert.Empty(source.History);
    }
}
