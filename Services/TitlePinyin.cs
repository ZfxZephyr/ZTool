using System;
using System.Text;
using ToolGood.Words.Pinyin;

namespace ZTool.Services;

/// <summary>
/// A second index for a title, spelled in Latin: the full pinyin and the
/// initial letters. Only the Han runs are converted — in 「Chrome 浏览器」 the
/// word "Chrome" is already matched by the title ladder, and folding it into
/// the pinyin would bury the initials under it.
/// </summary>
public static class TitlePinyin
{
    /// <summary>Full pinyin, e.g. 「记事本」 → "jishiben". Empty for a title with no Han.</summary>
    public static string Full(string title) => Build(title, s => WordsHelper.GetPinyin(s));

    /// <summary>Initials, e.g. 「记事本」 → "jsb". Empty for a title with no Han.</summary>
    public static string Initials(string title) => Build(title, s => WordsHelper.GetFirstPinyin(s));

    private static string Build(string title, Func<string, string> convert)
    {
        if (string.IsNullOrEmpty(title))
        {
            return string.Empty;
        }

        var result = new StringBuilder(title.Length);
        var run = new StringBuilder();

        foreach (var c in title)
        {
            if (IsHan(c))
            {
                run.Append(c);
                continue;
            }

            Flush(run, convert, result);
        }

        Flush(run, convert, result);

        return result.ToString();
    }

    private static void Flush(StringBuilder run, Func<string, string> convert, StringBuilder result)
    {
        if (run.Length == 0)
        {
            return;
        }

        // The library reads a phrase at a time so 「重庆」 stays chongqing and
        // 「重量」 stays zhongliang. One call per run is what buys that.
        result.Append(convert(run.ToString()).ToLowerInvariant());
        run.Clear();
    }

    private static bool IsHan(char c) =>
        c is >= '一' and <= '鿿'   // CJK Unified Ideographs
            or >= '㐀' and <= '䶿';  // Extension A
}
