using CareerCompanion.Core.Domain;

namespace CareerCompanion.Core.Services;

/// <summary>
/// Deterministic dialogue for offline play. It is deliberately factual and bounded:
/// it reacts to known career data without inventing results, injuries, or transfers.
/// </summary>
public static class OfflineDialogueLibrary
{
    /// <summary>
    /// Returns true when a player message needs a model response, which is nearly always. The only thing
    /// the offline library answers by choice is a bare greeting ("hey", "morning mate"): there is one
    /// natural reply to that and it costs nothing. The moment a greeting carries anything else, or the
    /// message is not a greeting at all, the player is saying something real and deserves a real answer.
    /// Everything else in this library exists for when the model cannot be reached.
    /// </summary>
    public static bool RequiresAi(string message)=>!IsBareGreeting(message);

    /// <summary>
    /// True for a greeting on its own. A trailing name or "mate" is still a bare greeting; a second
    /// clause, a question, or any word the greeting does not account for is not.
    /// </summary>
    public static bool IsBareGreeting(string message)
    {
        var text=message.Trim();
        if(text.Length==0)return true;
        if(text.Contains('?'))return false;
        // Two sentences means the greeting was only the opening of a real message.
        if(text.TrimEnd('.','!',' ').IndexOfAny(['.','!','\n'])>=0)return false;
        var words=text.ToLowerInvariant().Split([' ','\t',',','.','!','-'],StringSplitOptions.RemoveEmptyEntries);
        if(words.Length==0||words.Length>3)return false;
        if(!GreetingWords.Contains(words[0]))return false;
        // "hey Marco" is still just a greeting, so a single trailing word is allowed whatever it is.
        // A third word has to belong to the greeting itself: "good morning mate" stays offline,
        // "hey boss, quick one" does not.
        if(words.Length<=2)return words.All(word=>word.All(char.IsLetter));
        return words.Skip(1).All(word=>GreetingWords.Contains(word)||GreetingCompanions.Contains(word));
    }

    private static readonly HashSet<string> GreetingWords=new(StringComparer.OrdinalIgnoreCase)
        {"hi","hey","hello","yo","morning","afternoon","evening","good","hiya","alright","aight","ey","oi","sup","heya","hola","ciao","salam"};
    private static readonly HashSet<string> GreetingCompanions=new(StringComparer.OrdinalIgnoreCase)
        {"mate","boss","gaffer","coach","man","bro","brother","lad","there","again","you","all","everyone","pal","bud","buddy","sir","skip","skipper"};

    public static GenerationResult Direct(Character character,Career career,Relationship relationship,CharacterState state,PlayerState player,string message,SceneType scene)
    {
        var text=message.Trim();var lower=text.ToLowerInvariant();var p=character.Profile.Personality;var c=character.Profile.Communication;var seed=Seed(character.Id.ToString(),career.CurrentDate,text,scene.ToString());
        var intent=Intent(lower);var lines=BuildDirectLines(character,career,relationship,state,player,intent,scene,p,c);var dialogue=ComposedDirect(character,intent,lines,scene,seed);var mood=intent switch
        {
            "loss" or "distress"=>"concerned",
            "conflict"=>"guarded",
            "positive"=>"pleased",
            "selection"=>"focused",
            "injury"=>"concerned",
            "transfer" or "international"=>"encouraging",
            _=>state.Mood is "" or "neutral"?"steady":state.Mood
        };
        var relationshipDelta=intent switch{"positive"=>1,"loss" or "distress"=>p.Openness>=55?1:0,"conflict"=>p.Diplomacy>=65?0:-1,"selection"=>p.Loyalty>=60?1:0,_=>0};
        var trustDelta=intent is "loss" or "distress"?1:intent=="conflict"?-1:0;var respectDelta=intent is "selection" or "training"&&p.Professionalism>=65?1:0;
        var memory=intent switch{"loss" or "distress"=>$"Discussed the difficult moment: {Trim(text,100)}","selection"=>$"Discussed selection and playing time: {Trim(text,100)}","transfer"=>$"Discussed the career move: {Trim(text,100)}","international"=>$"Discussed international duty: {Trim(text,100)}",_=>null};
        return new(dialogue,mood,relationshipDelta,trustDelta,respectDelta,memory is null?Array.Empty<string>():new[]{memory},0,0,"offline-library");
    }

