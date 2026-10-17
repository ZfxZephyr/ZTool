using Xunit;
using ZTool.Services;

namespace ZTool.Tests;

public class TitlePinyinTests
{
    [Theory]
    [InlineData("记事本", "jishiben", "jsb")]
    [InlineData("画图", "huatu", "ht")]
    [InlineData("计算器", "jisuanqi", "jsq")]
    [InlineData("设置", "shezhi", "sz")]
    public void Han_titles_carry_full_pinyin_and_initials(string title, string full, string initials)
    {
        Assert.Equal(full, TitlePinyin.Full(title));
        Assert.Equal(initials, TitlePinyin.Initials(title));
    }

    /// <summary>
    /// The whole point of the phrase dictionary: 「重庆」 is chongqing and
    /// 「重量」 is zhongliang, not the same reading twice.
    /// </summary>
    [Theory]
    [InlineData("重庆", "chongqing", "cq")]
    [InlineData("重复", "chongfu", "cf")]
    [InlineData("重量", "zhongliang", "zl")]
    [InlineData("银行", "yinhang", "yh")]
    [InlineData("行业", "hangye", "hy")]
    [InlineData("行走", "xingzou", "xz")]
    [InlineData("自行车", "zixingche", "zxc")]
    public void Polyphones_read_as_phrases_not_as_single_characters(string title, string full, string initials)
    {
        Assert.Equal(full, TitlePinyin.Full(title));
        Assert.Equal(initials, TitlePinyin.Initials(title));
    }

    /// <summary>
    /// Only the Han runs are converted. Folding "Chrome" in would bury the
    /// initials people actually type under a prefix nobody does.
    /// </summary>
    [Theory]
    [InlineData("Chrome 浏览器", "liulanqi", "llq")]
    [InlineData("360安全卫士", "anquanweishi", "aqws")]
    [InlineData("Chrome", "", "")]
    [InlineData("", "", "")]
    public void Mixed_script_titles_convert_only_their_han_runs(string title, string full, string initials)
    {
        Assert.Equal(full, TitlePinyin.Full(title));
        Assert.Equal(initials, TitlePinyin.Initials(title));
    }
}
