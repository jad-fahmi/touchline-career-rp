using CareerCompanion.Core.Domain;
using CareerCompanion.Core.Persistence;
using CareerCompanion.Core.Simulation;

namespace CareerCompanion.Core.Services;

/// <summary>How a character has decided to come at the conversation. Not everyone is supportive.</summary>
public enum Stance { Praise, Joking, Measured, Critical, Supportive, Challenging, Frustrated, Distant, Proud }

public sealed record ComposedReaction(string Text, IReadOnlyList<string> PhraseKeys, int Valence, int Importance, Stance Stance);

/// <summary>
/// Builds offline character dialogue from career facts rather than picking a canned line. The message a
/// character sends depends on what actually happened, how they feel about the player, who they are, and
/// what they have already said before, so a long career keeps producing new conversations.
/// </summary>
public sealed class ReactionComposer(Database db)
{
    private const int RecentKeyWindow = 180;
    /// <summary>Kept smaller than the personal window so the pools never run dry across a whole squad.</summary>
    private const int OthersKeyWindow = 150;
    private static readonly IReadOnlySet<string> NoKeys = new HashSet<string>(StringComparer.Ordinal);

    /// <param name="alreadySaid">Phrases used by other characters reacting to this same match, so two people never say the same line.</param>
    public ComposedReaction Compose(Character character, MatchNarrative narrative, long eventId,
        IReadOnlySet<string>? alreadySaid = null)
    {
        var relationship = db.GetRelationship(character.Id);
        var state = db.GetCharacterState(character.Id);
        var recent = db.GetRecentDialogueKeys(narrative.Career.Id, character.Id, RecentKeyWindow).ToHashSet(StringComparer.Ordinal);
        // What this character said before is worth avoiding. What someone else said, either about this same
        // match or anywhere in the recent past, is forbidden outright: a sentence arriving from a second
        // person is the one repetition a reader always notices.
        var forbidden = alreadySaid is null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(alreadySaid, StringComparer.Ordinal);
        forbidden.UnionWith(db.GetRecentDialogueKeysFromOthers(narrative.Career.Id, character.Id, OthersKeyWindow));
        recent.UnionWith(forbidden);
        var mate = narrative.Squad.FirstOrDefault(x => string.Equals(x.Name, character.Name, StringComparison.OrdinalIgnoreCase));
        var stance = ChooseStance(character, narrative, relationship, state, mate, eventId);
        var subject = Subject(narrative, stance);
        var slots = Slots(character, narrative, mate, subject);
        var seed = Seed(character.Id, eventId, narrative.Match.Id, stance.ToString());

        var used = new List<string>();
        var parts = new List<string>();
        var voice = new Voice(character, relationship, narrative);
        var lengthBudget = LengthBudget(character, narrative, stance);

        if (lengthBudget >= 3 && voice.UsesOpeners)
        {
            var opener = Choose(ReactionPhrases.Openers(character.Type, stance), recent, used, seed, slots, forbidden);
            if (opener is not null) parts.Add(opener);
        }
        var core = ChooseCore(character.Type, stance, subject.Key, recent, used, seed + 1, slots, forbidden);
        if (core is not null) parts.Add(core);
        if (lengthBudget >= 2 && narrative.Facts.Count > 1)
        {
            var second = narrative.Facts.FirstOrDefault(x => x.Key != subject.Key) ?? narrative.Facts[1];
            var detail = Choose(ReactionPhrases.Detail(character.Type, stance, second.Key), recent, used, seed + 2, slots, forbidden);
            if (detail is not null) parts.Add(detail);
        }
        if (lengthBudget >= 3)
        {
            var forward = Choose(ReactionPhrases.Forward(character.Type, stance), recent, used, seed + 3, slots, forbidden);
            if (forward is not null) parts.Add(forward);
        }
        if (parts.Count == 0)
        {
            var last = ReactionPhrases.Fallbacks(character.Type, stance);
            var pick = Choose(last, recent, used, seed + 4, slots, forbidden) ?? Choose(last, NoKeys, used, seed + 4, slots, forbidden);
            parts.Add(pick ?? last[(int)(seed % last.Count)].Text);
        }

        var text = string.Join(" ", parts.Select(Polish)).Trim();
        var valence = Valence(stance, narrative);
        var importance = Math.Clamp(narrative.Intensity, 30, 90);
        return new(text, used, valence, importance, stance);
    }

