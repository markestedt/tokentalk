using Microsoft.EntityFrameworkCore;

namespace TokenTalk.Storage;

public class DictationRepository
{
    private readonly TokenTalkDbContext _db;

    public DictationRepository(TokenTalkDbContext db)
    {
        _db = db;
    }

    public async Task SaveAsync(Dictation dictation, CancellationToken ct = default)
    {
        _db.Dictations.Add(dictation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<(List<Dictation> Items, int Total)> GetHistoryAsync(
        int limit, int offset, CancellationToken ct = default)
    {
        var total = await _db.Dictations.CountAsync(ct);
        var items = await _db.Dictations
            .OrderByDescending(d => d.Timestamp)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var dictation = await _db.Dictations.FindAsync([id], ct);
        if (dictation == null)
            throw new KeyNotFoundException($"Dictation {id} not found");

        _db.Dictations.Remove(dictation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<OverallStats> GetOverallStatsAsync(int days, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        var query = _db.Dictations.Where(d => d.Timestamp >= since);

        var total = await query.CountAsync(ct);
        if (total == 0)
            return new OverallStats();

        return new OverallStats
        {
            TotalDictations = total,
            TotalWords = await query.SumAsync(d => d.WordCount, ct),
            TotalCharacters = await query.SumAsync(d => d.CharacterCount, ct),
            SuccessCount = await query.CountAsync(d => d.Success, ct),
            FailureCount = await query.CountAsync(d => !d.Success, ct),
            AvgRecordingMs = await query.AverageAsync(d => (double)d.RecordingDurationMs, ct),
            AvgTranscriptionMs = await query.AverageAsync(d => (double)d.TranscriptionLatencyMs, ct),
            AvgInjectionMs = await query.AverageAsync(d => (double)d.InjectionLatencyMs, ct),
            AvgTotalLatencyMs = await query.AverageAsync(d => (double)d.TotalLatencyMs, ct),
            TotalRecordingTimeMs = await query.SumAsync(d => d.RecordingDurationMs, ct),
            TotalAudioSizeBytes = await query.SumAsync(d => d.AudioSizeBytes, ct),
        };
    }

    public async Task<List<DailyStats>> GetDailyStatsAsync(int days, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        // Group in SQL, format date on the client to avoid EF translation issues
        var rows = await _db.Dictations
            .Where(d => d.Timestamp >= since)
            .GroupBy(d => d.Timestamp.Date)
            .Select(g => new
            {
                Date = g.Key,
                TotalDictations = g.Count(),
                TotalWords = g.Sum(d => d.WordCount),
                SuccessCount = g.Count(d => d.Success),
                FailureCount = g.Count(d => !d.Success),
            })
            .OrderByDescending(s => s.Date)
            .ToListAsync(ct);

        return rows.Select(r => new DailyStats
        {
            Date = r.Date.ToString("yyyy-MM-dd"),
            TotalDictations = r.TotalDictations,
            TotalWords = r.TotalWords,
            SuccessCount = r.SuccessCount,
            FailureCount = r.FailureCount,
        }).ToList();
    }

    public async Task<List<ProviderStats>> GetProviderStatsAsync(int days, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        var results = await _db.Dictations
            .Where(d => d.Timestamp >= since)
            .GroupBy(d => d.Provider)
            .Select(g => new ProviderStats
            {
                Provider = g.Key,
                TotalDictations = g.Count(),
                TotalWords = g.Sum(d => d.WordCount),
                SuccessCount = g.Count(d => d.Success),
                FailureCount = g.Count(d => !d.Success),
                AvgLatencyMs = g.Average(d => (double)d.TotalLatencyMs),
            })
            .OrderByDescending(s => s.TotalDictations)
            .ToListAsync(ct);

        return results;
    }

    public async Task<List<HeatmapStats>> GetHeatmapStatsAsync(CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddDays(-365);
        var rows = await _db.Dictations
            .Where(d => d.Timestamp >= since)
            .GroupBy(d => d.Timestamp.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .OrderBy(s => s.Date)
            .ToListAsync(ct);

        return rows.Select(r => new HeatmapStats
        {
            Date = r.Date.ToString("yyyy-MM-dd"),
            Count = r.Count,
        }).ToList();
    }

    public async Task<List<WordFrequencyEntry>> GetWordFrequenciesAsync(
        int? days, int topN = 100, CancellationToken ct = default)
    {
        IQueryable<Dictation> query = _db.Dictations.Where(d => d.Success);
        if (days.HasValue)
        {
            var since = DateTime.UtcNow.AddDays(-days.Value);
            query = query.Where(d => d.Timestamp >= since);
        }

        var texts = await query
            .Select(d => d.TranscribedText)
            .ToListAsync(ct);

        var wordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            foreach (var word in TokenizeWords(text))
            {
                if (StopWords.Contains(word)) continue;
                wordCounts[word] = wordCounts.GetValueOrDefault(word) + 1;
            }
        }

        return wordCounts
            .OrderByDescending(kv => kv.Value)
            .Take(topN)
            .Select(kv => new WordFrequencyEntry { Word = kv.Key, Count = kv.Value })
            .ToList();
    }

    private static IEnumerable<string> TokenizeWords(string text)
    {
        var word = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                word.Append(char.ToLowerInvariant(ch));
            }
            else if (ch == '\'' || ch == '\u2019') // apostrophe / right single quote — transparent inside a word
            {
                // don't break the current word (don't → dont, can't → cant)
            }
            else if (word.Length > 0)
            {
                if (word.Length > 1) yield return word.ToString();
                word.Clear();
            }
        }
        if (word.Length > 1) yield return word.ToString();
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","about","above","after","again","against","all","am","an","and","any","are","arent",
        "as","at","be","because","been","before","being","below","between","both","but","by",
        "can","cant","cannot","could","couldnt","did","didnt","do","does","doesnt","doing","dont",
        "down","during","each","few","for","from","further","get","gets","got","had","hadnt",
        "has","hasnt","have","havent","having","he","hed","hell","hes","her","here","heres",
        "hers","herself","him","himself","his","how","hows","if","im","in","into","is","isnt",
        "it","its","itself", "ive","just","lets","me","more","most","mustnt","my","myself","no","nor",
        "not","of","off","on","once","only","or","other","ought","our","ours","ourselves",
        "out","over","own","same","shant","she","shed","shell","shes","should","shouldnt",
        "so","some","such","than","that","thats","the","their","theirs","them","themselves",
        "then","there","theres","these","they","theyd","theyll","theyre","theyve","this",
        "those","through","to","too","under","until","up","very","was","wasnt","we","wed",
        "well","were","weve","werent","what","whats","when","whens","where","wheres","which",
        "while","who","whos","whom","why","whys","will","with","wont","would","wouldnt",
        "you","youd","youll","youre","youve","your","yours","yourself","yourselves",
        "also","ok","okay","yeah","yes","now","really","actually","like","just","even",
        "still","already","maybe","probably","actually","um","uh","hmm","right","oh",
        "hey","hi","hello","please","thank","thanks","sorry","yes","nope","yep",
        "want","make","need","know","think","go","going","come","coming","use",
        "used","using","thing","things","way","ways","time","times","lot","lots",
        "something","anything","everything","nothing","someone","anyone","everyone",
        "new","good","great","nice","bad","big","little","old","long","short",
        "able","back","been","call","case","give","here","high","keep","last",
        "look","made","many","much","must","next","open","over","part","place",
        "point","put","said","same","see","seem","show","side","such","take",
        "them","then","two","well","work","world","year","years","day","days",
        "week","weeks","month","months","ago","around","every","always","never",
    };
}
