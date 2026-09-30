using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Angband.Avalonia.Services;

/// <summary>What a check for updates found.</summary>
public enum UpdateStatus
{
    /// <summary>This build is the newest release's commit.</summary>
    UpToDate,
    /// <summary>The newest release is ahead of this build.</summary>
    NewerAvailable,
    /// <summary>This build is ahead of the newest release (built from newer source).</summary>
    LocalIsNewer,
    /// <summary>This build and the release have each moved on from a common commit.</summary>
    Diverged,
    /// <summary>This build's commit is unknown, or not on GitHub (a local build).</summary>
    UnknownLocal,
    /// <summary>GitHub couldn't be asked (offline, rate-limited...).</summary>
    Failed,
}

/// <param name="Status">What was found.</param>
/// <param name="Message">A line for the message bar.</param>
/// <param name="ReleaseUrl">The newest release's page, if there is one.</param>
public sealed record UpdateCheck(UpdateStatus Status, string Message, string? ReleaseUrl = null);

/// <summary>
/// Help → Check for updates: asks GitHub for the newest AVABand release and compares its commit
/// with the one this build was made from (the SDK stamps it into the informational version,
/// "1.0.0+&lt;sha&gt;"). Releases are the rolling "latest" pre-release of main plus any v* tags,
/// so commits, not version numbers, say which is newer. Only runs when asked; sends nothing
/// but the requests themselves.
/// </summary>
public sealed class UpdateChecker(HttpClient http, string repository = UpdateChecker.Repository)
{
    public const string Repository = "Harlock123/AVABand";
    private const string Api = "https://api.github.com/repos/";

    /// <summary>The commit this build was made from, if the build recorded it.</summary>
    public static string? BuildCommit(Assembly? assembly = null)
    {
        var version = (assembly ?? typeof(UpdateChecker).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var plus = version?.IndexOf('+') ?? -1;
        if (plus < 0) return null;
        var sha = version![(plus + 1)..];
        return sha.Length >= 7 && sha.All(Uri.IsHexDigit) ? sha : null;
    }

    public static UpdateChecker CreateDefault()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AVABand-update-check");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return new UpdateChecker(http);
    }

    public async Task<UpdateCheck> CheckAsync(string? localCommit, CancellationToken cancel = default)
    {
        try
        {
            using var releases = await GetJson($"releases?per_page=10", cancel);
            var newest = releases?.RootElement.EnumerateArray()
                .Where(r => !r.GetProperty("draft").GetBoolean())
                .OrderByDescending(r => r.GetProperty("published_at").GetDateTimeOffset())
                .Select(r => (Tag: r.GetProperty("tag_name").GetString()!, Name: r.GetProperty("name").GetString(),
                              Url: r.GetProperty("html_url").GetString()!, Published: r.GetProperty("published_at").GetDateTimeOffset(),
                              Assets: r.GetProperty("assets").EnumerateArray().Select(a => a.GetProperty("name").GetString()!).ToList()))
                .Cast<(string Tag, string? Name, string Url, DateTimeOffset Published, List<string> Assets)?>()
                .FirstOrDefault();
            if (newest is not { } release) return new(UpdateStatus.Failed, "No AVABand release was found on GitHub.");

            using var commit = await GetJson($"commits/{Uri.EscapeDataString(release.Tag)}", cancel);
            var remote = commit?.RootElement.GetProperty("sha").GetString();
            var built = $"built {release.Published.ToLocalTime():yyyy-MM-dd}";
            var download = AssetFor(release.Assets) is { } asset ? $" (download {asset})" : "";
            if (remote is null) return new(UpdateStatus.Failed, "GitHub didn't say which commit the newest release is.", release.Url);
            if (localCommit is null)
                return new(UpdateStatus.UnknownLocal, $"This build doesn't record its commit, so it can't be compared. The newest release was {built}{download}.", release.Url);
            if (remote.StartsWith(localCommit, StringComparison.OrdinalIgnoreCase) || localCommit.StartsWith(remote, StringComparison.OrdinalIgnoreCase))
                return new(UpdateStatus.UpToDate, $"AVABand is up to date (build {Short(localCommit)}, {built}).", release.Url);

            using var compare = await GetJson($"compare/{localCommit}...{remote}", cancel);
            if (compare is null)
                return new(UpdateStatus.UnknownLocal, $"This build ({Short(localCommit)}) isn't on GitHub — a local build? The newest release is {Short(remote)}, {built}{download}.", release.Url);
            var status = compare.RootElement.GetProperty("status").GetString();
            var ahead = compare.RootElement.GetProperty("ahead_by").GetInt32();
            var behind = compare.RootElement.GetProperty("behind_by").GetInt32();
            return status switch
            {
                "ahead" => new(UpdateStatus.NewerAvailable,
                    $"A newer AVABand is out: {ahead} {Plural(ahead, "change")} newer than yours, {built}{download}.", release.Url),
                "behind" => new(UpdateStatus.LocalIsNewer,
                    $"This build ({Short(localCommit)}) is {behind} {Plural(behind, "change")} newer than the newest release.", release.Url),
                "identical" => new(UpdateStatus.UpToDate, $"AVABand is up to date (build {Short(localCommit)}, {built}).", release.Url),
                _ => new(UpdateStatus.Diverged,
                    $"This build ({Short(localCommit)}) and the newest release ({built}) have each changed: {ahead} {Plural(ahead, "change")} there you don't have.", release.Url),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return new(UpdateStatus.Failed, $"Couldn't check for updates: {Reason(ex)}");
        }
    }

    /// <summary>The GET's JSON, or null on 404 (an unknown commit). Other failures throw.</summary>
    private async Task<JsonDocument?> GetJson(string path, CancellationToken cancel)
    {
        using var response = await http.GetAsync(Api + repository + "/" + path, cancel);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("GitHub is limiting requests; try again in a while.");
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancel);
        return await JsonDocument.ParseAsync(body, cancellationToken: cancel);
    }

    /// <summary>The release download for this platform (AVABand-&lt;rid&gt;.zip or .tar.gz), if there is one.</summary>
    public static string? AssetFor(IEnumerable<string> assets, string? rid = null)
    {
        rid ??= RuntimeInformation.RuntimeIdentifier;
        return assets.FirstOrDefault(a => a.StartsWith($"AVABand-{rid}.", StringComparison.OrdinalIgnoreCase));
    }

    private static string Reason(Exception ex) => ex switch
    {
        TaskCanceledException => "GitHub didn't answer in time.",
        HttpRequestException { StatusCode: null } h when h.Message.Contains("limiting") => h.Message,
        HttpRequestException { StatusCode: { } code } => $"GitHub answered {(int)code} {code}.",
        HttpRequestException => "GitHub couldn't be reached (offline?).",
        _ => "GitHub's answer couldn't be read.",
    };

    private static string Short(string sha) => sha.Length > 7 ? sha[..7] : sha;
    private static string Plural(int n, string word) => n == 1 ? word : word + "s";
}