    /// <summary>
    /// Walks the candidate pools until one offers a line nobody has used recently. Only if every pool is
    /// exhausted does it accept a repeat, which keeps two speakers from echoing each other word for word.
    /// </summary>
    private static string? ChooseCore(CharacterType type, Stance stance, string fact, IReadOnlySet<string> recent,
        List<string> used, long seed, IReadOnlyDictionary<string, string> slots, IReadOnlySet<string> forbidden)
    {
        var pools = ReactionPhrases.CoreOptions(type, stance, fact).ToList();
        foreach (var pool in pools)
        {
            var fresh = Choose(pool, recent, used, seed, slots, forbidden, strict: true);
            if (fresh is not null) return fresh;
        }
        // Repeating something this character said months ago is acceptable. Repeating what a team-mate just
        // said about this match is not, so the forbidden set still applies on the second pass.
        foreach (var pool in pools)
        {
            var repeated = Choose(pool, recent, used, seed, slots, forbidden);
            if (repeated is not null) return repeated;
        }
        return null;
    }

    /// <summary>Stores the phrases a character has just used so the same wording does not come back for a long time.</summary>
    public void Remember(long careerId, long characterId, IReadOnlyList<string> keys)
        => db.RecordDialogueKeys(careerId, characterId, keys);

    private static int Valence(Stance stance, MatchNarrative narrative) => stance switch
    {
        Stance.Praise or Stance.Proud => 45,
        Stance.Joking => 30,
        Stance.Supportive => 25,
        Stance.Measured => narrative.Headline.Tone == FactTone.Negative ? -5 : 10,
        Stance.Challenging => -10,
        Stance.Critical => -35,
        Stance.Frustrated => -25,
        _ => 0
    };

    /// <summary>Longer messages come from expressive people, close relationships, and matches that earned them.</summary>
    private static int LengthBudget(Character character, MatchNarrative narrative, Stance stance)
    {
        var communication = character.Profile.Communication;
        var budget = communication.ResponseLength switch { "very brief" => 1, "brief" => 2, _ => 3 };
        if (communication.Expressiveness >= 65) budget++;
        if (narrative.Intensity >= 75) budget++;
        if (narrative.Intensity <= 40) budget--;
        if (stance is Stance.Distant) budget = Math.Min(budget, 1);
        return Math.Clamp(budget, 1, 4);
    }

