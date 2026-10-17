using System;

namespace ZTool.Services;

/// <summary>
/// Ordering for a launcher has to be boring and predictable: exact name,
/// then prefix, then word start, then buried substring. Fuzzy matching is
/// noise at this scale and makes the top row jump around while you type.
/// </summary>
public static class Ranking
{
    /// <summary>
    /// How far a pinyin hit sits below a same-tier hit on the real name. The
    /// tiers are 200 apart, so a notch of 100 keeps the ladder readable:
    /// pinyin exact, then Han prefix, then pinyin prefix.
    /// </summary>
    private const double PinyinNotch = 100;

    /// <summary>Higher is better. Zero means no match at all.</summary>
    public static double Score(string text, string query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
        {
            return 0;
        }

        // Windows app names carry case by accident, never by meaning.
        if (text.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1000;
        }

        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 800 - text.Length * 0.1;
        }

        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return 0;
        }

        // A hit that starts a word reads as intentional; a mid-word hit does not.
        var onWordStart = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
        return (onWordStart ? 600 : 400) - index * 0.5 - text.Length * 0.05;
    }

    /// <summary>
    /// Scores a title's pinyin against a Latin query. Runs on the same ladder as
    /// the name itself, then pulled one notch down: typing "jsb" should find
    /// 「记事本」, but a row actually called "JSB" keeps the top slot. Chinese
    /// queries are left alone — they already have the ladder above.
    /// </summary>
    public static double ScorePinyin(string pinyin, string query)
    {
        if (!IsLatinLetters(query))
        {
            return 0;
        }

        var score = Score(pinyin, query);
        return score > PinyinNotch ? score - PinyinNotch : 0;
    }

    /// <summary>True for "jsb" or "jishiben", false for anything with digits or Han.</summary>
    public static bool IsLatinLetters(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var c in text)
        {
            if (c is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z'))
            {
                return false;
            }
        }

        return true;
    }
}