    // Twelve choices in four slots plus several intent-specific core lines produce
    // over 100,000 deterministic combinations before role, scene, and personality variations.
    private static string ComposedDirect(Character character,string intent,IReadOnlyList<string> cores,SceneType scene,int seed)
    {
        if(intent is "greeting" or "ping"||cores.Count==0)return Pick(cores,seed);
        var openers=new[]{"I have been thinking about what you said.","I hear you.","Thank you for being direct with me.","Let us take this one piece at a time.","I understand why that is on your mind.","You do not need to dress this up for me.","That is worth talking about properly.","I am glad you brought it to me.","I can see why this matters to you.","Before we jump to an answer, take a breath.","I know there is more behind those words.","All right. Let us deal with the real issue."};
        var bridges=new[]{"The important thing is that we stay honest about where we are.","That does not have to become a bigger story than it already is.","One conversation will not solve everything, but it can change the next step.","You still have choices here, even if the situation feels narrow.","The group will respond to the way we handle this now.","There is no value in pretending the feeling is not real.","We can protect the football without ignoring the person playing it.","The next decision should come from clarity, not noise.","I would rather hear the difficult version than a polished one.","What happens next will matter more than the first emotion.","You have earned the right to be heard before anyone gives advice.","We can be ambitious and still be patient with the process."};
        var actions=character.Type==CharacterType.Manager?new[]{"Bring that focus to training tomorrow.","Come to me before the next session and we will make a practical plan.","Keep your work visible and let the football answer the questions.","Take the evening to reset, then arrive ready to contribute.","I will make sure the staff understand what you need.","Use the next match as a response, not as a referendum on your worth.","Stay close to the group and keep communicating with me.","Give me consistency and I will give you a fair assessment.","Let us review the details before we decide what changes.","Protect your recovery and trust the process around you.","You are responsible for the response, and you will not make it alone.","I expect honesty, effort, and patience from here."}:character.Type==CharacterType.Agent?new[]{"I will deal with the outside noise while you focus on the next step.","Call me later if you want to talk without an audience.","Keep your routine steady and let me handle the speculation.","Write down what you need before the next conversation with the club.","I can make the practical calls, but the feeling still deserves your attention.","Do not make a permanent decision in a temporary mood.","Give me the facts you know and I will help with the rest.","We will protect your options while you decide what you really want.","Stay professional, but do not pretend you are not affected.","The next opportunity will be easier to see after you have rested.","I will check in again tomorrow, and you can tell me what changed.","You are allowed to want more, as long as we plan the path there."}:new[]{"Come find me when you want company rather than another opinion.","Keep talking to the people who know you, not only the people who watch you.","Stay with the group for a while and do not disappear into your own head.","We can take this outside the changing room if that feels easier.","Tell me what would actually help tonight, not what sounds brave.","You do not have to solve the whole season before dinner.","Keep the next step small enough that you can really take it.","I will listen first and give advice only if you want it.","Let us find one good thing to hold on to before we analyse the rest.","You can be honest here without turning it into a headline.","Message me again if the feeling gets heavier instead of lighter.","Whatever happens next, you still have a place with us."};
        var closers=new[]{"We can continue this whenever you need.","I mean that.","You are not on your own with it.","Take the time you need before you answer again.","There is no rush to perform for me.","We will speak soon.","Keep me updated, even if the update is only that today was difficult.","For now, that is enough.","I am glad we had this conversation.","Let us leave the door open.","One day at a time is still progress.","We will find the next honest step."};
        var core=Pick(cores,seed+1);var opener=Pick(NaturalOpeners(character),seed+2);var bridge=Pick(NaturalBridges(intent),seed+3);var action=Pick(NaturalActions(character),seed+4);var closer=Pick(NaturalClosers,seed+5);var parts=new[]{opener,core,bridge,action,closer};return string.Join(" ",parts.Take(2+Math.Abs(seed%3)));
    }

