using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using BibleFTS.Api.Models;
using Nest;
using Elasticsearch.Net;

namespace BibleFTS.Api.Services;

public class BibleSeeder
{
    private readonly IElasticClient _elastic;

    public BibleSeeder(IElasticClient elastic)
    {
        _elastic = elastic;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await EnsureIndexAsync(ct);

        using var stream = await OpenVplZipAsync(ct);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".vpl", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"VPL file not found inside zip. Entries: {string.Join(", ", zip.Entries.Select(e => e.FullName))}");
        using var vpl = entry.Open();

        using var reader = new StreamReader(vpl, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 16);
        var rx = new Regex(@"^(?<book>[^\s]+)\s+(?<chapter>\d+):(?<verse>\d+)\s+(?<text>.+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        var batch = new List<Verse>(1000);
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            line = line.Trim();
            if (line.Length == 0) continue;

            var m = rx.Match(line);
            if (!m.Success)
            {
                throw new FormatException($"Line not matched by regex: '{line}'");
            }

            batch.Add(new Verse
            {
                Book = m.Groups["book"].Value.Trim(),
                Chapter = int.Parse(m.Groups["chapter"].Value),
                VerseNumber = int.Parse(m.Groups["verse"].Value),
                Text = m.Groups["text"].Value.Trim()
            });

            if (batch.Count >= 1000)
            {
                await BulkIndexAsync(batch, ct);
                batch.Clear();
            }
        }
        if (batch.Count > 0) await BulkIndexAsync(batch, ct);
    }

    private async Task EnsureIndexAsync(CancellationToken ct)
    {
        var exists = await _elastic.Indices.ExistsAsync("bible", ct: ct);
        if (exists.Exists)
            await _elastic.Indices.DeleteAsync("bible", ct: ct);

        // var create = await _elastic.Indices.CreateAsync("bible", c => c
        //     .Settings(s => s
        //         .Analysis(a => a
        //             .Analyzers(ad => ad
        //                 .Custom("pt_analyzer", ca => ca
        //                     .Tokenizer("standard")
        //                     .Filters("lowercase", "asciifolding", "portuguese_stem", "portuguese_stop")
        //                 )
        //             )
        //             .TokenFilters(tf => tf
        //                 .Stop("portuguese_stop", st => st.StopWords("_portuguese_"))
        //                 .Stemmer("portuguese_stem", st => st.Language("light_portuguese"))
        //             )
        //         )
        //     )
        //     .Map<Verse>(m => m.Properties(p => p
        //         .Text(t => t.Name(v => v.Text).Analyzer("pt_analyzer").SearchAnalyzer("pt_analyzer"))
        //         .Keyword(k => k.Name(v => v.Book).IgnoreAbove(256))
        //         .Number(n => n.Name(v => v.Chapter).Type(NumberType.Integer))
        //         .Number(n => n.Name(v => v.VerseNumber).Type(NumberType.Integer))
        //     ))
        // , ct: ct);
        var create = await _elastic.Indices.CreateAsync("bible", c => c
            .Map<Verse>(m => m.Properties(p => p
                .Text(t => t.Name(v => v.Text))                // text = analisado padrão
                .Keyword(k => k.Name(v => v.Book).IgnoreAbove(256))
                .Number(n => n.Name(v => v.Chapter).Type(NumberType.Integer))
                .Number(n => n.Name(v => v.VerseNumber).Type(NumberType.Integer))
            ))
        , ct: ct);

        if (!create.IsValid)
            throw new Exception($"Failed to create index: {create.OriginalException?.Message}");
    }

    private Task<Stream> OpenVplZipAsync(CancellationToken ct)
    {
        var localPath = Path.Combine(AppContext.BaseDirectory, "data", "bliv-n4_vpl.zip");
        if (!File.Exists(localPath))
            throw new FileNotFoundException($"VPL zip not found at {localPath}. Make sure the file exists and the volume is mounted.");

        Stream fs = File.OpenRead(localPath);
        return Task.FromResult(fs);
    }

    private async Task BulkIndexAsync(List<Verse> batch, CancellationToken ct)
    {
        var resp = await _elastic.BulkAsync(b => b
            .Index("bible")
            .Refresh(Refresh.True) // útil em dev
            .IndexMany(batch), ct: ct);

        if (resp.Errors)
            throw new Exception($"Bulk index had errors. First: {resp.ItemsWithErrors.FirstOrDefault()?.Error?.Reason}");
    }
}
