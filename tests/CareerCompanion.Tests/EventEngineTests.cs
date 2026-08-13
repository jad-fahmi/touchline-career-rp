using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Simulation;

namespace CareerCompanion.Tests;

public sealed class EventEngineTests
{
    private static MatchInput Match(int us,int them,int goals=0,bool red=false,bool derby=false,bool major=false)
        => new("2017-10-01","League","Opposition",true,us,them,true,90,goals,0,7.0,false,red,false,false,"",null,derby,major);
    private static IReadOnlyList<CareerMatch> Prior(params string[] results)=>results.Select((x,i)=>new CareerMatch(i+1,1,Match(x=="W"?2:0,x=="L"?2:0),x,DateTime.UtcNow)).ToList();

    [Theory][InlineData(2,1,"MATCH_WON")][InlineData(0,2,"MATCH_LOST")][InlineData(1,1,"MATCH_DRAWN")]
    public void Classifies_result(int us,int them,string expected)=>Assert.Contains(new EventEngine().Detect(1,1,Match(us,them),[]),x=>x.Type==expected);
    [Fact] public void Detects_hat_trick_and_red_card(){var events=new EventEngine().Detect(1,1,Match(3,2,3,true),[]);Assert.Contains(events,x=>x.Type=="PLAYER_HATTRICK");Assert.Contains(events,x=>x.Type=="PLAYER_RED_CARD");}
    [Fact] public void Detects_winning_streak(){var events=new EventEngine().Detect(1,4,Match(1,0),Prior("W","W"));Assert.Contains(events,x=>x.Type=="WINNING_STREAK");}
    [Fact] public void Detects_losing_streak(){var events=new EventEngine().Detect(1,4,Match(0,1),Prior("L","L"));Assert.Contains(events,x=>x.Type=="LOSING_STREAK");}
    [Fact] public void Major_derby_is_more_important(){var routine=new EventEngine().Detect(1,1,Match(1,0),[]).First(x=>x.Type=="MATCH_WON");var major=new EventEngine().Detect(1,2,Match(1,0,derby:true,major:true),[]).First(x=>x.Type=="MATCH_WON");Assert.True(major.Importance>routine.Importance);}
}
