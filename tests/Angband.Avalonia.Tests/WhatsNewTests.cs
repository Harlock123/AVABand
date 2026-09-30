using Angband.Avalonia.ViewModels;
using Angband.Data;
using Avalonia.Headless.XUnit;

namespace Angband.Avalonia.Tests;

/// <summary>What's new: shown once after an update, never to a new player, and always in Help.</summary>
public class WhatsNewTests
{
    private const string Page = """
        # What's new

        Intro.

        ## 2026-10-01

        - **Newest** thing.

        ## 2026-09-30

        - Middle thing.

        ## 2026-09-29

        - Oldest thing.
        """;

    [Fact]
    public void Sections_AreReadNewestFirst()
    {
        var sections = WhatsNew.Sections(Page);
        Assert.Equal(["2026-10-01", "2026-09-30", "2026-09-29"], sections.Select(s => s.Date));
        Assert.Equal("- **Newest** thing.", sections[0].Text);
        Assert.Equal("2026-10-01", WhatsNew.Newest(Page));
    }

    [Fact]
    public void ASecondRoundTheSameDay_SortsAfterTheFirst()
    {
        const string page = "# What's new\n\n## 2026-09-30.2\n\n- Later.\n\n## 2026-09-30\n\n- Earlier.\n";
        Assert.Equal(["2026-09-30.2", "2026-09-30"], WhatsNew.Sections(page).Select(s => s.Date));
        var topic = WhatsNew.Since(page, "2026-09-30")!;
        var text = string.Join(" ", topic.Blocks.SelectMany(b => b.Spans).Select(s => s.Text));
        Assert.Contains("Later.", text);
        Assert.DoesNotContain("Earlier.", text);
        Assert.Contains(topic.Blocks, b => b.Kind == HelpBlockKind.Heading && b.Spans[0].Text == "2026-09-30");
    }

    [Fact]
    public void OnlyWhatIsNewer_IsShown()
    {
        var topic = WhatsNew.Since(Page, "2026-09-29")!;
        Assert.Equal("What's new since you last played", topic.Title);
        var text = string.Join(" ", topic.Blocks.SelectMany(b => b.Spans).Select(s => s.Text));
        Assert.Contains("Newest", text);
        Assert.Contains("Middle thing.", text);
        Assert.DoesNotContain("Oldest thing.", text);
        Assert.Null(WhatsNew.Since(Page, "2026-10-01"));
        // Never shown before (updating from a build without it): just the newest.
        Assert.DoesNotContain("Middle", string.Join(" ", WhatsNew.Since(Page, null)!.Blocks.SelectMany(b => b.Spans).Select(s => s.Text)));
    }

    [AvaloniaFact]
    public void TheBundledPage_IsDatedThroughout_AndInTheHelp()
    {
        var page = WhatsNew.Load();
        var headings = page.Split('\n').Count(l => l.StartsWith("## ", StringComparison.Ordinal));
        Assert.Equal(headings, WhatsNew.Sections(page).Count); // every section heading is a date
        var help = HelpViewModel.LoadBundled().Single(t => t.Title == "What's new");
        Assert.DoesNotContain(help.Blocks, b => b.Kind == HelpBlockKind.Heading && b.Spans[0].Text.Contains('.'));
    }

    private static (MainWindowViewModel Vm, List<HelpViewModel> Shown, List<AppSettings> Saved) Open(AppSettings settings)
    {
        var shown = new List<HelpViewModel>();
        var saved = new List<AppSettings>();
        var vm = new MainWindowViewModel(DataLoader.Load(DataLoader.DefaultDataDirectory), [], settings, saved.Add);
        vm.HelpRequested += shown.Add;
        return (vm, shown, saved);
    }

    [AvaloniaFact]
    public void AfterAnUpdate_ItIsShownOnce()
    {
        var settings = new AppSettings { LastCharacter = new SavedCharacter(), WhatsNewSeen = "2026-09-29" };
        var (vm, shown, saved) = Open(settings);
        vm.ShowWhatsNewIfUpdated(Page);
        var help = Assert.Single(shown);
        Assert.Equal("What's new since you last played", help.Selected!.Title);
        Assert.Equal("2026-10-01", settings.WhatsNewSeen);
        Assert.NotEmpty(saved);

        vm.ShowWhatsNewIfUpdated(Page); // the next start: nothing new
        Assert.Single(shown);
    }

    [AvaloniaFact]
    public void ANewPlayer_IsSparedIt_ButItIsRemembered()
    {
        var settings = new AppSettings();
        var (vm, shown, _) = Open(settings);
        Assert.True(vm.IsFirstRun);
        vm.ShowWhatsNewIfUpdated(Page);
        Assert.Empty(shown);
        Assert.Equal("2026-10-01", settings.WhatsNewSeen);
    }

    [AvaloniaFact]
    public void TheHelpMenu_OpensAtWhatsNew()
    {
        var (vm, shown, _) = Open(new AppSettings());
        vm.ShowWhatsNewCommand.Execute(null);
        Assert.Equal("What's new", Assert.Single(shown).Selected!.Title);
    }
}