    private static IReadOnlyList<string> NaturalOpeners(Character character)=>character.Type switch
    {
        CharacterType.Manager=>new[]{"Alright, talk to me.","Okay. What is actually going on?","Right, give me the honest version.","You alright?"},
        CharacterType.Agent=>new[]{"Yeah, I am here.","Okay, what happened?","Talk to me.","I had a feeling you would call."},
        _=>new[]{"Yeah?","Okay, go on.","I hear you.","What is up?"}
    };
    private static IReadOnlyList<string> NaturalBridges(string intent)=>intent switch
    {
        "loss" or "distress"=>new[]{"That sounds rough.","I get why it is stuck in your head.","You do not have to put a brave face on it with me.","One bad night does not explain everything."},
        "selection"=>new[]{"I know that is frustrating.","It is not a great feeling, is it?","Do not let one team sheet get in your head.","You still have a say in what happens next."},
        "conflict"=>new[]{"I would rather clear it up than let it sit there.","We can disagree without making it weird.","Tell me the bit I am missing.","No point pretending it did not bother you."},
        _=>new[]{"That makes sense.","I get what you mean.","Let us keep it simple.","No need to make it into a speech."}
    };
    private static IReadOnlyList<string> NaturalActions(Character character)=>character.Type switch
    {
        CharacterType.Manager=>new[]{"Come by after training and we will talk it through.","Show me in the next session.","We will deal with the football side tomorrow.","Give me a ring if it is still bothering you."},
        CharacterType.Agent=>new[]{"I will make a couple of calls, but tell me what you actually want.","Leave the outside noise to me for now.","Sleep on it and we will speak tomorrow.","I can help with the practical bits, not the feeling itself."},
        _=>new[]{"Come find me later.","We can grab a coffee and talk properly.","Text me if your head is still going round in circles.","I am around, yeah?"}
    };
    private static readonly string[] NaturalClosers={"You good?","Text me later.","I am around.","Do not disappear, yeah?","We will talk."};

    public static string PreMatch(Character character,CareerFixture fixture,bool rival,string? keyThreat,int seed,string availability="Unknown",ISet<string>? spoken=null)
    {
        var p=character.Profile.Personality;var c=character.Profile.Communication;var opponent=fixture.Opponent;var venue=fixture.IsHome?"at home":$"away at {opponent}";
        if(availability is "Injured" or "Suspended" or "NotSelected" or "Unavailable")
        {
            if(character.Type==CharacterType.Manager)return Pick(availability=="Injured"
                ?new[]{$"You are not available for {opponent} while the injury is assessed. Focus on the recovery plan and stay close to the group.",$"The match against {opponent} will go on without you. No pressure to perform from the stands; get the medical work right."}
                :availability=="Suspended"?new[]{$"You are unavailable for {opponent} through suspension. Stay involved, learn from the match, and be ready when the ban is served.",$"You cannot play against {opponent}. Keep your focus on the team and use the time out to reset."}
                :new[]{$"You are not in the match selection for {opponent}. Keep training, support the group, and make the next decision harder.",$"You will not be on the pitch against {opponent}. I know that stings, but your response starts at the next session."},seed);
            return Pick(new[]{$"Looks like you are not with us on the pitch against {opponent}. We will keep you close to the group.",$"The {opponent} match is not yours to play today. Stay around the lads and look after yourself."},seed,spoken);
        }
        if(availability=="Benched")
        {
            if(character.Type==CharacterType.Manager)return Pick(new[]{$"You are listed among the substitutes for {opponent}. Stay ready, but do not treat an appearance as guaranteed.",$"You are on the bench against {opponent}. Watch the game, stay warm, and be ready if the match needs you."},seed,spoken);
            return Pick(new[]{$"You are on the bench for {opponent}. Keep your head in the game and be ready if your moment comes.",$"Not starting today, but the match can change quickly. Stay with the group and stay ready."},seed,spoken);
        }
        if(availability=="Unknown")
        {
            if(character.Type==CharacterType.Manager)return Pick(new[]{$"The next fixture is {opponent}, {venue}. I have not settled on the team yet, so prepare as if you are starting.",$"We are preparing for {opponent}. I will name the side closer to kick-off, so keep the work honest this week.",$"{opponent} next, {venue}. Selection is still open. Give me a reason to pick you."},seed,spoken);
            return Pick(new[]{$"The next one is {opponent}. Team has not gone up yet, so we are all guessing.",$"No idea if either of us is starting against {opponent}. Stay ready, yeah?",$"{opponent} next. I will find out the same time you do."},seed,spoken);
        }
        if(character.Type==CharacterType.Manager)return Pick(rival?new[]{$"This is {opponent}, {venue}, and they will make it emotional. Be disciplined before you try to win it.",$"Rivalry matches punish loose decisions. Start with our shape against {opponent}, then let your quality decide it.",$"The occasion will be loud against {opponent}. I need your head clear and your work rate high."}:new[]{$"Against {opponent}, our first job is to control the spaces and play with patience.",$"Prepare properly for {opponent}. The details in your role will matter more than the noise around the match.",$"We have a plan for {opponent}. Trust it, communicate, and be ready for the moments that change the game."},seed,spoken);
        if(rival)return Pick(p.Humor>=60?new[]{$"Big one against {opponent}. If the atmosphere gets wild, at least make sure we enjoy winning it.",$"They will be talking before kick-off. Let us give them something to talk about after it.",$"Rivalry day. Keep your head, win your battles, and do not give them an easy story."}:new[]{$"Big one against {opponent}. We need to set the tone early.",$"The atmosphere will be intense. Stay brave on the ball and stay together.",$"Matches like this are remembered. Let us make sure the memory belongs to us."},seed,spoken);
        return Pick(new[]{$"Ready for {opponent}? Let us start quickly and make the match ours.",$"We have a chance to set the rhythm against {opponent}. Stay switched on from the first whistle.",$"No need for a speech. Do your job, help the man beside you, and the match will open up.",$"I have been looking forward to this one. Bring your sharpness and I will bring mine."},seed,spoken);
    }

