using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZTool.Models;
using ZTool.Services;

namespace ZTool.Tests;

public class SearchTests
{
    [Theory]
    [InlineData("jsb", "记事本")]
    [InlineData("jishiben", "记事本")]
    [InlineData("sz", "设置")]
    [InlineData("jsq", "计算器")]
    public void Latin_query_finds_the_row_by_its_pinyin(string query, string expected)
    {
        var results = Desktop().Search(query);

        Assert.NotEmpty(results);
        Assert.Equal(expected, results[0].Title);
    }

    [Fact]
    public void A_row_actually_named_jsb_outranks_the_pinyin_hit()
    {
        var results = Desktop("JSB").Search("jsb");

        Assert.Equal("JSB", results[0].Title);
    }

    [Fact]
    public void A_chinese_query_still_ranks_the_real_name_first()
    {
        var results = Desktop().Search("记事本");

        Assert.Equal("记事本", results[0].Title);
    }

    private static SearchService Desktop(params string[] extra) =>
        new SearchService(new Stub(extra.Select(App).Concat(
            new[] { "记事本", "画图", "计算器", "设置" }.Select(App)).ToList()));

    private static SearchResult App(string title) => new()
    {
        Title = title,
        Kind = ResultKind.Application,
        PinyinFull = TitlePinyin.Full(title),
        PinyinInitials = TitlePinyin.Initials(title),
        Launch = static () => true,
    };

    private sealed class Stub(List<SearchResult> items) : IResultProvider
    {
        public IEnumerable<SearchResult> Candidates() => items;
    }
}
