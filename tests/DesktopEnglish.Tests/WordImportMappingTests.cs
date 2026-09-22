using System.IO;
using System.Text;
using DesktopEnglish.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DesktopEnglish.Tests;

/// <summary>
/// Verifies the import mapping rules: BOM handling, quoted commas, level clamping and part-of-speech
/// extraction from the Chinese definition prefix.
/// </summary>
public class WordImportMappingTests
{
    private static WordImportService CreateImporter() => new(
        new StubDbContextFactory(null!),
        new NullExampleProvider(),
        new FixedClock(),
        new NoOpLogger<WordImportService>());

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("1", 1)]
    [InlineData("1.0", 1)]
    [InlineData("5.0", 5)]
    [InlineData("10.0", 10)]
    public void TryMapLevel_keeps_valid_scores(string? raw, int expected)
    {
        Assert.True(WordImportMapping.TryMapLevel(raw, out var level));
        Assert.Equal(expected, level);
    }

    [Fact]
    public void TryMapLevel_clamps_out_of_range_values()
    {
        Assert.True(WordImportMapping.TryMapLevel("0", out var low));
        Assert.Equal(1, low);

        Assert.True(WordImportMapping.TryMapLevel("99", out var high));
        Assert.Equal(10, high);
    }

    [Theory]
    [InlineData("n. 协议；  vt. 授予；  v. （与...）一致，符合", "n. / vt. / v.")]
    [InlineData("adj. 偶然的，意外的；非本质的；附属的；  n. 偶然", "adj. / n.")]
    [InlineData("/ˈælɔɪ/, /əˈlɔɪ/", "")]
    [InlineData(null, "")]
    public void ExtractPartOfSpeech_collects_distinct_prefixes(string? meaning, string expected)
    {
        Assert.Equal(expected, WordImportMapping.ExtractPartOfSpeech(meaning));
    }

    [Fact]
    public async Task Csv_import_handles_bom_headers_and_quoted_commas()
    {
        var csv = new StringBuilder();
        csv.AppendLine("\ufefftopic_id,单词,音标,中文释义,词频");
        csv.AppendLine("27,acceleration,/ekˌseləˈreɪʃn/,n. 加速；促进；加速度,9.0");
        csv.AppendLine("149,alloy,\"/ˈælɔɪ/, /əˈlɔɪ/\",v. 使成合金；  n. 合金,9.0");
        csv.AppendLine("58,additional,/əˈdɪʃənl/,adj. 增加的；附加的；额外的,6.0");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv.ToString()));
        var importer = CreateImporter();

        var words = await importer.ParseAsync(stream, ImportFormat.Csv, CancellationToken.None);

        Assert.Equal(3, words.Count);
        Assert.Equal("acceleration", words[0].Word);
        Assert.Equal(9, words[0].Level);
        Assert.Equal("n.", words[0].PartOfSpeech);

        // The phonetic column contains a comma inside quotes, so it must survive the row split.
        Assert.Equal("/ˈælɔɪ/, /əˈlɔɪ/", words[1].Phonetic);
        Assert.Equal("v. / n.", words[1].PartOfSpeech);

        Assert.Equal(6, words[2].Level);
        Assert.Equal("adj.", words[2].PartOfSpeech);
    }

    [Fact]
    public async Task Json_import_maps_words_and_levels()
    {
        const string json = """
        [
          { "word": "abandon", "phonetic": "/əˈbændən/", "meaning": "v. 放弃；抛弃", "level": 4 },
          { "word": "serendipity", "phonetic": "/ˌserənˈdɪpəti/", "meaning": "n. 机缘巧合", "level": 99 }
        ]
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var importer = CreateImporter();

        var words = await importer.ParseAsync(stream, ImportFormat.Json, CancellationToken.None);

        Assert.Equal(2, words.Count);
        Assert.Equal("abandon", words[0].Word);
        Assert.Equal("v.", words[0].PartOfSpeech);
        Assert.Equal(4, words[0].Level);
        Assert.Equal(10, words[1].Level);
    }
}

