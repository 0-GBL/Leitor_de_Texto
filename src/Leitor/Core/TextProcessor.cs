using System.Text.RegularExpressions;

namespace Leitor.Core;

public static partial class TextProcessor
{
    // Offsets always refer to the unmodified original, including CRLF and whitespace.
    public static IReadOnlyList<Segment> Split(string text, CancellationToken token = default) => SplitCore(text, token, false);
    public static IReadOnlyList<Segment> SplitLegacy(string text, CancellationToken token = default) => SplitCore(text, token, true);
    private static IReadOnlyList<Segment> SplitCore(string text, CancellationToken token, bool legacy)
    {
        var result = new List<Segment>();
        int paragraph = 0;
        foreach (Match line in Lines().Matches(text))
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line.Value)) continue;
            string trimmed = line.Value.Trim();
            bool heading = trimmed.StartsWith('#') || (trimmed.Length < 85 && !".!?;,:".Contains(trimmed[^1]));
            int start = line.Index, end = line.Index + line.Length, part = start;
            while (part < end)
            {
                token.ThrowIfCancellationRequested();
                int limit = Math.Min(part + 420, end), cut = limit;
                for (int i = part; i < limit; i++)
                {
                    if (legacy)
                    {
                        if (".!?".Contains(text[i]) && (i + 1 == end || char.IsWhiteSpace(text[i + 1])) && !IsAbbreviation(text, part, i)) { cut = i + 1; break; }
                        continue;
                    }
                    if (!".!?…".Contains(text[i])) continue;
                    int boundary = i + 1;
                    while (boundary < end && ".!?…\"'”’»)]}".Contains(text[boundary])) boundary++;
                    if ((boundary == end || char.IsWhiteSpace(text[boundary])) &&
                        !(text[i] == '.' && boundary == i + 1 && IsAbbreviation(text, part, i)))
                    { cut = boundary; break; }
                }
                if (cut == limit && limit < end)
                {
                    int space = text.LastIndexOf(' ', limit - 1, limit - part);
                    if (space > part) cut = space + 1;
                    if (cut < text.Length && cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;
                }
                while (cut < end && char.IsWhiteSpace(text[cut])) cut++;
                if (!text.AsSpan(part, cut - part).IsWhiteSpace())
                    result.Add(new(result.Count, part, cut - part, paragraph, heading, cut == end, text[part..cut]));
                part = cut;
            }
            paragraph++;
        }
        return result;
    }
    private static bool IsAbbreviation(string text, int start, int dot)
    {
        int p = dot - 1;
        while (p >= start && char.IsLetter(text[p])) p--;
        string word = text[(p + 1)..dot].ToLowerInvariant();
        return word is "sr" or "sra" or "dr" or "dra" or "prof" or "profa" or "etc" or "ex" || word.Length == 1;
    }
    [GeneratedRegex(@"[^\r\n]+", RegexOptions.CultureInvariant)] private static partial Regex Lines();
    public static double Estimate(Segment s, SpeechOptions o) => Math.Max(.3, s.Text.Length / (14.0 * o.Speed)) + Pause(s, o);
    public static double Pause(Segment s, SpeechOptions o) => s.Heading ? o.HeadingPause : s.ParagraphEnd ? o.ParagraphPause : EndsSentence(s.Text) ? o.SentencePause : 0;
    public static bool EndsSentence(string text)
    {
        var end = text.AsSpan().TrimEnd();
        while (!end.IsEmpty && "\"'”’»)]}".Contains(end[^1])) end = end[..^1].TrimEnd();
        return !end.IsEmpty && ".!?…".Contains(end[^1]);
    }
}
