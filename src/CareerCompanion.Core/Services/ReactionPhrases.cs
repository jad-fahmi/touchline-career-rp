using CareerCompanion.Core.Domain;

namespace CareerCompanion.Core.Services;

/// <summary>
/// Phrase pools for offline dialogue, indexed by what happened and by the attitude the speaker has taken.
/// Every entry has a stable key so the composer can avoid repeating itself across a long career.
/// Managers get their own voice; team-mates and agents share a peer voice with a few career-minded variants.
/// </summary>
internal static class ReactionPhrases
{
    private static readonly (string Key, string Text)[] None = [];

    public static IReadOnlyList<(string Key, string Text)> Openers(CharacterType type, Stance stance)
        => Merge(Lookup(OpenerPools, Voice(type), stance), Lookup(OpenerPools, "any", stance));

    public static IReadOnlyList<(string Key, string Text)> Core(CharacterType type, Stance stance, string fact)
    {
        var voice = Voice(type);
        // Staying on the actual event matters more than hitting the exact attitude. A derby win described
        // with a neighbouring stance is right; the same win described by a generic line is not.
        foreach (var pool in CoreOptions(type, stance, fact)) return pool;
        return None;
    }

    /// <summary>
    /// Every pool that could carry this moment, best first. The composer walks them so a second character
    /// reacting to the same match can move to another pool instead of echoing the first one word for word.
    /// </summary>
    public static IEnumerable<IReadOnlyList<(string Key, string Text)>> CoreOptions(CharacterType type, Stance stance, string fact)
    {
        var voice = Voice(type);
        foreach (var candidate in Nearby(stance))
        {
            var pool = Merge(Lookup(CorePools, $"{voice}|{fact}", candidate), Lookup(CorePools, $"any|{fact}", candidate));
            if (pool.Count > 0) yield return pool;
        }
        foreach (var candidate in Nearby(stance))
        {
            var pool = Merge(Lookup(CorePools, $"{voice}|routine", candidate), Lookup(CorePools, "any|routine", candidate));
            if (pool.Count > 0) yield return pool;
        }
    }

    /// <summary>The stance itself first, then the attitudes closest to it in warmth and directness.</summary>
    private static IReadOnlyList<Stance> Nearby(Stance stance) => stance switch
    {
        Stance.Praise => [Stance.Praise, Stance.Proud, Stance.Measured, Stance.Joking],
        Stance.Proud => [Stance.Proud, Stance.Praise, Stance.Measured, Stance.Joking],
        Stance.Joking => [Stance.Joking, Stance.Praise, Stance.Proud, Stance.Measured],
        Stance.Measured => [Stance.Measured, Stance.Praise, Stance.Challenging, Stance.Proud, Stance.Supportive],
        Stance.Supportive => [Stance.Supportive, Stance.Measured, Stance.Challenging, Stance.Praise],
        Stance.Challenging => [Stance.Challenging, Stance.Critical, Stance.Measured, Stance.Supportive],
        Stance.Critical => [Stance.Critical, Stance.Frustrated, Stance.Challenging, Stance.Measured],
        Stance.Frustrated => [Stance.Frustrated, Stance.Critical, Stance.Challenging, Stance.Measured],
        _ => [Stance.Distant, Stance.Measured, Stance.Praise]
    };

    public static IReadOnlyList<(string Key, string Text)> Detail(CharacterType type, Stance stance, string fact)
        => Merge(Lookup(DetailPools, $"{Voice(type)}|{fact}", stance), Lookup(DetailPools, $"any|{fact}", stance));

    public static IReadOnlyList<(string Key, string Text)> Forward(CharacterType type, Stance stance)
        => Merge(Lookup(ForwardPools, Voice(type), stance), Lookup(ForwardPools, "any", stance));

