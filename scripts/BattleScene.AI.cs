using Godot;
using NewKOF.BattleCore.AI;
using NewKOF.BattleCore.Presentation;
using NewKOF.BattleCore.Combat;
using NewKOF.BattleCore.Input;
using NewKOF.BattleCore.State;
using NewKOF.BattleCore.Team;
namespace NewKOF;

public partial class BattleScene
{
    private bool _aiP1, _aiP2, _aiAutoNext, _aiSeededTeams;
    private AiDifficulty _aiDifficulty = AiDifficulty.Normal;
    private AiTactic _aiTactic = AiTactic.Balanced;
    private uint _aiSeed = 12345;
    private AiInputSource? _aiSource;
    private void ConfigureAiFromArgs()
    {
        foreach(string arg in OS.GetCmdlineUserArgs()) {
            if(arg=="--p1=ai") _aiP1=true;
            if(arg=="--p2=ai") _aiP2=true;
            if(arg=="--ai-auto-next") _aiAutoNext=true;
            if(arg=="--ai-seeded-teams") _aiSeededTeams=true;
            if(arg.StartsWith("--ai-difficulty=") && Enum.TryParse<AiDifficulty>(arg[16..],true,out var difficulty) && Enum.IsDefined(difficulty)) _aiDifficulty=difficulty;
            if(arg.StartsWith("--ai-tactic=") && Enum.TryParse<AiTactic>(arg[12..],true,out var tactic) && Enum.IsDefined(tactic)) _aiTactic=tactic;
            if(arg.StartsWith("--ai-seed=") && uint.TryParse(arg[10..],out var seed)) _aiSeed=seed;
        }
    }
    private bool HandleAiSelectionKey(Key key)
    {
        switch(key) {
            case Key.F6: _aiP1=!_aiP1; break;
            case Key.F7: _aiP2=!_aiP2; break;
            case Key.F8: _aiDifficulty=(AiDifficulty)(((int)_aiDifficulty+1)%4); break;
            case Key.F10: _aiSeededTeams=!_aiSeededTeams; break;
            case Key.F9: _aiTactic=(AiTactic)(((int)_aiTactic+1)%3); break;
            default:return false;
        }
        if(_stage==Stage.TeamSelect && key is Key.F6 or Key.F7 or Key.F10) _teamSelect.ClearReady();
        SyncViewFromState(); return true;
    }
    private void BindAiInput(InputBuffer human, Func<BattleState> state, FightRoster roster, int scope, int slot, string phase)
    {
        _aiSource=null;
        if(!_aiP1 && !_aiP2) return;
        var profile=AiDifficultyProfile.For(_aiDifficulty);
        _aiSource=new AiInputSource(state,roster,human,
            _aiP1 ? new AiController(profile,_aiTactic) : null,
            _aiP2 ? new AiController(profile,_aiTactic) : null,
            AiRandom.DeriveSeed(_aiSeed,0,scope,slot),phase:phase,scope:scope);
        _tracedSource=_diagnostics ? new TracingInputSource(_aiSource,_trace) : _aiSource;
    }
    private string AiTeamPreview(int player)
    {
        bool enabled=player==0 ? _aiP1 : _aiP2;
        if(!_aiSeededTeams || !enabled) return _teamSelect.DescribeLine(player);
        uint seed=player==0 ? _aiSeed : _aiSeed ^ 0xA511E9B3u;
        return AiTeamBuilder.Create(player==0 ? "P1" : "P2",_catalog.Entries.Select(e=>e.FighterId),seed).ToString();
    }
    private string AiHud()
    {
        string Seat(int index,bool enabled) {
            if(!enabled) return $"P{index+1}: Human";
            var state=_aiSource?.PolicyState(index);
            return $"P{index+1}: AI {_aiDifficulty}/{_aiTactic} {state?.FighterId} {state?.Action} {state?.TargetMove}";
        }
        return Seat(0,_aiP1)+"\n"+Seat(1,_aiP2)+"\n"+(_aiSeededTeams ? "AI teams: seeded (manual fighter edit overrides)\n" : "");
    }
    private void AdvanceAiBoutIfRequested()
    {
        if(!_aiAutoNext || _paused) return;
        if(_stage==Stage.DuelBattle && _match.State.Phase==BattleCore.Match.MatchPhase.BetweenRounds) {
            _match.StartNextRound(); BindDuelRound();
        }
        if(_stage==Stage.TeamBattle && _team.State.Phase==TeamMatchPhase.BetweenBouts) {
            _team.StartNextBout(); BindTeamBout();
        }
    }
}