    public static string Support(Character person,int severity,string opponent,int seed,ISet<string>? spoken=null)
    {
        var p=person.Profile.Personality;var direct=person.Profile.Communication.Directness>=70;var high=severity>=22;
        if(person.Type==CharacterType.Agent)return Pick(high?new[]{$"I heard how badly the result against {opponent} hit you. Do not sit with this alone tonight.",$"I heard how badly the result against {opponent} hit you. Do not sit with this alone. Call me when you are somewhere quiet.",$"Do not sit with this alone. I am checking in because this is exactly when you need people around you. No performance, just talk to me."}:new[]{$"Forget the noise around the result for a moment. Call me when you are somewhere quiet.",$"Keep tonight simple: eat, rest, and let me know how your head feels tomorrow.",$"You have a career to manage, but you are also allowed to have a difficult evening. I am here."},seed,spoken);
        if(person.Type==CharacterType.Manager)return Pick(high?new[]{$"Football can wait tonight. Speak to someone you trust and check in with me tomorrow.",$"I know the result has landed heavily. Take the evening away from the noise, then we will make a plan.",$"You are still part of this group. Rest first, then we will decide what support you need."}:new[]{$"I know this result hurts. Take tonight, clear your head, and we will talk properly tomorrow.",$"Do not let one performance become a judgement on your whole season. We will review it calmly.",$"You can be disappointed and still be ready to respond. We will help you get there."},seed,spoken);
        return Pick(high?new[]{$"I know this one has hit you hard. You do not need to find the right words.",$"Come sit with us for a while. You do not have to talk about the match.",$"I am not going to tell you to cheer up. I am just here, and I am not going anywhere."}:new[]{$"That result is still hurting, I know. If you want company, call me.",$"Bad days happen. Stay near the lads tonight and do not disappear.",$"You do not have to pretend it was fine. We can talk when you are ready."},seed,spoken);
    }

    public static string TransferRequest(Character character,string status,int seed,ISet<string>? spoken=null)
    {
        if(character.Type==CharacterType.Manager)return status switch
        {
            "Accepted"=>Pick(new[]{"The request has been accepted. We will handle the next steps professionally, but I would still like to understand what brought you here.","The club has agreed to let you leave. Before you go, tell me honestly what was missing for you here."},seed,spoken),
            "Rejected"=>Pick(new[]{"The request was rejected. Come and speak to me properly. Why did you feel leaving was the answer?","I have not approved the request. I need to hear from you directly, not through headlines or an agent."},seed,spoken),
            _=>Pick(new[]{"I have seen the transfer request. Before we decide anything, tell me why you want to leave.","You handed in a request. I am disappointed, but I would rather hear the reason from you. Is this about minutes, the squad, or something else?","This changes the conversation around your place here. Come to my office and explain what has brought you to this point."},seed,spoken)
        };
        if(character.Type==CharacterType.Agent)return status switch
        {
            "Accepted"=>Pick(new[]{"The club has accepted the request. We will keep the next steps clean and make sure you understand every option.","It is moving now. I still want your honest reason for leaving so we choose the right next club, not just the quickest one."},seed,spoken),
            "Rejected"=>Pick(new[]{"They rejected the request. We need to decide whether you want to repair the relationship or keep pushing for a move.","The answer is no for now. Tell me what changed for you, and I will work out the next move from there."},seed,spoken),
            _=>Pick(new[]{"I saw the request go in. Good. Now tell me the real reason you want out so I can protect your next step.","The paperwork is only the beginning. Is this about minutes, the manager, the club direction, or something personal?","I will handle the noise around the request, but I need clarity from you. Why leave now?"},seed,spoken)
        };
        return status switch
        {
            "Accepted"=>Pick(new[]{"So it has been accepted. I am happy for you, but I want to know why you felt you had to leave us.","The room is talking about it. Are you okay with how quickly this is happening?"},seed,spoken),
            "Rejected"=>Pick(new[]{"I heard they rejected it. What made you want out in the first place?","That must feel awkward. Do you think you can still be happy here after asking to leave?"},seed,spoken),
            _=>Pick(new[]{"I saw you handed in a transfer request. Why? Is it something in the squad, or are you just ready for a change?","The lads are wondering what happened. You do not have to explain everything, but are you really trying to leave?","That is a big step. If you want to talk about it without the football language, I am here."},seed,spoken)
        };
    }