    /// <summary>
    /// The last resort, when no pool had a line this match could fill. It is a keyed pool rather than a
    /// single sentence so that two silent characters do not end up sending the player identical words.
    /// </summary>
    public static IReadOnlyList<(string Key, string Text)> Fallbacks(CharacterType type, Stance stance)
        => type == CharacterType.Manager
            ? [("mgr-fb-review", "We will review it properly when the emotion has settled."),
               ("mgr-fb-tomorrow", "We will go through it tomorrow with clear heads."),
               ("mgr-fb-next", "Nothing useful gets decided tonight. Next one matters more."),
               ("mgr-fb-training", "Bring it to training and we will work on it there.")]
            : stance is Stance.Critical or Stance.Frustrated
                ? [("peer-fb-more", "We need more than that."),
                   ("peer-fb-honest", "Not much to say about that one, honestly."),
                   ("peer-fb-standard", "That was below what we expect from each other."),
                   ("peer-fb-flat", "Long night. We have to be better than that.")]
                : [("peer-fb-good", "Good to see you out there."),
                   ("peer-fb-fine", "All right out there today."),
                   ("peer-fb-keep", "Keep going, mate."),
                   ("peer-fb-catch", "Catch you at training.")];

    private static string Voice(CharacterType type) => type switch
    {
        CharacterType.Manager => "manager",
        CharacterType.Agent => "agent",
        _ => "peer"
    };

