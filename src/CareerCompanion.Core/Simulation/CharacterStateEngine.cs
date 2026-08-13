using CareerCompanion.Core.Domain;

namespace CareerCompanion.Core.Simulation;

public sealed class CharacterStateEngine
{
    public CharacterState AfterMatch(Character character,CharacterState current,CareerMatch match,CareerEvent top)
    {
        var won=match.Result=="W";var lost=match.Result=="L";var delta=won?6:lost?-7:0;if(character.Type==CharacterType.Manager)delta=won?4:lost?-9:0;if(top.Type=="PLAYER_RED_CARD")delta-=character.Type==CharacterType.Manager?12:5;if(top.Type=="PLAYER_HATTRICK")delta+=8;
        var satisfaction=Math.Clamp(current.Satisfaction+delta,0,100);var mood=satisfaction switch{>=75=>won?"energized":"confident",>=58=>"positive",<=25=>"frustrated",<=42=>lost?"concerned":"reserved",_=>"neutral"};var concern=top.Type switch{"PLAYER_RED_CARD"=>"discipline","LARGE_DEFEAT"=>"the team's response","LOSING_STREAK"=>"poor form",_=>satisfaction<40?"recent results":""};var ambition=character.Type switch{CharacterType.Manager=>"build consistent results",CharacterType.Teammate when character.SquadRole.Contains("Key",StringComparison.OrdinalIgnoreCase)=>"compete for major honours",_=>current.Ambitions};return new(character.Id,mood,concern,ambition,satisfaction,top.Type,DateTime.TryParse(match.Input.Date,out var date)?date:DateTime.UtcNow);
    }
}
