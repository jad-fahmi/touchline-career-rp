using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Simulation;

namespace CareerCompanion.Tests;

public sealed class SimulationTests
{
    [Fact] public void Relationships_are_bounded_and_proposals_are_clamped(){var x=new RelationshipEngine().Apply(new(1,99,99,-99),50,20,-20);Assert.Equal(100,x.Score);Assert.Equal(100,x.Trust);Assert.Equal(-100,x.Respect);}
    [Fact] public void Relationship_changes_accumulate_gradually(){var engine=new RelationshipEngine();var x=new Relationship(1);for(var i=0;i<3;i++)x=engine.Apply(x,2,1,1);Assert.Equal(6,x.Score);Assert.Equal(3,x.Familiarity);}
    [Fact] public void Memory_ranking_prefers_topic_and_importance(){var now=DateTime.UtcNow;var memories=new[]{new Memory(1,1,1,null,"Routine training",now,10,0,"training",false,null),new Memory(2,1,1,null,"Won the Arsenal final together",now.AddDays(-20),90,70,"Arsenal final",false,null),new Memory(3,1,1,null,"Ate lunch",now,15,5,"casual",false,null)};var result=new MemoryRanker().Rank(memories,"Arsenal match",now,2);Assert.Equal(2,result[0].Id);Assert.Equal(2,result.Count);}
    [Fact] public void Reaction_engine_allows_silence_for_minor_event(){var e=new CareerEvent(1,1,1,"PLAYER_YELLOW_CARD",DateTime.UtcNow,16,"[]","{}","Booked");Assert.Empty(new ReactionEngine().Select(e,[],new Dictionary<long,Relationship>()));}
}
