using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;

namespace CareerCompanion.Core.Services;

public sealed class DemoSeeder(Database db)
{
    public long EnsureDemo()
    {
        var existing=db.GetCareers();if(existing.Count>0)return existing[0].Id;
        var id=db.CreateCareer("Touchline Demo (Fictional)","Alex Morgan","English",19,"Northbridge FC","Premier Division","2017/18","CAM",18);
        db.AddCharacter(id,"Jamie Cole",24,"English","Northbridge FC","ST","Key Player",CharacterType.Teammate,new(72,82,58,62,38,45,75,48,40,60,66,74),new("brief",72,38,55,60,25,18,24));
        db.AddCharacter(id,"Mateo Silva",28,"Spanish","Northbridge FC","CM","Captain",CharacterType.Teammate,new(68,78,35,45,28,75,66,88,70,72,80,88),new("brief",58,10,25,38,58,22,8));
        db.AddCharacter(id,"Martin Hale",51,"English","Northbridge FC","Manager","Manager",CharacterType.Manager,new(74,86,20,25,35,70,78,82,42,68,55,92),new("brief",80,5,12,30,70,35,18));
        var service=new CareerService(db);var result=service.ProcessMatch(id,new(DateTime.Today.AddDays(-3).ToString("yyyy-MM-dd"),"Premier Division","Riverside Athletic",true,3,1,true,90,2,1,9.2,false,false,false,false,"Late second goal sealed the win","Easton City",false,true));
        new MediaService(db).GenerateDeterministic(id,result.Events);
        foreach(var c in db.GetCharacters(id)){db.AddMemory(id,c.Id,result.Events.First().Id,$"Shared a memorable 3-1 win; {db.GetCareer(id).PlayerName} scored twice.",60,55,"Riverside Athletic match");}
        db.Log("seed","Created explicitly fictional demonstration career");return id;
    }
}
