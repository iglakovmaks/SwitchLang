namespace SwitchLang;

internal enum KeyboardLayout
{
    Russian,
    English
}

internal static class KeyboardLayoutConverter
{
    private const string EnglishUnshifted = "`1234567890-=qwertyuiop[]\\asdfghjkl;'zxcvbnm,./";
    private const string RussianUnshifted = "ё1234567890-=йцукенгшщзхъ\\фывапролджэячсмитьбю.";
    private const string EnglishShifted = "~!@#$%^&*()_+QWERTYUIOP{}|ASDFGHJKL:\"ZXCVBNM<>?";
    private const string RussianShifted = "Ё!\"№;%:?*()_+ЙЦУКЕНГШЩЗХЪ/ФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,";
    private static readonly HashSet<char> Punctuation = new(",./<>?");

    private static readonly IReadOnlyDictionary<char, char> EnglishToRussian;
    private static readonly IReadOnlyDictionary<char, char> RussianToEnglish;

    static KeyboardLayoutConverter()
    {
        var englishToRussian = new Dictionary<char, char>();
        var russianToEnglish = new Dictionary<char, char>();

        foreach (var (source, target) in new[]
        {
            (EnglishUnshifted, RussianUnshifted),
            (EnglishShifted, RussianShifted)
        })
        {
            for (var index = 0; index < source.Length; index++)
            {
                var sourceCharacter = source[index];
                var targetCharacter = target[index];
                if (sourceCharacter == '?' || targetCharacter == '?')
                {
                    continue;
                }
                if (char.IsLetter(sourceCharacter) || char.IsLetter(targetCharacter) ||
                    Punctuation.Contains(sourceCharacter) || Punctuation.Contains(targetCharacter))
                    englishToRussian[sourceCharacter] = targetCharacter;
                    russianToEnglish[targetCharacter] = sourceCharacter;
            }
        }

        EnglishToRussian = englishToRussian;
        RussianToEnglish = russianToEnglish;
    }

    public static bool CanConvert(string text, KeyboardLayout layout)
    {
        var map = layout == KeyboardLayout.Russian ? RussianToEnglish : EnglishToRussian;
        return text.Any(character => map.ContainsKey(character));
    }

    public static string Convert(string text, KeyboardLayout layout)
    {
        var map = layout == KeyboardLayout.Russian ? RussianToEnglish : EnglishToRussian;
        return string.Concat(text.Select(character => map.TryGetValue(character, out var replacement)
            ? replacement
            : character));
    }
}