    private static IReadOnlyList<(string, string)> Merge(params IReadOnlyList<(string, string)>[] pools)
    {
        var result = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pool in pools)
            foreach (var entry in pool)
                if (seen.Add(entry.Item1)) result.Add(entry);
        return result;
    }

    private static IReadOnlyList<(string, string)> Lookup(
        IReadOnlyDictionary<string, IReadOnlyDictionary<Stance, (string, string)[]>> pools, string bucket, Stance stance)
        => pools.TryGetValue(bucket, out var byStance) && byStance.TryGetValue(stance, out var entries) ? entries : None;

    private static IReadOnlyDictionary<Stance, (string, string)[]> S(params (Stance Stance, string[] Lines)[] groups)
    {
        var result = new Dictionary<Stance, (string, string)[]>();
        foreach (var group in groups)
            result[group.Stance] = group.Lines.Select((text, index) => ($"{group.Stance}:{Hash(text)}:{index}", text)).ToArray();
        return result;
    }

    private static string Hash(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in text) { hash ^= ch; hash *= 16777619; }
            return hash.ToString("x8");
        }
    }

    private static readonly Dictionary<string, IReadOnlyDictionary<Stance, (string, string)[]>> OpenerPools = new(StringComparer.Ordinal)
    {
        ["manager"] = S(
            (Stance.Praise, ["Well played.", "Good afternoon's work.", "That was more like it."]),
            (Stance.Proud, ["I have to say it.", "Days like that are why we do this.", "Take a moment with that one."]),
            (Stance.Measured, ["A few thoughts.", "Right.", "Quick word."]),
            (Stance.Critical, ["We need to talk.", "I will be direct.", "I am not going to dress this up."]),
            (Stance.Challenging, ["Listen.", "Here is where we are.", "One thing before you switch off."]),
            (Stance.Supportive, ["Before you go.", "Come here a second.", "Head up."]),
            (Stance.Frustrated, ["That was hard to watch.", "I am not happy.", "Honestly?"]),
            (Stance.Joking, ["Do not let it go to your head.", "Well then.", "You enjoyed that, did you?"]),
            (Stance.Distant, ["Noted.", "Right."])),
        ["peer"] = S(
            (Stance.Praise, ["Mate.", "Hey.", "Listen."]),
            (Stance.Proud, ["Honestly?", "I have to say it.", "Mate, seriously."]),
            (Stance.Joking, ["Right then.", "Okay, okay.", "So."]),
            (Stance.Measured, ["Hey.", "Alright?"]),
            (Stance.Critical, ["Look.", "I will say it if nobody else will."]),
            (Stance.Supportive, ["Hey.", "Listen.", "Come here."]),
            (Stance.Frustrated, ["Honestly.", "Look."]),
            (Stance.Challenging, ["Right.", "Listen."])),
        ["agent"] = S(
            (Stance.Praise, ["Just seen the numbers.", "Quick one."]),
            (Stance.Proud, ["I will be honest with you.", "Right, listen."]),
            (Stance.Measured, ["Checking in.", "Quick one."]),
            (Stance.Supportive, ["Picking up the phone before you do.", "Listen."]),
            (Stance.Challenging, ["We should talk.", "One thing."]),
            (Stance.Critical, ["I am going to be blunt.", "We need to talk about this."]))
    };

    private static readonly Dictionary<string, IReadOnlyDictionary<Stance, (string, string)[]>> CorePools = new(StringComparer.Ordinal)
    {
        // ---- sendings-off ----
        ["manager|red_card"] = S(
            (Stance.Critical, ["You left ten men out there against {opponent}. That is not competing, that is costing us.",
                "The sending-off decided how the rest of that match went. I need you on the pitch, not in the tunnel.",
                "I can accept mistakes with the ball. I cannot accept losing you to a moment of temper."]),
            (Stance.Challenging, ["We will look at the red card together, but the response matters more than the decision.",
                "You are going to miss matches now. Use the time instead of sulking through it.",
                "One sending-off is a moment. Two becomes a reputation. Do not let it get there."]),
            (Stance.Supportive, ["The red card will replay in your head tonight. Do not let it become a story about who you are.",
                "I have had players sent off in far worse circumstances. It is recoverable. Come in tomorrow ready."]),
            (Stance.Measured, ["We will review the sending-off with the staff before anyone says anything public."])),
        ["peer|red_card"] = S(
            (Stance.Critical, ["We were down to ten because of you. That is the bit I am struggling with.",
                "You know that was avoidable. The lads had to run themselves into the ground after it."]),
            (Stance.Supportive, ["Forget the noise about the red. It happens, and we have all been there.",
                "Do not sit on your own tonight replaying it. It is one decision, not your whole season."]),
            (Stance.Frustrated, ["I am not going to pretend that was fine. We had a game to win.",
                "That one hurt us. I know you did not mean it, but it hurt us."]),
            (Stance.Measured, ["Tough one to take, that red card."])),

        // ---- goals ----
        ["manager|hattrick"] = S(
            (Stance.Proud, ["{detail} against {opponent}. That is a performance people will remember for a long time.",
                "Three goals in this shirt is not a small thing. Enjoy tonight properly."]),
            (Stance.Praise, ["{detail} and you never stopped moving. That is exactly the level I have been asking for.",
                "You took every chance that came. Clinical."]),
            (Stance.Measured, ["{detail}. The finishing was excellent. Now the challenge is making that normal rather than exceptional."]),
            (Stance.Joking, ["{detail}? Leave a couple for the rest of the squad next week."])),
        ["peer|hattrick"] = S(
            (Stance.Proud, ["{detail}. I have played with a lot of people and that was something else.",
                "You just put {detail} past {opponent}. Nobody in that dressing room is talking about anything else."]),
            (Stance.Joking, ["{detail}. I am claiming at least one of those as my assist, by the way.",
                "Match ball is yours. I checked, they will not let me have it."]),
            (Stance.Praise, ["{detail} and you still tracked back. That is the bit people will miss."]),
            (Stance.Measured, ["{detail}. Good day for you."]),
            (Stance.Distant, ["{detail}. Fair play."])),
        ["manager|brace"] = S(
            (Stance.Praise, ["Two goals and you kept working without the ball. That is the complete version of you.",
                "Two goals against {opponent} changes a match. Well taken."]),
            (Stance.Measured, ["Two goals. The finishing was good; I want the same conviction when the chances are harder."]),
            (Stance.Proud, ["Two goals in a match like that says a lot about your nerve."])),
        ["peer|brace"] = S(
            (Stance.Praise, ["Two goals, and both of them looked easy from where I was standing.",
                "You won us that with the two goals. Simple as that."]),
            (Stance.Joking, ["Two goals. I am starting to feel unnecessary out there."]),
            (Stance.Measured, ["Two goals. Good day."])),
        ["manager|goal"] = S(
            (Stance.Praise, ["Good goal, and the timing of the run was better than the finish.",
                "Well taken. You were in the right place because you worked to be there.",
                "That is the finish of someone who expects to score.",
                "One goal, but the one that mattered against {opponent}."]),
            (Stance.Measured, ["You got your goal. I want more of the work around it next time.",
                "A goal is a goal. The rest of your game can still improve.",
                "You scored, and you did the basics properly. That is the order I want it in.",
                "Goal on the sheet against {opponent}. Fine."]),
            (Stance.Challenging, ["The goal was good. The twenty minutes before it were not.",
                "One goal does not settle the selection question.",
                "You scored and still drifted out of the game for long spells."]),
            (Stance.Proud, ["{season}. The numbers are starting to speak for you."]),
            (Stance.Joking, ["A goal and no theatrics. I might frame that."])),
        ["peer|goal"] = S(
            (Stance.Praise, ["Lovely finish. You made that look calm.",
                "That goal was the difference, whatever anyone says.",
                "Great run for the goal. I saw you go before you did."]),
            (Stance.Joking, ["Nice goal. Celebration needs work, though.",
                "One goal and you are already impossible to talk to.",
                "I set that up in my head, so I am taking half of it."]),
            (Stance.Measured, ["Good goal today.", "Nice one on the scoresheet.",
                "You got your goal against {opponent}, at least."]),
            (Stance.Proud, ["{season}. Nobody is surprised any more."])),
        ["any|drought_broken"] = S(
            (Stance.Praise, ["{detail}. You could see what it meant to you.",
                "That is {detail}. The weight is off now."]),
            (Stance.Proud, ["{detail}. I know how much that has been sitting on you."]),
            (Stance.Supportive, ["{detail}. Told you it would come."]),
            (Stance.Joking, ["{detail}. Somebody check whether the goal is still standing."]),
            (Stance.Measured, ["{detail}. Good to see it finally go in."])),
        ["any|drought"] = S(
            (Stance.Supportive, ["{detail}, and I know you are counting. Keep getting into those positions.",
                "The goals are not coming, but the movement is still right. That is the part that comes back first."]),
            (Stance.Challenging, ["{detail}. I need you to stop waiting for the perfect chance and hit the first one.",
                "{detail}. Confidence follows the shot, not the other way round."]),
            (Stance.Critical, ["{detail}. At some point the number stops being bad luck."]),
            (Stance.Measured, ["Still no goal. It will turn."])),

        // ---- results ----
        ["manager|derby_win"] = S(
            (Stance.Proud, ["Beating {opponent} is not just three points and you know it. The supporters will carry that for weeks.",
                "{score} against {opponent}. Enjoy tonight, because these ones do not come round often."]),
            (Stance.Praise, ["We won the derby {score}. You handled the occasion without losing your discipline."]),
            (Stance.Measured, ["A derby win {score}. Take the feeling, then put it down before training."])),
        ["peer|derby_win"] = S(
            (Stance.Proud, ["We beat {opponent}. I am not sleeping tonight and neither should you.",
                "{score} against {opponent}. That is the one everybody remembers.",
                "Years from now people will still bring that one up."]),
            (Stance.Joking, ["{score} against {opponent}. I am going to be insufferable about this for a month.",
                "I have already texted three people who support {opponent}.",
                "{score}. I am never letting them forget this."]),
            (Stance.Praise, ["We got the derby {score}. You were right in the middle of it.",
                "{score} in a derby. You did not hide from a single moment out there.",
                "Beating {opponent} takes a certain kind of head. You had it today."]),
            (Stance.Measured, ["{score} against {opponent}. Enjoy it, because the next one comes round quickly.",
                "We got the derby. That will keep the place happy for a while."])),
        ["manager|derby_loss"] = S(
            (Stance.Critical, ["Losing {score} to {opponent} is the result that follows you around. We were not ready for the occasion.",
                "That was a derby and we let them dictate it. I expect more from this group."]),
            (Stance.Challenging, ["Losing to {opponent} hurts and it should. The only answer is the next performance."]),
            (Stance.Supportive, ["Derby defeats sting longer than the rest. Do not carry it alone this week."])),
        ["peer|derby_loss"] = S(
            (Stance.Frustrated, ["Losing to {opponent} of all teams. I cannot look at my phone tonight.",
                "{score} to {opponent}. I will not be over that for a while."]),
            (Stance.Supportive, ["Losing the derby is the worst one. Stay off social media and stay near the lads."]),
            (Stance.Measured, ["Derby gone. We go again."])),
        ["manager|heavy_defeat"] = S(
            (Stance.Critical, ["{score} is not a bad day, it is a standard problem. We fix it on the training pitch this week.",
                "We were second best in every duel. I will not pretend otherwise to make anyone feel better."]),
            (Stance.Challenging, ["{score} at {opponent}. The response starts tomorrow morning, and I will be watching who leads it."]),
            (Stance.Supportive, ["{score} is a heavy night. Judge yourself on the month, not on that."])),
        ["peer|heavy_defeat"] = S(
            (Stance.Frustrated, ["{score}. We were nowhere near it and everybody knows it."]),
            (Stance.Supportive, ["{score} is a horrible one to sit with. Do not disappear tonight."]),
            (Stance.Measured, ["Heavy one. Nothing to say that helps right now."])),
        ["manager|big_win"] = S(
            (Stance.Praise, ["{score}. That is what we look like when we play with conviction from the first whistle."]),
            (Stance.Measured, ["{score}. Comfortable, but the standard only counts if it holds next week."])),
        ["peer|big_win"] = S(
            (Stance.Joking, ["{score}. I actually enjoyed the last twenty minutes for once."]),
            (Stance.Praise, ["{score} and it never felt in doubt. Proper performance."])),
        ["manager|narrow_win"] = S(
            (Stance.Measured, ["We got the result {score}. It was closer than it needed to be, but I will take it.",
                "{score}. Winning the tight ones is a habit worth building.",
                "{score} against {opponent}. Not comfortable, but three points are three points.",
                "We made hard work of {opponent}. The result is what matters this week."]),
            (Stance.Praise, ["{score}, and you did the ugly parts as well as the good ones. That is why we won it.",
                "{score}. You held your nerve when the match was tight.",
                "That is a win built on concentration, and you had plenty of it."]),
            (Stance.Challenging, ["{score}. We should not be that nervous against {opponent}."]),
            (Stance.Proud, ["{score}, and we are {run}. This group is building something."])),
        ["peer|narrow_win"] = S(
            (Stance.Praise, ["{score}. We found a way, and that counts for something.",
                "{score} and you were in the middle of everything good."]),
            (Stance.Joking, ["{score}. My heart rate has still not come down.",
                "{score}. Why do we always make it difficult?",
                "{score}. I aged about five years in that last ten minutes."]),
            (Stance.Measured, ["{score}. We will take it.", "{score} against {opponent}. Job done.",
                "Tight one, but we are {run}."])),
        ["manager|defeat"] = S(
            (Stance.Challenging, ["We lost {score}. I want a clear-headed response, not a reaction.",
                "{score}. There were periods in there we controlled. We have to make them count."]),
            (Stance.Critical, ["{score} and we made the same mistakes we talked about all week."]),
            (Stance.Supportive, ["{score}. Difficult night, but nothing in that performance worries me long term."])),
        ["peer|defeat"] = S(
            (Stance.Supportive, ["{score}. It happens. We go again on the training pitch."]),
            (Stance.Frustrated, ["{score}. We gave that away and it is annoying me more than it should."]),
            (Stance.Measured, ["{score}. On to the next one."])),
        ["manager|draw"] = S(
            (Stance.Measured, ["{score}. A point, though there was more in that match for us.",
                "{score}. We managed the game without ever taking hold of it."]),
            (Stance.Challenging, ["{score}. Draws like that decide where you finish."])),
        ["peer|draw"] = S(
            (Stance.Measured, ["{score}. Feels like we left one out there."]),
            (Stance.Joking, ["{score}. Nobody is writing a song about that one."])),
        ["any|run_ended"] = S(
            (Stance.Praise, ["{detail}. You could feel the relief around the ground."]),
            (Stance.Proud, ["{detail}. The group needed that more than anyone will admit."]),
            (Stance.Measured, ["{detail}. Now we make it two."])),
        ["any|run_broken"] = S(
            (Stance.Measured, ["{detail}. It had to end sometime."]),
            (Stance.Challenging, ["{detail}. The test is whether one defeat becomes three."])),

        // ---- individual level ----
        ["manager|outstanding"] = S(
            (Stance.Praise, ["You were {detail}. That is the level I want as your baseline, not your ceiling."]),
            (Stance.Proud, ["You were {detail} and you carried the tempo for the whole team. Excellent."]),
            (Stance.Measured, ["{detail}. Good. Now repeat it when the pitch is heavier and the crowd is against us."])),
        ["peer|outstanding"] = S(
            (Stance.Praise, ["You were the best player on the pitch and it was not close."]),
            (Stance.Proud, ["{detail}. Genuinely one of the best games I have seen from you."]),
            (Stance.Joking, ["{detail}. Save some of it for when I am watching from the bench."]),
            (Stance.Distant, ["Good game today."])),
        ["manager|poor_display"] = S(
            (Stance.Critical, ["{detail}. You were off the pace all evening and the team felt it.",
                "That was below the standard we have agreed. I want to see the difference in training, not hear about it."]),
            (Stance.Challenging, ["{detail}. One flat performance is fine. Show me the reaction in the next session."]),
            (Stance.Supportive, ["{detail}. I have seen enough of you to know that is not the player you are."])),
        ["peer|poor_display"] = S(
            (Stance.Supportive, ["Everybody has one of those. Do not overthink it."]),
            (Stance.Critical, ["That was not you today. We needed more."]),
            (Stance.Frustrated, ["We could have used you in that game."]),
            (Stance.Measured, ["Quiet one for you today."])),
        ["any|flat_display"] = S(
            (Stance.Measured, ["{detail}. Not your sharpest, but nothing to lose sleep over."]),
            (Stance.Challenging, ["{detail}. I know there is more there."]),
            (Stance.Supportive, ["{detail}. It was a difficult game to play in."])),
        ["any|team_best"] = S(
            (Stance.Praise, ["You were {detail}. When the game got difficult, you were the one who kept playing."]),
            (Stance.Proud, ["You were {detail}, and that is leadership whether you call it that or not."]),
            (Stance.Measured, ["You were {detail} today.", "You came out of that with more credit than most of us."])),
        ["any|team_worst"] = S(
            (Stance.Critical, ["You were the one who struggled most out there. You will have seen it back by now."]),
            (Stance.Supportive, ["It was not your afternoon. It happens to everyone in this squad."]),
            (Stance.Challenging, ["You know that was below your level. So do I."])),
        ["any|above_the_team"] = S(
            (Stance.Praise, ["You were a level above the rest of us out there."]),
            (Stance.Measured, ["You were one of the few who came out of that with credit."])),
        ["any|below_the_team"] = S(
            (Stance.Challenging, ["The rest of the group found a level you did not reach today."]),
            (Stance.Supportive, ["You were not alone in struggling, but you will be hardest on yourself."])),
        ["manager|penalty_missed"] = S(
            (Stance.Supportive, ["Missing a penalty takes nothing away from being brave enough to take it. You take the next one."]),
            (Stance.Measured, ["The penalty will bother you more than it bothers me."]),
            (Stance.Critical, ["The penalty was a big moment and we did not take it."])),
        ["peer|penalty_missed"] = S(
            (Stance.Supportive, ["Forget the penalty. You stepped up, which is more than most would."]),
            (Stance.Joking, ["I would have missed it worse. Considerably worse."]),
            (Stance.Measured, ["Penalties are a lottery. Next one goes in."])),

        // ---- selection ----
        ["manager|bench_streak"] = S(
            (Stance.Challenging, ["{detail}. I know you are frustrated. Frustration is fine; show it in the work.",
                "{detail}. You want the shirt back, and I want a reason to give it to you. Both of those are on the training pitch."]),
            (Stance.Supportive, ["{detail}. You have not dropped out of my thinking. Keep the standard up."]),
            (Stance.Critical, ["{detail}. If you want to change that, the intensity in training has to change first."])),
        ["peer|bench_streak"] = S(
            (Stance.Supportive, ["{detail}. I know how much that eats at you. Stay ready, it turns quickly.",
                "Watching from the bench that long is horrible. Do not let it make you quiet around the group."]),
            (Stance.Measured, ["{detail} now. That is a hard run."])),
        ["any|bench_cameo"] = S(
            (Stance.Praise, ["{detail} and you changed the rhythm as soon as you came on."]),
            (Stance.Measured, ["{detail}. Not much time to make a case, but you used it."]),
            (Stance.Challenging, ["{detail}. Make the next twenty minutes impossible to ignore."])),

        // ---- context ----
        ["any|unknown_score"] = S(
            (Stance.Measured, ["I have your minutes and how you played, but not the full picture of the match yet. How did it feel out there?",
                "I know how you did. Tell me how the game itself went."]),
            (Stance.Supportive, ["I would rather hear about the match from you than read about it anywhere else."]),
            (Stance.Praise, ["Your part of that was good, whatever the rest of the game looked like."])),
        ["any|international"] = S(
            (Stance.Praise, ["Representing {team} is not a small thing. Take it in."]),
            (Stance.Measured, ["International duty done. Recover properly before you are back with us."]),
            (Stance.Proud, ["Pulling on the {team} shirt is something a lot of players never get to do."])),
        ["manager|major_fixture"] = S(
            (Stance.Measured, ["A match like {competition} is a different kind of pressure. You handled it."]),
            (Stance.Challenging, ["These are the nights careers are measured against."])),
        ["manager|routine"] = S(
            (Stance.Measured, ["Not much to add on that one. We move to the next.",
                "Solid enough. Recover and be ready for the session.",
                "That was a controlled afternoon against {opponent}. Nothing more, nothing less.",
                "We got through {opponent} without doing anything special. That will do for now.",
                "Nothing in that match changes my thinking. Keep working.",
                "We are {run}, and matches like that are how runs are kept alive.",
                "The important part was the discipline. The rest we can improve.",
                "You did your job against {opponent}. We will look at the detail in the week."]),
            (Stance.Praise, ["Good, professional performance.",
                "You were reliable in there, and reliable wins seasons.",
                "That was a mature display against {opponent}.",
                "No fuss, no drama, job done. I like that from you.",
                "{season} now. That is a proper contribution."]),
            (Stance.Challenging, ["We will need more than that in the coming weeks.",
                "That level keeps you in the squad. It does not keep you in the team.",
                "I want to see you take more responsibility in matches like {opponent}.",
                "You are {run}. Do not let that become comfortable.",
                "There is another level in you and I am still waiting to see it regularly."]),
            (Stance.Supportive, ["Keep your head where it needs to be. The rest follows.",
                "You are doing the right things. It will come.",
                "Nothing to worry about in that performance.",
                "I know you are {mood}. Keep talking to me."]),
            (Stance.Critical, ["That was not good enough for where we want to be.",
                "We were passive against {opponent} and you were part of that.",
                "I expect more intensity than that, from you especially.",
                "Being {run} does not excuse a performance like that."]),
            (Stance.Distant, ["Fine.", "Noted.", "We will speak in the week."]),
            (Stance.Frustrated, ["I expected more from that.",
                "That was a wasted afternoon against {opponent}.",
                "We keep doing this and I am running out of patience with it."]),
            (Stance.Proud, ["Good afternoon's work.",
                "You have grown into this level quicker than most.",
                "{season}. Not many players your age manage that."]),
            (Stance.Joking, ["Not our finest hour, but we have all seen worse.",
                "I have watched worse football this month. Not much worse, mind."])),
        ["peer|routine"] = S(
            (Stance.Measured, ["Not the most memorable one, that.", "That is that one done.",
                "{opponent} away is never fun. Glad it is over.",
                "Bit of a slog, that one.",
                "We are {run}. Long may it continue, however dull it looks.",
                "Not one for the highlights, but we are still here."]),
            (Stance.Praise, ["Decent shift today.", "You did your bit out there, as always.",
                "Quietly good game from you.",
                "You made it look easy in the middle of that.",
                "{season}. You are having a proper year, you know that?"]),
            (Stance.Joking, ["Well, we were all there. That is about the best I can say.",
                "If anyone asks, we meant to play like that.",
                "I have seen more entertaining training sessions.",
                "Do not watch that one back. Trust me."]),
            (Stance.Supportive, ["You all right after that?", "You looked {mood} out there. Talk to me if you need to.",
                "Chin up, that is a long season ahead.",
                "You are doing more than people notice."]),
            (Stance.Critical, ["We were poor and you know it.",
                "That is twice now we have played like that.",
                "We cannot keep turning up like that against {opponent}."]),
            (Stance.Distant, ["Yeah.", "Aye.", "See you at training."]),
            (Stance.Frustrated, ["Not good enough, that.",
                "I am sick of games like that one.",
                "We are {run} and it still feels like hard work."]),
            (Stance.Challenging, ["We need more from all of us.",
                "Next week has to be better than that."]),
            (Stance.Proud, ["Good to be out there with you.",
                "Whatever else happens, I enjoy playing alongside you."])),
        ["agent|routine"] = S(
            (Stance.Measured, ["Nothing that changes the picture, but I am keeping track."]),
            (Stance.Praise, ["Performances like that get noticed by people who matter."]),
            (Stance.Challenging, ["We need a run of these, not one."]),
            (Stance.Supportive, ["One match does not change what I am building for you."]),
            (Stance.Proud, ["That is the kind of afternoon that moves a career."]),
            (Stance.Critical, ["That will not help the conversations I am having."]))
    };

    private static readonly Dictionary<string, IReadOnlyDictionary<Stance, (string, string)[]>> DetailPools = new(StringComparer.Ordinal)
    {
        ["any|assist"] = S(
            (Stance.Praise, ["The assist was the best pass of the match.",
                "That pass for the goal was the moment of the game.",
                "You created the goal as well, which people will forget."]),
            (Stance.Measured, ["You got an assist out of it too.", "An assist on top of it.",
                "The assist counts as much as anything."]),
            (Stance.Joking, ["Nice assist. I will pretend I would have scored it.",
                "Great pass. Shame about the finish, obviously."]),
            (Stance.Proud, ["And the assist. You were involved in everything that mattered."]),
            (Stance.Challenging, ["The assist was good. I want that decision-making every week."])),
        ["any|assists"] = S(
            (Stance.Praise, ["{second} on top of everything else."]),
            (Stance.Proud, ["{second} as well. You were involved in everything."])),
        ["any|booked"] = S(
            (Stance.Measured, ["Watch the bookings, they add up."]),
            (Stance.Challenging, ["That yellow was careless."])),
        ["any|team_best"] = S(
            (Stance.Praise, ["You were {second}, which says plenty."]),
            (Stance.Measured, ["You came out of it better than most."])),
        ["any|team_worst"] = S(
            (Stance.Supportive, ["It was not only you out there."]),
            (Stance.Critical, ["And you know you were the weak link in it."])),
        ["any|drought"] = S(
            (Stance.Supportive, ["The goals will come back."]),
            (Stance.Challenging, ["{second} is too long for a player of your quality."])),
        ["any|bench_cameo"] = S(
            (Stance.Measured, ["{second} is not much to work with."]),
            (Stance.Challenging, ["{second}. Make the next one count."])),
        ["any|outstanding"] = S(
            (Stance.Praise, ["{second} tells its own story."]),
            (Stance.Measured, ["{second} as well."])),
        ["any|poor_display"] = S(
            (Stance.Supportive, ["{second}, but the effort was there."]),
            (Stance.Critical, ["{second}. That is the number that matters."])),
        ["any|penalty_scored"] = S(
            (Stance.Praise, ["Taking the penalty took nerve as well."]),
            (Stance.Measured, ["The penalty was calmly done."]))
    };

    private static readonly Dictionary<string, IReadOnlyDictionary<Stance, (string, string)[]>> ForwardPools = new(StringComparer.Ordinal)
    {
        ["manager"] = S(
            (Stance.Praise, ["Recover well and bring that into the next session.", "Keep the standard where it is now."]),
            (Stance.Proud, ["Enjoy it tonight, then put it away.", "Take the credit, then get back to work."]),
            (Stance.Measured, ["We will look at the detail this week.", "Recover properly.", "On to the next one."]),
            (Stance.Critical, ["I want to see a response in training tomorrow.", "We will talk properly before the next match."]),
            (Stance.Challenging, ["Show me the answer on the grass.", "The next selection is still yours to earn."]),
            (Stance.Supportive, ["Come and see me if it is still sitting with you tomorrow.", "Rest first. We will talk after."]),
            (Stance.Frustrated, ["Tomorrow will not be a comfortable session."]),
            (Stance.Joking, ["Do not get used to me saying nice things."])),
        ["peer"] = S(
            (Stance.Praise, ["See you at training.", "Get some rest, you have earned it."]),
            (Stance.Proud, ["Enjoy it properly tonight.", "Do not let anyone take that away from you."]),
            (Stance.Joking, ["Drinks are on you.", "You are buying breakfast."]),
            (Stance.Measured, ["See you tomorrow.", "Get some sleep."]),
            (Stance.Supportive, ["Message me if you need company later.", "Do not sit on your own with it.", "I am around, yeah?"]),
            (Stance.Critical, ["We need to be better. All of us."]),
            (Stance.Frustrated, ["I will see you tomorrow."]),
            (Stance.Challenging, ["Prove it next week."])),
        ["agent"] = S(
            (Stance.Praise, ["I will make sure the right people saw it."]),
            (Stance.Proud, ["This is the kind of run that changes your options."]),
            (Stance.Measured, ["I will keep watching how the season develops."]),
            (Stance.Supportive, ["Call me if you want to talk away from the club."]),
            (Stance.Challenging, ["We need consistency before I can move anything forward."]),
            (Stance.Critical, ["I need more than this to work with."]))
    };
}