    public static string Transfer(Character agent,string from,string to,int seed,ISet<string>? spoken=null)=>Pick(new[]{$"The move from {from} to {to} is complete. Settle quickly, learn the environment, and make the first opportunity count.",$"A new club means a new test. Be patient with the adjustment from {from} to {to}, but do not hide from the competition.",$"The transfer is done. Your first target at {to} is simple: earn trust every day and let the football follow.",$"Forget the announcement now. The real work starts at {to}, and I will help you manage the noise around it."},seed,spoken);
    public static string Record(Character agent,string summary,int seed,ISet<string>? spoken=null)=>Pick(new[]{$"That is not just a good performance. You have put your name into the record book. Take the moment in, then protect the standard you have set.",$"Records are built from thousands of ordinary decisions before the special day. Enjoy this one, then keep the habits that created it.",$"Your name is now attached to a piece of football history. Let yourself feel that, but do not let the attention change your work.",$"That achievement will travel with you. The next challenge is proving it was the beginning of a level, not a single afternoon."},seed,spoken);
    public static string International(Character agent,string team,bool debut,int seed,ISet<string>? spoken=null)=>debut?Pick(new[]{$"A senior debut for {team} is a real career milestone. Take it in, then keep building on it.",$"Your first cap changes how people see your career. Enjoy the pride, then return to the daily work.",$"You have crossed an important line with {team}. Keep your feet on the ground and make the next selection inevitable."},seed,spoken):Pick(new[]{$"Another cap for {team}. International matches raise your profile, but your next club performance still matters.",$"You are becoming a regular part of {team}. Manage the travel and keep your club standards high.",$"Every appearance for {team} adds experience. Bring that confidence back to the club without losing your balance."},seed,spoken);
    public static string SquadArrival(Character player,int seed,ISet<string>? spoken=null)=>Pick(new[]{$"Good to meet you. I am settling in and looking forward to playing together.",$"Welcome in. I have heard good things, but I would rather see what you are like around the group.",$"New club, new faces. If you need anything while you settle, come find me.",$"Welcome. The first few weeks are always strange, so do not be afraid to ask where everything is.",$"Good to have you here. Let us make the adjustment easier for each other."},seed,spoken);
    public static string Statement(Character character,bool teamFirst,bool blame,bool referee,bool accountable,int seed,ISet<string>? spoken=null)
    {
        if(character.Type==CharacterType.Manager)return blame?Pick(new[]{"We discuss problems inside the dressing room, not through the press. Keep that in mind.","If there is an issue, bring it to me directly. Public blame will not improve the team.","You are entitled to your view, but the group needs responsibility more than accusations."},seed,spoken):referee?Pick(new[]{"Do not let comments about officials become a distraction. Focus on what we control.","The decision can be debated, but the next performance cannot be.","Leave the referee discussion there. Your energy is needed on the football."},seed,spoken):accountable?Pick(new[]{"Taking responsibility was the right response. Now show it in the next performance.","Words matter, but the best answer will come in training and the next match.","That was mature. Keep the ownership when the work becomes difficult."},seed,spoken):"The team will judge the statement by what happens next.";
        if(teamFirst)return Pick(new[]{"The squad appreciated you putting the team first in there. We stay together.","Good answer. Nobody wins alone, and the lads noticed you said it.","That is the kind of message that keeps a dressing room connected."},seed,spoken);
        if(blame||referee)return Pick(new[]{"Some of the lads noticed those comments. Better to settle it face to face in the dressing room.","The headlines will move on, but the people in the room will remember how it felt.","Come speak to us directly. It is easier to fix tension when nobody is performing for cameras."},seed,spoken);
        return accountable?Pick(new[]{"Fair answer. We can respect someone who owns their part.","That sounded honest. Now let us turn it into something useful.","The supporters will appreciate the honesty if the next performance follows it."},seed,spoken):"I saw the interview. We will see how the next match feels.";
    }