    private static Stance ChooseStance(Character character, MatchNarrative narrative, Relationship relationship,
        CharacterState state, MatchPerformance? mate, long eventId)
    {
        var p = character.Profile.Personality;
        var c = character.Profile.Communication;
        var headline = narrative.Headline;
        var warmth = relationship.Score + relationship.Friendliness + relationship.Trust;
        var friction = relationship.Tension + relationship.Rivalry;
        var jitter = (int)(Seed(character.Id, eventId, narrative.Match.Id, "stance") % 100);

        // Someone who had a poor match themselves is less likely to hand out compliments.
        var ownBadGame = mate is not null && mate.Minutes >= 25 && mate.Rating > 0 && mate.Rating <= 6.0;
        var ownGoodGame = mate is not null && mate.Rating >= 7.5;

        if (headline.Tone == FactTone.Negative)
        {
            // Blame follows what the player did, not only which fact happens to be the biggest headline.
            var blameWorthy = PlayerFaultKeys.Any(narrative.Has);
            if (blameWorthy && friction >= 45 && p.Diplomacy < 60) return Stance.Critical;
            if (blameWorthy && character.Type == CharacterType.Manager && (p.Patience < 45 || c.Directness >= 75)) return Stance.Critical;
            if (blameWorthy && ownBadGame && p.Aggression >= 55) return Stance.Frustrated;
            if (warmth >= 60 && p.Openness >= 50) return Stance.Supportive;
            if (character.Type == CharacterType.Manager && p.Professionalism >= 70) return Stance.Challenging;
            if (p.Loyalty >= 65 || p.Openness >= 60) return Stance.Supportive;
            if (friction >= 30 || warmth <= 0) return Stance.Distant;
            return jitter < 45 ? Stance.Measured : Stance.Supportive;
        }
        if (headline.Tone == FactTone.Positive)
        {
            if (friction >= 55 && p.Competitiveness >= 60) return Stance.Measured;
            if (ownBadGame && p.Competitiveness >= 70 && warmth < 40) return Stance.Distant;
            if ((p.Humor >= 60 || c.Humor >= 60) && warmth >= 25) return Stance.Joking;
            if (character.Type == CharacterType.Manager && p.Professionalism >= 72 && headline.Salience < 80) return Stance.Measured;
            if (headline.Salience >= 80) return warmth >= 20 ? Stance.Proud : Stance.Measured;
            if (ownGoodGame && p.Leadership >= 55) return Stance.Praise;
            if (warmth >= 30) return Stance.Praise;
            return jitter < 55 ? Stance.Measured : Stance.Praise;
        }
        if (narrative.Has("bench_streak") && character.Type == CharacterType.Manager) return Stance.Challenging;
        if (friction >= 50) return Stance.Distant;
        if (warmth >= 70 && (p.Humor >= 55 || c.Humor >= 55)) return Stance.Joking;
        if (state.Satisfaction <= 35 && p.Diplomacy < 55) return Stance.Frustrated;
        return Stance.Measured;
    }

    /// <summary>
    /// What this character actually talks about. Someone handing out criticism goes to the thing the player
    /// did wrong rather than the scoreline, which is what makes the criticism land as criticism.
    /// </summary>
    private static MatchFact Subject(MatchNarrative narrative, Stance stance)
    {
        if (stance is not (Stance.Critical or Stance.Frustrated or Stance.Challenging)) return narrative.Headline;
        foreach (var key in PlayerFaultKeys)
            if (narrative.Find(key) is { } fault) return fault;
        return narrative.Headline;
    }

    private static readonly string[] PlayerFaultKeys = ["red_card", "penalty_missed", "poor_display", "team_worst", "bench_streak", "below_the_team", "drought"];

