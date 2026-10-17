using System;
using System.IO;
using Xunit;
using ZTool.Services;

namespace ZTool.Tests;

public class ClipboardStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "ztool-tests", Guid.NewGuid().ToString("N"), "clipboard.db");

    public void Dispose()
    {
        try
        {
            var folder = Path.GetDirectoryName(_path);
            if (folder is not null && Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover scratch database in %TEMP% is not worth a test failure.
        }
    }

    [Fact]
    public void Copying_the_same_thing_twice_makes_one_row_and_moves_it_up()
    {
        using var store = new ClipboardStore(_path);

        store.Upsert(ClipboardKind.Text, "first", "first", Hash("first"), "Code", 1000);
        Tick();
        store.Upsert(ClipboardKind.Text, "second", "second", Hash("second"), "Code", 1000);
        Tick();

        // "first" is then wanted again — it becomes newest, not a third row.
        var again = store.Upsert(ClipboardKind.Text, "first", "first", Hash("first"), "Code", 1000);

        var rows = store.Query(string.Empty, -1);

        Assert.Equal(2, rows.Count);
        Assert.Equal("first", rows[0].Content);
        Assert.Equal(again.Id, rows[0].Id);
    }

    [Fact]
    public void Capacity_drops_the_oldest_unpinned_rows()
    {
        using var store = new ClipboardStore(_path);

        for (var i = 0; i < 12; i++)
        {
            var text = $"copy {i}";
            store.Upsert(ClipboardKind.Text, text, text, Hash(text), "Code", capacity: 10);
        }

        var rows = store.Query(string.Empty, -1);

        Assert.Equal(10, rows.Count);
        Assert.DoesNotContain(rows, r => r.Content == "copy 0");
        Assert.DoesNotContain(rows, r => r.Content == "copy 1");
        Assert.Contains(rows, r => r.Content == "copy 11");
    }

    [Fact]
    public void Pinned_rows_are_never_the_ones_that_go()
    {
        using var store = new ClipboardStore(_path);

        var held = store.Upsert(ClipboardKind.Text, "keep me", "keep me", Hash("keep me"), "Code", capacity: 5);
        store.SetPinned(held.Id, true);

        for (var i = 0; i < 12; i++)
        {
            var text = $"copy {i}";
            store.Upsert(ClipboardKind.Text, text, text, Hash(text), "Code", capacity: 5);
        }

        var rows = store.Query(string.Empty, -1);

        Assert.Contains(rows, r => r.Content == "keep me");
        Assert.Contains(rows, r => r.Pinned);
    }

    [Fact]
    public void A_pinned_table_is_allowed_to_sit_over_capacity()
    {
        using var store = new ClipboardStore(_path);

        for (var i = 0; i < 6; i++)
        {
            var text = $"held {i}";
            var row = store.Upsert(ClipboardKind.Text, text, text, Hash(text), "Code", capacity: 5);
            store.SetPinned(row.Id, true);
        }

        // Nothing is disposable, so nothing is disposed. Pinning is a decision.
        Assert.Equal(6, store.Query(string.Empty, -1).Count);
    }

    [Fact]
    public void History_is_still_there_after_the_app_would_have_restarted()
    {
        using (var store = new ClipboardStore(_path))
        {
            store.Upsert(ClipboardKind.Text, "survivor", "survivor", Hash("survivor"), "Code", 1000);
        }

        using var reopened = new ClipboardStore(_path);

        var rows = reopened.Query(string.Empty, -1);

        Assert.Single(rows);
        Assert.Equal("survivor", rows[0].Content);
    }

    [Fact]
    public void Search_looks_inside_the_content_not_just_the_preview()
    {
        using var store = new ClipboardStore(_path);

        store.Upsert(
            ClipboardKind.Text,
            "the quick brown fox jumps over the lazy dog",
            "the quick brown fox jumps over the lazy dog",
            Hash("x"),
            "Code",
            1000);

        Assert.Single(store.Query("lazy", -1));
        Assert.Empty(store.Query("zebra", -1));
    }

    /// <summary>Trailing CRLF is what Windows adds; the words in the middle are the copy.</summary>
    [Theory]
    [InlineData("hello\r\n", "hello")]
    [InlineData("hello", "hello")]
    [InlineData("hello\r\nworld\r\n", "hello\r\nworld")]
    [InlineData("hello  ", "hello")]
    [InlineData("  hello  ", "  hello")]
    [InlineData("  hello\r\n  world  ", "  hello\r\n  world")]
    public void Only_the_trailing_whitespace_is_dropped(string raw, string expected)
    {
        Assert.Equal(expected, ClipboardService.Normalize(raw));
    }

    /// <summary>Timestamps have millisecond resolution; two copies in the same one are indistinguishable.</summary>
    private static void Tick() => System.Threading.Thread.Sleep(5);

    private static string Hash(string value) => value;

    [Fact]
    public void Process_names_are_matched_however_they_are_written()
    {
        Assert.True(ProcessExclusions.IsExcluded("KeePass"));
        Assert.True(ProcessExclusions.IsExcluded("keepass.exe"));
        Assert.True(ProcessExclusions.IsExcluded(@"C:\Program Files\KeePass\KeePass.exe"));
        Assert.False(ProcessExclusions.IsExcluded("KeePassXCustom"));
        Assert.False(ProcessExclusions.IsExcluded("Code"));
        Assert.False(ProcessExclusions.IsExcluded(null));
    }
}