    private static IReadOnlyList<string> BuildDirectLines(Character character,Career career,Relationship relationship,CharacterState state,PlayerState player,string intent,SceneType scene,Personality p,CommunicationStyle c)
    {
        var name=career.PlayerName;var friendly=relationship.Friendliness+relationship.Familiarity>70;var tense=relationship.Tension+relationship.Rivalry>60;var manager=character.Type==CharacterType.Manager;var agent=character.Type==CharacterType.Agent;var privateScene=scene is SceneType.PrivateMessage or SceneType.ManagerOffice or SceneType.TransferDiscussion;
        return intent switch
        {
            "greeting"=>manager?new[]{"Morning. How is your head today?","Good to hear from you. Are you ready for the work ahead?","Morning. Give me an honest read on how you are feeling.","Hello. I have a few minutes before training if you want to talk.","Good morning. We can keep this brief or take the time it needs."}:agent?new[]{"Hello. I was going to check in with you anyway.","Good to hear from you. What do you need from me today?","I am here. Is this about football, or is it about everything around football?","Morning. I have been keeping an eye on how things are developing.","Hello. Talk to me, and start wherever makes sense."}:new[]{"Hey. How are you holding up?","Good to hear from you. What is going on?","I was about to message you. You all right?","Hey. Give me the version you would not say in the changing room.","Hello. I am around if you need a proper conversation."},
            "ping"=>PingLines(character,career,relationship,p,c),
            "loss" or "distress"=>manager?new[]{"I know the result has stayed with you. We will separate the emotion from the work and deal with both.","You do not need to apologise for caring. We need you recovered enough to think clearly.","I have seen players mistake a bad result for a bad identity. Do not make that mistake.","Take the evening seriously, but do not let it become a verdict on your career.","You are allowed to be upset. Tomorrow, we turn that feeling into something useful."}:agent?new[]{"Let us keep the noise away from you tonight. I can handle calls and speculation while you reset.","The result is painful, but it does not change the opportunities in front of you.","Tell me whether you want advice, silence, or someone to take the pressure off for a few hours.","I know the football matters deeply to you. That is exactly why we need to protect your head now.","We can review the match later. Tonight, you are more important than the headlines."}:new[]{"I am here. You do not have to make the feeling smaller for me.","Come find me after dinner. We can talk about the match, or absolutely nothing.","You can be disappointed without disappearing from everyone who cares about you.","That result is one night. I know it does not feel like one night right now, but it is.","No motivational speech from me. Just company, if you want it."},
            "positive"=>manager?new[]{"Good. Keep that confidence quiet enough to work and strong enough to show.","Enjoy the result, then protect it with the next training session.","You played with conviction. Now make that feeling repeatable.","The group felt your energy today. Use it well rather than chasing praise.","A good day. Recover properly and arrive hungry for the next one."}:new[]{"That was a good feeling, was it not?","You earned the smile. I am still replaying the best moment.","Good days make the hard ones worth it. Keep that energy close.","I knew you had that performance in you. I am glad everyone else saw it too.","Enjoy tonight. Tomorrow we can start talking about doing it again."},
            "selection"=>manager?new[]{"Selection is a conversation between your daily work and the needs of the match.","I have not closed any door. Give me a reason to choose you again.","If you want more minutes, show me more reasons to trust you with them.","I understand the frustration. Bring it to training, where it can help you.","You are part of the plan, but your next step is still yours to earn."}:new[]{"I know you want to start. Keep pushing, and do not let the disappointment isolate you.","You looked ready when you came on. That matters more than sulking about the bench.","Your chance will come. When it does, make it difficult for anyone to leave you out.","I would be frustrated too. Just do not let it turn into bad work.","The squad notices who stays ready. Keep showing that you are one of them."},
            "training"=>manager?new[]{"Training is where selection starts. Give me intensity and clarity today.","The details are improving. Keep the standard when nobody is applauding.","I want you to be brave with the ball and reliable without it.","One sharp session will not decide your career, but a month of them can change it.","Come early, prepare properly, and make the work visible."}:new[]{"Training was sharp today. You looked switched on from the first drill.","Stay after for ten minutes if you want to work on that movement.","The work nobody sees usually decides the moments everyone remembers.","I can see you are building something. Do not rush the result.","Good session. Eat, recover, and do not waste the work you put in."},
            "injury"=>agent?new[]{"I will deal with the practical side. You focus on the recovery plan and keep me updated.","Do not let impatience turn a short absence into a longer one.","The medical timeline is the fact. Everything else is noise until we know more.","We can manage the questions from outside. You only need to manage the next recovery step.","Your value has not changed because you are unavailable for a while."}:manager?new[]{"The medical team will guide the return. I will not let outside pressure rush you.","Stay involved with the group while you recover, but respect the work your body needs.","You are still part of the team every day, even when you cannot play.","We will miss you on the pitch. That is not a reason to take a shortcut back.","Focus on what you can control today, not the date you wish you could return."}:new[]{"How are you feeling after the check?","Come around if you are bored. I can bring terrible food and better company.","Do not rush it for us. We want you back properly.","You are still one of us, even if you are watching from the side for now.","Message me after rehab. I want to know how it really feels, not just what the report says."},
            "transfer"=>agent?new[]{"A move is exciting, but the first month is about listening, learning, and earning trust.","Do not compare every new room with the old one. Build your place here.","The announcement is the easy part. The daily adjustment is where the move becomes real.","I will handle the external noise. You focus on the football and the people beside you.","New club, same standards. That is how you make the transition yours."}:new[]{"So it is really happening. Strange feeling, is it not?","Whatever comes next, do not forget the people who helped you get here.","New colours will take some getting used to. I hope they treat you well.","I am proud of you, even if I am selfishly sorry to see you leave.","The next chapter is yours. Make sure you keep the parts of yourself that got you here."},
            "international"=>new[]{"That call-up must feel different from a club selection.","Bring the confidence back with you when international duty is over.","Your family must be proud. Let yourself enjoy that part of it.","The shirt carries history, but you still have to play your own game in it.","Travel well, represent yourself properly, and come back healthy."},
            "conflict"=>tense?new[]{"I do not want this to become a performance for everyone else.","Say what you mean to me directly and we can work with it.","We clearly see the moment differently. I am willing to hear your side.","I am not going to pretend there is no tension. Let us decide what to do with it.","The team cannot carry an argument forever. We need a way forward."}:new[]{"I do not want a small misunderstanding to become a bigger story.","If I upset you, tell me what I missed.","We can disagree without making the room uncomfortable for everyone.","I would rather clear the air now than let it sit between us.","Give me the honest version. I will try not to answer defensively."},
            "support"=>new[]{"You can say it plainly. I am not going to judge the first version.","Do you want advice, distraction, or just someone to stay on the phone?","You do not have to be useful in this conversation.","I have time. Start with the part you keep replaying.","Whatever you are carrying, you do not need to carry all of it alone."},
            "question"=>manager?new[]{"That is a fair question. Let us look at what we know before we decide what it means.","I will give you an honest answer, but I want an honest conversation in return.","Come to my office before training and we will work through it properly.","The short answer is not enough here. Tell me what is really worrying you.","I understand why you are asking. We can discuss the football and the feeling behind it."}:new[]{"You want the honest version? I will give it to you.","I have wondered about that too. Let us not rush the answer.","Tell me what made you ask, and I will tell you what I think.","That is a bigger question than it looks. We have time.","I hear you. Give me the context and I will not guess at the rest."},
            _=>manager?new[]{"Understood. Keep your focus on the next session and bring that standard with you.","I hear you. We will turn it into a practical next step.","Noted. We deal with the important part first, then move on.","Thanks for telling me. I will keep it in mind when we plan the next session.","Stay close to the work. It has a way of making the next decision clearer."}:friendly?new[]{"I get you. Keep talking if there is more behind it.","Fair enough. I am glad you said it instead of keeping it to yourself.","That makes sense to me. We will figure out the next bit together.","I hear you. Let us not make it heavier than it needs to be.","Thanks for trusting me with that."}:new[]{"I am listening. Tell me what is on your mind.","I get it. Say as much or as little as you want.","Understood. We can leave it there or keep talking.","That is worth thinking about. I will not dismiss it.","I hear you. Let us take it one piece at a time."}
        };
    }