    private static IReadOnlyDictionary<string, string> Slots(Character character, MatchNarrative narrative, MatchPerformance? mate, MatchFact subject)
    {
        var input = narrative.Input;
        var best = narrative.Squad.Where(x => x.Minutes >= 25 && x.Rating > 0).OrderByDescending(x => x.Rating).FirstOrDefault();
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["player"] = FirstName(narrative.Career.PlayerName),
            // An unnamed opponent, an unpublished score, and a missing rating leave their slot empty, which
            // makes lines built around them unusable. Nobody says "Score unknown" out loud.
            ["opponent"] = input.OpponentKnown ? input.Opponent : "",
            ["venue"] = input.IsHomeKnown ? input.IsHome ? "at home" : "away" : "in that one",
            ["competition"] = input.Competition,
            ["score"] = input.ScoreKnown ? input.ScoreLabel : "",
            ["rating"] = input.Rating > 0 ? input.Rating.ToString("0.#") : "",
            ["minutes"] = input.Minutes.ToString(),
            ["goals"] = input.Goals.ToString(),
            ["assists"] = input.Assists.ToString(),
            ["detail"] = subject.Detail,
            ["second"] = narrative.Facts.FirstOrDefault(x => x.Key != subject.Key)?.Detail ?? "",
            ["form"] = string.IsNullOrWhiteSpace(narrative.FormLine) ? "the recent run" : narrative.FormLine,
            ["drought"] = narrative.GoalDrought.ToString(),
            ["benched"] = (narrative.StartDrought + 1).ToString(),
            ["team"] = string.IsNullOrWhiteSpace(input.RepresentingTeam) ? narrative.Career.Club : input.RepresentingTeam,
            ["best"] = best?.Name ?? "the group",
            ["mine"] = mate is null ? "" : mate.Rating.ToString("0.#"),
            ["speaker"] = FirstName(character.Name),
            // Slots that resolve to an empty string make their template unusable, so lines written around a
            // run of form or a season tally simply do not appear when there is no run or tally to mention.
            ["run"] = narrative.UnbeatenRun >= 4 ? $"unbeaten in {narrative.UnbeatenRun}"
                : narrative.WinlessRun >= 3 ? $"still without a win in {narrative.WinlessRun}" : "",
            ["season"] = narrative.SeasonGoals > 0 && narrative.SeasonAppearances > 1
                ? $"{narrative.SeasonGoals} in {narrative.SeasonAppearances} this season" : "",
            ["mood"] = narrative.Player.Confidence >= 70 ? "flying" : narrative.Player.Confidence <= 35 ? "low on confidence" : ""
        };
    }

    private static string FirstName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? name : parts[0];
    }

    /// <summary>Picks the least recently used phrase from a pool, so long careers keep sounding fresh.</summary>
    private static string? Choose(IReadOnlyList<(string Key, string Text)> pool, IReadOnlySet<string> recent,
        List<string> used, long seed, IReadOnlyDictionary<string, string> slots, IReadOnlySet<string>? forbidden = null,
        bool strict = false)
    {
        if (pool.Count == 0) return null;
        var offset = (int)(seed % pool.Count);
        (string Key, string Text)? fallback = null;
        for (var i = 0; i < pool.Count; i++)
        {
            var candidate = pool[(offset + i) % pool.Count];
            if (used.Contains(candidate.Key)) continue;
            if (forbidden is not null && forbidden.Contains(candidate.Key)) continue;
            var filled = Fill(candidate.Text, slots);
            if (filled is null) continue;
            fallback ??= (candidate.Key, filled);
            if (recent.Contains(candidate.Key)) continue;
            used.Add(candidate.Key);
            return filled;
        }
        if (strict || fallback is null) return null;
        used.Add(fallback.Value.Key);
        return fallback.Value.Text;
    }

    /// <summary>Fills a template, or returns null when the line depends on a fact this match does not have.</summary>
    private static string? Fill(string template, IReadOnlyDictionary<string, string> slots)
    {
        if (!template.Contains('{')) return template;
        var result = template;
        foreach (var slot in slots)
        {
            var token = "{" + slot.Key + "}";
            if (!result.Contains(token, StringComparison.Ordinal)) continue;
            if (string.IsNullOrWhiteSpace(slot.Value)) return null;
            result = result.Replace(token, slot.Value);
        }
        return System.Text.RegularExpressions.Regex.Replace(result, @"\s{2,}", " ").Trim();
    }

    /// <summary>Each fragment is written to stand alone, so it starts with a capital and ends a sentence.</summary>
    private static string Polish(string part)
    {
        var value = part.Trim();
        if (value.Length == 0) return value;
        if (char.IsLower(value[0])) value = char.ToUpperInvariant(value[0]) + value[1..];
        return value[^1] is '.' or '!' or '?' ? value : value + ".";
    }

    private static long Seed(params object[] values)
    {
        unchecked
        {
            long hash = 2166136261;
            foreach (var value in values)
                foreach (var ch in value.ToString() ?? "") { hash ^= ch; hash *= 16777619; }
            return Math.Abs(hash);
        }
    }

    private readonly record struct Voice(Character Character, Relationship Relationship, MatchNarrative Narrative)
    {
        public bool UsesOpeners => Relationship.Familiarity >= 20 || Character.Type != CharacterType.Teammate;
    }
}
