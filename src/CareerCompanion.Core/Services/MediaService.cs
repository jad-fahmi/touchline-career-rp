using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;

namespace CareerCompanion.Core.Services;

public sealed record MediaGenerationResult(int NewsItems,int SocialPosts);

public sealed class MediaService(Database db)
{
    private static readonly (string Outlet,string Style)[] Outlets=[("The Football Desk","serious broadcaster"),("The Daily Strike","tabloid"),("Terrace Review","club-focused")];
    public MediaGenerationResult GenerateDeterministic(long careerId,IEnumerable<CareerEvent> events,bool generateNews=true,bool generateSocial=true)
    {
        var news=0;var social=0;
        foreach(var e in generateNews?events.Where(x=>x.Importance>=48).Take(2):[])
        {
            var outlet=Outlets[(int)(Math.Abs(e.Id)%Outlets.Length)];
            var headline=outlet.Style=="tabloid"?$"DRAMA: {e.Summary.TrimEnd('.')}":e.Type.Replace('_',' ').ToLowerInvariant() switch { var s => char.ToUpper(s[0])+s[1..] };
            db.AddNews(careerId,e.Id,outlet.Outlet,headline,$"{e.Summary} This report is generated from the recorded Career Mode result.",e.Type.Contains("LOST")||e.Type.Contains("RED")?"negative":"positive",e.Importance,e.Timestamp);news++;
        }
        foreach(var e in generateSocial?events.Where(x=>x.Importance>=55).Take(2):[])
        {
            db.AddSocial(careerId,e.Id,"@MatchdayWire","football account",$"{e.Summary} Big moment in this career save.",e.Timestamp);social++;
            if(e.Importance>=75){db.AddSocial(careerId,e.Id,"North Stand Voice","supporter",e.Type.Contains("LOST")?"That one hurts. The response matters now.":"What a night. That will be remembered.",e.Timestamp);social++;}
        }
        return new(news,social);
    }
}