    private static IReadOnlyList<string> PingLines(Character character,Career career,Relationship relationship,Personality p,CommunicationStyle c)
    {
        var ageGap=character.Age>0&&career.Age>0?character.Age-career.Age:0;var familiar=relationship.Familiarity>=35||relationship.Friendliness>=35;var senior=ageGap>=7||character.Type is CharacterType.Manager or CharacterType.Agent;var junior=ageGap<=-5&&ageGap!=0;
        if(c.Formality>=65||p.Diplomacy>=75)return senior?new[]{"Yes, how can I help?","I am here. What would you like to discuss?","Go ahead, I am listening.","Yes? Is everything all right?"}:new[]{"Yes?","Go ahead.","What is it?","I am listening."};
        if(p.Humor>=65||c.Humor>=65)return familiar?new[]{"That sounds ominous. Go on.","You have my attention. What trouble are we discussing?","A one-word message usually means a story is coming.","Yeah? Make it interesting.","I am here. This had better be good."}:new[]{"Cris? That is me. What is up?","You called? Go on.","That is a mysterious opening. What happened?","Yes? I promise I am listening.","I am here. No need to be dramatic yet."};
        if(senior)return familiar?new[]{"Yes, mate?","I am here. What do you need?","Go on, I have a minute.","What is it? You all right?","Talk to me."}:new[]{"Yes?","I am listening.","What do you need?","Go ahead.","Is everything okay?"};
        if(junior)return familiar?new[]{"Yeah, what is up?","I am here. What happened?","Go on, mate.","What is it?","You all right?"}:new[]{"Yeah?","What is up?","Go on.","What happened?","You need something?"};
        if(Math.Abs(ageGap)<5)return familiar?new[]{"Mate, what is up?","Yeah, talk to me.","You all right?","Go on, I am listening.","What happened?"}:new[]{"Yeah?","What is it?","You need something?","Go on.","What happened?"};
        if(p.Openness>=65)return new[]{"I am here. What is on your mind?","Yeah, talk to me.","Go on, I have time.","What happened?","I am listening."};
        if(c.Directness>=75)return new[]{"What is it?","Go on.","Say it.","What do you need?","Yes?"};
        return new[]{"Yeah? What is up?","I am here. What happened?","Go on.","What is it?","You all right?"};
    }

