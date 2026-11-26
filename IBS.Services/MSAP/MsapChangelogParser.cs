using System.Text.RegularExpressions;

namespace IBS.Services.MSAP
{
    public sealed record MsapChangelogItem(string Category, string? Title, string Text);

    public sealed record MsapChangelogRelease(string Version, string? Date, IReadOnlyList<MsapChangelogItem> Items);

    public static partial class MsapChangelogParser
    {
        private const string ChangelogFileName = "MSAP_CHANGELOG.md";

        public static async Task<IReadOnlyList<MsapChangelogRelease>> ReadReleasesAsync(
            string contentRootPath,
            CancellationToken ct = default)
        {
            var path = ResolveChangelogPath(contentRootPath);
            if (path == null)
            {
                return Array.Empty<MsapChangelogRelease>();
            }

            var markdown = await File.ReadAllTextAsync(path, ct);
            return Parse(markdown);
        }

        public static IReadOnlyList<MsapChangelogRelease> Parse(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return Array.Empty<MsapChangelogRelease>();
            }

            var releases = new List<MsapChangelogRelease>();
            string? currentVersion = null;
            string? currentDate = null;
            var currentItems = new List<MsapChangelogItem>();
            var currentCategory = "Updated";

            var lines = markdown.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    var releaseMatch = ReleaseHeadingRegex().Match(line);
                    if (!releaseMatch.Success)
                    {
                        continue;
                    }

                    if (currentVersion != null && currentItems.Count > 0)
                    {
                        releases.Add(new MsapChangelogRelease(currentVersion, currentDate, currentItems.ToArray()));
                    }

                    currentVersion = releaseMatch.Groups["version"].Value.Trim();
                    var parsedDate = releaseMatch.Groups["date"].Value.Trim();
                    currentDate = string.IsNullOrEmpty(parsedDate) ? null : parsedDate;
                    currentItems = new List<MsapChangelogItem>();
                    currentCategory = "Updated";
                    continue;
                }

                if (currentVersion == null)
                {
                    continue;
                }

                if (line.StartsWith("### ", StringComparison.Ordinal))
                {
                    currentCategory = line[4..].Trim();
                    continue;
                }

                if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
                {
                    var content = line[2..].Trim();
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        continue;
                    }

                    var boldMatch = BoldPrefixRegex().Match(content);
                    if (boldMatch.Success)
                    {
                        var title = boldMatch.Groups["title"].Value.Trim().TrimEnd(':');
                        var text = boldMatch.Groups["text"].Value.Trim();
                        currentItems.Add(new MsapChangelogItem(currentCategory, title, text));
                    }
                    else
                    {
                        currentItems.Add(new MsapChangelogItem(currentCategory, null, content));
                    }
                }
            }

            if (currentVersion != null && currentItems.Count > 0)
            {
                releases.Add(new MsapChangelogRelease(currentVersion, currentDate, currentItems.ToArray()));
            }

            return releases;
        }

        private static string? ResolveChangelogPath(string contentRootPath)
        {
            var candidates = new[]
            {
                Path.Combine(contentRootPath, ChangelogFileName),
                Path.Combine(AppContext.BaseDirectory, ChangelogFileName),
                Path.GetFullPath(Path.Combine(contentRootPath, "..", ChangelogFileName))
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        [GeneratedRegex(@"^##\s+\[?v?(?<version>[^\]\s]+)\]?(?:\s*[-–—]\s*(?<date>.+))?$", RegexOptions.CultureInvariant)]
        private static partial Regex ReleaseHeadingRegex();

        [GeneratedRegex(@"^\*\*(?<title>[^*]+)\*\*\s*:?\s*(?<text>.+)$", RegexOptions.CultureInvariant)]
        private static partial Regex BoldPrefixRegex();
    }
}
