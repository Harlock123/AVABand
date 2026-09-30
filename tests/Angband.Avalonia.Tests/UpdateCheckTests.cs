using System.Net;
using System.Reflection;
using System.Text;
using Angband.Avalonia.Services;
using Angband.Avalonia.ViewModels;
using Angband.Avalonia.Views;
using Angband.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Angband.Avalonia.Tests;

/// <summary>Help → Check for updates, against a pretend GitHub (the tests never go online).</summary>
public class UpdateCheckTests
{
    private const string Release = "aaaaaaa1111111111111111111111111111111aa";
    private const string Mine = "bbbbbbb2222222222222222222222222222222bb";

    /// <summary>Answers GitHub API paths with canned JSON (or a status code); records what was asked.</summary>
    private sealed class FakeGitHub(Func<string, (HttpStatusCode Code, string Json)> answer) : HttpMessageHandler
    {
        public List<string> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var path = request.RequestUri!.AbsolutePath.Replace("/repos/Harlock123/AVABand/", "");
            Asked.Add(path);
            var (code, json) = answer(path);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private const string Releases = """
        [ { "draft": false, "tag_name": "v0.1.0", "name": "AVABand v0.1.0", "html_url": "https://example/old",
            "published_at": "2026-01-01T00:00:00Z", "assets": [] },
          { "draft": false, "tag_name": "latest", "name": "Latest build (main)", "html_url": "https://example/latest",
            "published_at": "2026-09-29T12:00:00Z", "assets": [ { "name": "AVABand-win-x64.zip" }, { "name": "AVABand-linux-x64.tar.gz" } ] } ]
        """;

    private static FakeGitHub GitHub(string compareStatus = "ahead", int ahead = 3, int behind = 0, bool knowsMine = true) => new(path => path switch
    {
        "releases" => (HttpStatusCode.OK, Releases),
        "commits/latest" => (HttpStatusCode.OK, $$"""{ "sha": "{{Release}}" }"""),
        _ when path.StartsWith("compare/") => knowsMine
            ? (HttpStatusCode.OK, $$"""{ "status": "{{compareStatus}}", "ahead_by": {{ahead}}, "behind_by": {{behind}} }""")
            : (HttpStatusCode.NotFound, "{}"),
        _ => (HttpStatusCode.NotFound, "{}"),
    });

    private static Task<UpdateCheck> Check(FakeGitHub github, string? mine = Mine) =>
        new UpdateChecker(new HttpClient(github)).CheckAsync(mine);

    [Fact]
    public async Task ANewerRelease_IsFound_ByComparingCommits()
    {
        var github = GitHub("ahead", ahead: 3);
        var result = await Check(github);
        Assert.Equal(UpdateStatus.NewerAvailable, result.Status);
        Assert.Contains("3 changes newer than yours", result.Message);
        Assert.Equal("https://example/latest", result.ReleaseUrl); // the newest release, not the oldest
        Assert.Equal(["releases", "commits/latest", $"compare/{Mine}...{Release}"], github.Asked);
    }

    [Fact]
    public async Task TheSameCommit_IsUpToDate_WithoutComparing()
    {
        var github = GitHub();
        var result = await Check(github, Release[..7]);
        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.StartsWith("AVABand is up to date (build aaaaaaa", result.Message);
        Assert.DoesNotContain(github.Asked, a => a.StartsWith("compare/"));
    }

    [Fact]
    public async Task ANewerBuild_ALocalBuild_AndAnUnknownOne_AreEachSaidPlainly()
    {
        Assert.Equal(UpdateStatus.LocalIsNewer, (await Check(GitHub("behind", ahead: 0, behind: 2))).Status);
        Assert.Equal(UpdateStatus.Diverged, (await Check(GitHub("diverged", ahead: 1, behind: 1))).Status);
        var local = await Check(GitHub(knowsMine: false));
        Assert.Equal(UpdateStatus.UnknownLocal, local.Status);
        Assert.Contains("isn't on GitHub", local.Message);
        Assert.Equal(UpdateStatus.UnknownLocal, (await Check(GitHub(), mine: null)).Status);
    }

    [Fact]
    public async Task Failures_SayWhy_AndDontThrow()
    {
        var limited = await Check(new FakeGitHub(_ => (HttpStatusCode.Forbidden, "{}")));
        Assert.Equal(UpdateStatus.Failed, limited.Status);
        Assert.Contains("limiting requests", limited.Message);

        var broken = await Check(new FakeGitHub(_ => (HttpStatusCode.OK, "not json")));
        Assert.Equal(UpdateStatus.Failed, broken.Status);

        var none = await Check(new FakeGitHub(p => (HttpStatusCode.OK, p == "releases" ? "[]" : "{}")));
        Assert.Equal("No AVABand release was found on GitHub.", none.Message);
    }

    [Fact]
    public void TheBuildsCommit_AndThisPlatformsDownload_AreFound()
    {
        var commit = UpdateChecker.BuildCommit(typeof(UpdateChecker).Assembly);
        var version = typeof(UpdateChecker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        if (version.Contains('+')) Assert.EndsWith(commit!, version); // built from a git checkout
        Assert.Equal("AVABand-win-x64.zip", UpdateChecker.AssetFor(["AVABand-win-x86.zip", "AVABand-win-x64.zip"], "win-x64"));
        Assert.Equal("AVABand-linux-x64.tar.gz", UpdateChecker.AssetFor(["AVABand-linux-musl-x64.tar.gz", "AVABand-linux-x64.tar.gz"], "linux-x64"));
        Assert.Null(UpdateChecker.AssetFor(["AVABand-osx-x64.tar.gz"], "win-arm64"));
    }

    private static (MainWindow Window, MainWindowViewModel Vm, List<string> Opened) Open(UpdateCheck answer)
    {
        MainWindow.ShowCreationOnFirstRun = false;
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], new AppSettings(), save: null);
        vm.StartGame(42, "warrior");
        vm.CheckUpdates = _ => Task.FromResult(answer);
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 760 };
        var opened = new List<string>();
        vm.OpenUrlRequested += opened.Add;
        window.Show();
        return (window, vm, opened);
    }

    [AvaloniaFact]
    public void ANewerRelease_OffersTheDownloadPage()
    {
        var (window, vm, opened) = Open(new UpdateCheck(UpdateStatus.NewerAvailable, "A newer AVABand is out: 3 changes newer than yours.", "https://example/latest"));
        vm.CheckForUpdatesCommand.Execute(null);
        Assert.True(vm.IsConfirming);
        Assert.Equal("A newer AVABand is out: 3 changes newer than yours. Open the download page? (y/n)", vm.LastMessage);
        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
        Assert.Equal(["https://example/latest"], opened);
    }

    [AvaloniaFact]
    public void UpToDate_JustSaysSo()
    {
        var (_, vm, opened) = Open(new UpdateCheck(UpdateStatus.UpToDate, "AVABand is up to date (build aaaaaaa, built 2026-09-29).", "https://example/latest"));
        vm.CheckForUpdatesCommand.Execute(null);
        Assert.False(vm.IsConfirming);
        Assert.StartsWith("AVABand is up to date", vm.LastMessage);
        Assert.Empty(opened);
    }
}