    private static string Intent(string text)
    {
        if(text.Trim().Length<=8&&text.Contains('?'))return "ping";
        if(Contains(text,"depress","suicid","cannot cope","not okay","not ok","alone","cry","empty","worthless","anxious"))return "distress";
        if(Contains(text,"lose","lost","defeat","bad result","hurt","awful","terrible","angry"))return "loss";
        if(Contains(text,"injur","pain","physio","rehab","medical","ankle","knee","hamstring"))return "injury";
        if(Contains(text,"transfer","move","new club","leaving","leave us"))return "transfer";
        if(Contains(text,"country","national team","international","call-up","call up","cap","world cup","euro"))return "international";
        if(Contains(text,"bench","benched","start","starting","minutes","selection","picked","lineup","line-up"))return "selection";
        if(Contains(text,"training","session","drill","work out","workout","practice"))return "training";
        if(Contains(text,"argue","argument","angry with","upset with","disagree","blame","problem with"))return "conflict";
        if(Contains(text,"help","support","advice","talk","lonely","listen"))return "support";
        if(Contains(text,"hello","hi ","hey","morning","evening","yo "))return "greeting";
        if(Contains(text,"thank","thanks","proud","win","won","good","great","brilliant","happy"))return "positive";
        if(text.Contains('?')||text.StartsWith("how ",StringComparison.OrdinalIgnoreCase)||text.StartsWith("what ",StringComparison.OrdinalIgnoreCase)||text.StartsWith("why ",StringComparison.OrdinalIgnoreCase))return "question";
        return "general";
    }
    private static bool Contains(string text,params string[] terms)=>terms.Any(term=>text.Contains(term,StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Picks a line and, when several characters are reacting to the same event, records it so the next
    /// speaker moves on to another one. Two people sending the player identical words is the fastest way
    /// to break the illusion that they are separate people.
    /// </summary>
    private static string Pick(IReadOnlyList<string> values,int seed,ISet<string>? spoken=null)
    {
        if(values.Count==0)return "I am here.";
        var offset=Math.Abs(seed)%values.Count;
        if(spoken is null)return values[offset];
        for(var i=0;i<values.Count;i++)
        {
            var candidate=values[(offset+i)%values.Count];
            if(spoken.Add(Normalize(candidate)))return candidate;
        }
        return values[offset];
    }

    /// <summary>Compares lines by their words alone, so punctuation or casing cannot hide a repeat.</summary>
    internal static string Normalize(string text)=>new(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static int Seed(params string[] values){unchecked{var hash=17;foreach(var value in values){foreach(var ch in value)hash=hash*31+ch;hash=hash*31+1;}return hash&int.MaxValue;}}
    private static string Trim(string value,int length)=>value.Length<=length?value:value[..length]+"...";
}
