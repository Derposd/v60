using Dota2Optimizer.Core.Tweaks;
using Xunit;

namespace Dota2Optimizer.Tests;

public class SpecCatalogTests
{
    [Fact]
    public void General_profile_comes_first_and_lists_every_windows_tweak()
    {
        Assert.Equal(SpecGame.General, SpecCatalog.Games[0]);
        var profile = SpecCatalog.For(SpecGame.General);
        Assert.Equal(SystemTweakCatalog.All.Select(t => t.Id).OrderBy(x => x), profile.Items.Select(i => i.Id).OrderBy(x => x));
        Assert.Null(profile.Defaults);
    }

    [Theory]
    [InlineData(SpecGame.Dota2)]
    [InlineData(SpecGame.Cs2)]
    [InlineData(SpecGame.GtaRp)]
    public void Game_profiles_put_the_game_rows_first_and_have_no_duplicates(SpecGame game)
    {
        var items = SpecCatalog.For(game).Items;
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
        Assert.Equal(SpecCatalog.TitleOf(game), items[0].Group);
        // Groups are contiguous: a group never reappears after another one.
        var groups = items.Select(i => i.Group).ToList();
        var seen = new List<string>();
        foreach (var g in groups.Where((g, i) => i == 0 || g != groups[i - 1]))
        {
            Assert.DoesNotContain(g, seen);
            seen.Add(g);
        }
    }

    [Fact]
    public void Gta_profile_leaves_the_graphics_modes_to_their_page()
    {
        Assert.DoesNotContain(SpecCatalog.For(SpecGame.GtaRp).Items, i => i.Id.StartsWith(Dota2Optimizer.Core.GtaRp.GtaCatalog.ModePrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void Risk_maps_to_three_levels_and_only_high_risk_asks_for_consent()
    {
        Assert.Equal(SpecRisk.Safe, SpecCatalog.RiskOf(TweakRisk.Low));
        Assert.Equal(SpecRisk.Mid, SpecCatalog.RiskOf(TweakRisk.Medium));
        Assert.Equal(SpecRisk.Hard, SpecCatalog.RiskOf(TweakRisk.High));
        foreach (var t in SystemTweakCatalog.All)
        {
            Assert.Equal(t.RiskLevel == TweakRisk.High, t.RequiresConsent);
        }
    }

    [Fact]
    public void Both_power_plans_are_mutually_exclusive()
    {
        var group = Assert.Single(SpecCatalog.Exclusive, g => g.Contains("win.powerplan"));
        Assert.Contains("win.powerplan.ultimate", group);
        Assert.NotNull(SystemTweakCatalog.Find("win.powerplan.ultimate"));
    }
}
