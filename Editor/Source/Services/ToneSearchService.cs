using System.Diagnostics;

namespace Gp200.Replica;

internal sealed record CommunitySearchLink(string Title, string Url, string Platform);

internal static class ToneSearchService
{
    internal static string GetTone3000Url(string query) =>
        $"https://www.tone3000.com/search?q={Uri.EscapeDataString(query.Trim())}";

    internal static string GetToneHuntUrl(string query) =>
        $"https://tonehunt.org/search?q={Uri.EscapeDataString(query.Trim())}";

    internal static bool OpenSearch(string query, string platform = "tone3000")
    {
        string url = platform.ToLowerInvariant() switch
        {
            "tonehunt" => GetToneHuntUrl(query),
            _ => GetTone3000Url(query)
        };

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static IReadOnlyList<CommunitySearchLink> GenerateSearchSuggestions(string modelOrToneName)
    {
        if (string.IsNullOrWhiteSpace(modelOrToneName)) return Array.Empty<CommunitySearchLink>();
        string clean = modelOrToneName.Trim();

        return
        [
            new($"TONE3000: \"{clean}\"", GetTone3000Url(clean), "TONE3000"),
            new($"ToneHunt: \"{clean}\" (NAM)", GetToneHuntUrl(clean), "ToneHunt")
        ];
    }
}

