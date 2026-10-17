using Dota2Optimizer.Core.Game;

namespace Dota2Optimizer.Tests;

public class LaunchOptionsTests
{
    [Fact]
    public void Tokenizer_groups_cvars_with_values()
    {
        var tokens = LaunchOptionsComposer.Tokenize("-novid +fps_max 144 -threads 8 -high +exec autoexec");
        Assert.Equal(new[] { "-novid", "+fps_max 144", "-threads 8", "-high", "+exec autoexec" }, tokens);
    }

    [Fact]
    public void Analysis_separates_managed_user_and_myth_tokens()
    {
        var a = LaunchOptionsComposer.Analyze("-novid -threads 8 +exec my -prewarm -language russian");
        Assert.Equal(new[] { "-novid" }, a.ManagedTokens);
        Assert.Contains(a.MythTokens, m => m.Token == "-threads 8");
        Assert.Contains(a.MythTokens, m => m.Token == "-prewarm");
        Assert.Equal(new[] { "+exec my", "-language", "russian" }, a.UserTokens);
    }

    [Fact]
    public void Compose_keeps_user_tokens_and_removes_myths()
    {
        var line = LaunchOptionsComposer.Compose("-high -threads 8 -language russian", new[] { "-novid", "+fps_max" }, 120, removeMyths: true);
        Assert.Equal("-novid +fps_max 120 -language russian", line);
    }

    [Fact]
    public void Compose_can_keep_myths_when_asked()
    {
        var line = LaunchOptionsComposer.Compose("-threads 8", new[] { "-novid" }, null, removeMyths: false);
        Assert.Equal("-novid -threads 8", line);
    }

    [Fact]
    public void Compose_rejects_both_apis()
    {
        Assert.Throws<ArgumentException>(() => LaunchOptionsComposer.Compose(null, new[] { "-dx11", "-vulkan" }, null, true));
    }

    [Fact]
    public void Compose_is_idempotent()
    {
        var once = LaunchOptionsComposer.Compose("-console", new[] { "-novid", "-console", "+fps_max" }, 144, true);
        var twice = LaunchOptionsComposer.Compose(once, new[] { "-novid", "-console", "+fps_max" }, 144, true);
        Assert.Equal(once, twice);
    }

    [Theory]
    [InlineData(144, true, false, 144)]
    [InlineData(60, false, false, 90)]
    [InlineData(165, false, true, 162)]
    [InlineData(0, true, false, 60)]
    public void FpsMax_suggestion(int hz, bool weak, bool vrr, int expected)
    {
        Assert.Equal(expected, LaunchOptionsComposer.SuggestFpsMax(hz, weak, vrr));
    }

    [Fact]
    public void Catalog_does_not_offer_known_myths()
    {
        foreach (var o in LaunchOptionCatalog.All)
        {
            Assert.False(LaunchOptionCatalog.Myths.ContainsKey(o.Token), o.Token);
        }

        Assert.True(LaunchOptionCatalog.High.Disputed);
        Assert.False(LaunchOptionCatalog.High.Recommended);
    }
}
