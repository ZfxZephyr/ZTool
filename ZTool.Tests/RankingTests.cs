using Xunit;
using ZTool.Services;

namespace ZTool.Tests;

public class RankingTests
{
    [Fact]
    public void A_row_actually_named_jsb_outranks_the_pinyin_hit()
    {
        Assert.True(Ranking.Score("JSB", "jsb") > Ranking.ScorePinyin("jsb", "jsb"));
    }

    /// <summary>
    /// The notch must not push a pinyin hit under a mere name prefix: "jsb" is
    /// a deliberate keying of 「记事本」, not a weaker hint than "JSB…".
    /// </summary>
    [Fact]
    public void Pinyin_exact_still_outranks_a_plain_name_prefix()
    {
        Assert.True(Ranking.ScorePinyin("jsb", "jsb") > Ranking.Score("JSBuilder", "jsb"));
    }

    [Fact]
    public void Pinyin_lands_between_the_name_tiers_never_on_top_of_one()
    {
        var nameExact = Ranking.Score("jsb", "jsb");
        var pinyinExact = Ranking.ScorePinyin("jsb", "jsb");
        var namePrefix = Ranking.Score("JSBuilder", "jsb");
        var pinyinPrefix = Ranking.ScorePinyin("jishiben", "ji");
        var nameWordStart = Ranking.Score("工具 JSB", "jsb");

        Assert.True(nameExact > pinyinExact);
        Assert.True(pinyinExact > namePrefix);
        Assert.True(namePrefix > pinyinPrefix);
        Assert.True(pinyinPrefix > nameWordStart);
    }

    [Theory]
    [InlineData("记事本")]
    [InlineData("js1")]
    [InlineData("js-b")]
    [InlineData("")]
    public void Queries_that_are_not_plain_latin_letters_never_match_pinyin(string query)
    {
        Assert.Equal(0d, Ranking.ScorePinyin("jishiben", query));
    }
}
