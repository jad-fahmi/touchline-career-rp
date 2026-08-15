using CareerCompanion.Core.Persistence;

namespace CareerCompanion.Core.Services;

/// <summary>
/// Which language the football world is spoken in. A Spanish teammate answering in Spanish is realistic,
/// but a player who cannot read it loses the conversation, so English is the default and the native
/// language is something the player turns on. The setting is global, like the other generation toggles.
/// </summary>
public static class DialogueLanguage
{
    public const string SettingKey="native_language_dialogue";

    /// <summary>True when the player has asked characters to speak their own first language.</summary>
    public static bool NativeLanguagesEnabled(Database db)=>bool.TryParse(db.GetSetting(SettingKey),out var enabled)&&enabled;

    /// <summary>The sentence added to a system prompt so the model knows which language to write in.</summary>
    public static string Directive(bool nativeLanguages)=>nativeLanguages
        ?"A character may use their own first language when it fits: a greeting, an exclamation, an endearment, or a whole line to someone who shares it. Keep it natural and in character rather than decorating every line with it."
        :"Everyone in this world is speaking English to the player. Write every line in English, including greetings, slang, exclamations, and endearments, even for a character who would use another language in real life. Nationality belongs in rhythm, warmth, and turns of phrase, not in foreign words.";

    /// <summary>
    /// True when a reply reads as another language rather than as English. This only has to catch a reply
    /// that was written in the character's own language: a single borrowed word is not worth a regeneration,
    /// so a line is only refused when it is largely foreign, and English function words always argue for
    /// keeping it.
    /// </summary>
    public static bool ReadsAsAnotherLanguage(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return false;
        var letters=0;var otherScript=0;
        foreach(var c in text)
        {
            if(!char.IsLetter(c))continue;
            letters++;
            // Everything past Latin Extended-B is a different alphabet: Greek, Cyrillic, Hebrew, Arabic,
            // Devanagari, Thai, Hangul, and the CJK ranges all sit above it.
            if(c>'ʯ')otherScript++;
        }
        if(letters>=6&&otherScript*5>=letters)return true;

        var words=Words(text);
        if(words.Length<4)return false;
        var foreign=0;var english=0;
        foreach(var word in words)
        {
            if(ForeignMarkers.Contains(word))foreign++;
            else if(EnglishMarkers.Contains(word))english++;
        }
        if(foreign==0)return false;
        // A quarter of the line built from words English does not use is a line written in another language.
        if(foreign>=3&&foreign*4>=words.Length)return true;
        // Otherwise the absence of English is the evidence: ordinary English dialogue is full of function words.
        return words.Length>=6&&foreign>=2&&english*8<words.Length;
    }

    /// <summary>Splits a line into lowercase words, keeping the apostrophe so "c'est" stays one word.</summary>
    private static string[] Words(string text)
    {
        var buffer=new System.Text.StringBuilder(text.Length);
        foreach(var c in text)buffer.Append(char.IsLetter(c)||c=='\''||c=='’'?char.ToLowerInvariant(c=='’'?'\'':c):' ');
        return buffer.ToString().Split(' ',StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Everyday words from the languages a squad actually speaks. Anything that is also an English word
    /// ("no", "die", "war", "men", "come", "den") is deliberately absent, because one of those in an English
    /// sentence must never start counting towards a foreign reply.
    /// </summary>
    private static readonly HashSet<string> ForeignMarkers=new(StringComparer.Ordinal)
    {
        // Spanish and Portuguese
        "que","qué","porque","pero","cuando","muy","todo","todos","esto","esta","está","estás","estoy",
        "eres","soy","hoy","mañana","gracias","hermano","tío","vamos","siempre","nada","nunca","tienes",
        "tengo","hacer","mejor","ahora","así","sí","también","para","por","los","las","del","una","mucho",
        "bien","obrigado","irmão","você","muito","tudo","então","não","gente","cara","jogo","estamos",
        "vou","vai","meu","minha","jogar","amigo","chaval",
        // French
        "je","tu","nous","vous","c'est","j'ai","n'est","qu'il","est","très","mais","pour","avec","merci",
        "frère","mec","ça","oui","être","fait","faire","peux","veux","alors","toujours","jamais","comme",
        "cette","aussi","déjà","tout","rien","quoi","voilà",
        // Italian
        "che","sono","siamo","molto","grazie","fratello","ragazzi","bene","sempre","adesso","però","anche",
        "cosa","questo","niente","davvero","dai","forza","ciao","bello","tutto","perché",
        // German and Dutch
        "ich","nicht","und","das","aber","wir","mit","auch","sehr","immer","schon","danke","bruder","junge",
        "haben","kann","muss","gut","wieder","alles","bist","sind","der","dass","heute","echt","ja",
        "ik","het","niet","maar","goed","jongen","altijd","gewoon","dat","ook","heb","jij","wij","lekker",
        // Nordic, Polish, Turkish, and the Balkans
        "och","att","är","jag","inte","jeg","ikke","från","tack","bror","riktigt","hej","kompis",
        "jest","nie","tak","bardzo","dobrze","już","teraz","dzięki","stary","jak",
        "bir","çok","için","abi","kardeşim","evet","hayır","şimdi","tamam","kanka",
        "brate","hvala","dobro","kako","sada"
    };

    /// <summary>The English function words that show a line is English even when it borrows a word or two.</summary>
    private static readonly HashSet<string> EnglishMarkers=new(StringComparer.Ordinal)
    {
        "the","and","you","your","to","a","is","it","that","that's","of","in","for","we","i","i'm","was",
        "are","be","on","with","but","not","they","this","have","had","he","he's","she","she's","at","so",
        "just","get","got","do","don't","it's","you're","we're","didn't","can't","what","when","about",
        "like","all","my","me","him","her","us","them","from","if","as","out","up","there","how","now",
        "were","been","will","would","one","some","more","than","very","really","today","tomorrow","good",
        "great","keep","going","think","know","said","see","back","next","time","mate","lads","boys"
    };
}
