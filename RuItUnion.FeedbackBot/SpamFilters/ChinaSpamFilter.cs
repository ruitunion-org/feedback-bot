using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Unicode;
using Microsoft.FeatureManagement.Mvc;

namespace RuItUnion.FeedbackBot.SpamFilters;

[FeatureGate("ChinaSpamFilter")]
public class ChinaSpamFilter : ISpamFilter
{
    private const double TRIGGER_PERCENT = 0.15;

    private static readonly (int start, int end)[] _nonBmpRanges =
    [
        (0x20000, 0x2A6DF), // Extension B
        (0x2A700, 0x2B73F), // Extension C
        (0x2B740, 0x2B81D), // Extension D
        (0x2B820, 0x2CEAF), // Extension E
        (0x2CEB0, 0x2EBE0), // Extension F
        (0x2EBF0, 0x2EE5F), // Extension I (Unicode 15.1)
        (0x2F800, 0x2FA1F), // Compatibility Supplement
        (0x30000, 0x3134F), // Extension G
        (0x31350, 0x323AF), // Extension H
        (0x323B0, 0x3347F), // Extension J (Unicode 15.1)
    ];

    public bool IsSpam(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        int count = 0;
        int cjkCount = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            count++;
            if (IsInRange(UnicodeRanges.CjkCompatibility, rune.Value)
                || IsInRange(UnicodeRanges.CjkCompatibilityForms, rune.Value)
                || IsInRange(UnicodeRanges.CjkCompatibilityIdeographs, rune.Value)
                || IsInRange(UnicodeRanges.CjkRadicalsSupplement, rune.Value)
                || IsInRange(UnicodeRanges.CjkStrokes, rune.Value)
                || IsInRange(UnicodeRanges.CjkUnifiedIdeographs, rune.Value)
                || IsInRange(UnicodeRanges.CjkUnifiedIdeographsExtensionA, rune.Value)
                || (!rune.IsBmp && _nonBmpRanges.Any(x => rune.Value >= x.start && rune.Value <= x.end)))
                cjkCount++;
        }

        return (double)cjkCount / count >= TRIGGER_PERCENT;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInRange(in UnicodeRange range, in int value) =>
        value >= range.FirstCodePoint && value < range.FirstCodePoint + range.Length;
}