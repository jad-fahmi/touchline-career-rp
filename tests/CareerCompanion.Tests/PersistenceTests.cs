using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;

namespace CareerCompanion.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _dir=Path.Combine(Path.GetTempPath(),"touchline-tests-"+Guid.NewGuid().ToString("N"));private string DbPath=>Path.Combine(_dir,"world.db");
    private Database NewDb(){var db=new Database(DbPath);db.Migrate();return db;}
    [Fact] public void Career_survives_restart(){var db=NewDb();var id=db.CreateCareer("Save","Player","English",18,"Club","League","2017/18","CM",8);var reopened=NewDb();Assert.Equal("Player",reopened.GetCareer(id).PlayerName);}
    [Fact] public void Conversations_survive_restart(){var db=NewDb();var career=db.CreateCareer("Save","Player","",18,"Club","","2017/18","CM",8);var ch=db.AddCharacter(career,"Mate",22,"","Club","ST","Starter",CharacterType.Teammate);var conversation=db.StartConversation(career,ch,SceneType.PrivateMessage);db.AddMessage(conversation,"user","Good match");db.AddMessage(conversation,"assistant","Cheers.");Assert.Equal(2,NewDb().GetMessages(career,ch).Count);}
    [Fact] public void Match_event_and_backup_are_consistent(){var db=NewDb();var career=db.CreateCareer("Save","Player","",18,"Club","","2017/18","CM",8);var match=db.SaveMatch(career,new("2017-01-01","League","Other",true,2,0,true,90,1,0,8,false,false,false,false,""));db.SaveEvent(new(0,career,match,"MATCH_WON",DateTime.UtcNow,30,"[]","{}","Won"));var backup=Path.Combine(_dir,"backup.db");db.Backup(backup);var restored=new Database(backup);Assert.Single(restored.GetMatches(career));Assert.Single(restored.GetEvents(career));}
    public void Dispose(){try{Directory.Delete(_dir,true);}catch{}}
}
