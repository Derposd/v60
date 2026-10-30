using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.Boost;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.Tests;

public class ProfileBenchTests
{
    private sealed class T : Tweak
    {
        private readonly string _id;
        private readonly bool _recommended;

        public T(string id, bool recommended)
        {
            _id = id;
            _recommended = recommended;
        }

        public override string Id => _id;
        public override string Title => _id;
        public override TweakCategory Category => TweakCategory.Windows;
        public override Effectiveness Effectiveness => Effectiveness.Low;
        public override string Explanation => "x";
        public override bool Recommended => _recommended;
        public override TweakPlan Plan(TweakContext ctx) => Result(TweakStatus.CanApply, "x");
    }

    private static TweakToggle Toggle(string id, bool recommended, TweakStatus status, bool ours)
    {
        var t = new T(id, recommended);
        var plan = new TweakPlan { Tweak = t, Status = status };
        return new TweakToggle(t, plan, Tweak.StateOf(plan), ours ? new[] { new ChangeSet { TweakId = id } } : Array.Empty<ChangeSet>());
    }

    [Fact]
    public void Games_profile_first_time_takes_the_recommended_tweaks_then_the_remembered_set()
    {
        var toggles = new[]
        {
            Toggle("a", true, TweakStatus.CanApply, false),
            Toggle("b", false, TweakStatus.CanApply, false),
            Toggle("c", true, TweakStatus.AlreadyOptimal, false),
        };

        Assert.Equal(new[] { "a" }, new ProfileState().ToApply(toggles));
        Assert.Equal(new[] { "b" }, new ProfileState { GameTweaks = { "b", "c" } }.ToApply(toggles));
    }

    [Fact]
    public void Work_profile_rolls_back_only_what_this_app_turned_on()
    {
        var toggles = new[] { Toggle("a", true, TweakStatus.AlreadyOptimal, true), Toggle("b", true, TweakStatus.AlreadyOptimal, false) };
        Assert.Equal(new[] { "a" }, ProfileState.ToRevert(toggles));
    }

    [Fact]
    public void Profile_state_round_trips()
    {
        var fs = new InMemoryFileSystem();
        new ProfileState { Active = ProfileKind.Work, GameTweaks = { "x" }, GameModeWasOn = true }.Save(fs, @"C:\p.json");
        var back = ProfileState.Load(fs, @"C:\p.json");
        Assert.Equal(ProfileKind.Work, back.Active);
        Assert.Equal(new[] { "x" }, back.GameTweaks);
        Assert.True(back.GameModeWasOn);
    }

    private static BenchResult Run(string label, double fps, double low, DateTime t) =>
        new() { Game = "cs2", Label = label, AvgFps = fps, Low1Fps = low, Low01Fps = low * 0.8, MedianFrameMs = 1000 / fps, Time = t };

    [Fact]
    public void Small_differences_are_called_noise()
    {
        var d = BenchCompare.Deltas(Run("before", 200, 120, DateTime.Now), Run("after", 204, 121, DateTime.Now));
        Assert.True(d[0].Noise);
        Assert.Contains("погрешности", BenchCompare.Verdict(d));
    }

    [Fact]
    public void Real_gains_are_reported_with_percent()
    {
        var d = BenchCompare.Deltas(Run("before", 100, 60, DateTime.Now), Run("after", 120, 75, DateTime.Now));
        Assert.True(d[0].Better);
        Assert.Equal(20, d[0].Percent, 3);
        Assert.True(d[3].Better); // frame time went down
        Assert.StartsWith("Средний FPS вырос на 20%", BenchCompare.Verdict(d));
    }

    [Fact]
    public void Store_pairs_the_latest_before_with_an_after_taken_later()
    {
        var fs = new InMemoryFileSystem();
        var store = new BenchmarkStore(fs, @"C:\b.json");
        var t0 = new DateTime(2026, 1, 1);
        store.Add(Run("after", 90, 50, t0));
        store.Add(Run("before", 100, 60, t0.AddMinutes(1)));
        Assert.Null(store.Pair("cs2").After);

        store.Add(Run("after", 110, 70, t0.AddMinutes(5)));
        var (before, after) = store.Pair("CS2");
        Assert.Equal(100, before!.AvgFps);
        Assert.Equal(110, after!.AvgFps);
    }

    [Fact]
    public void Downsampling_keeps_spikes()
    {
        var frames = Enumerable.Repeat(5.0, 1000).ToList();
        frames[500] = 80;
        var d = BenchResult.Downsample(frames, 100);
        Assert.Equal(100, d.Count);
        Assert.Equal(80, d.Max());
    }

    [Fact]
    public void Every_boost_action_is_honest_about_its_effect()
    {
        Assert.All(BoostCatalog.All, a => Assert.False(string.IsNullOrWhiteSpace(a.Honest)));
        Assert.Contains("обычно не нужно", BoostCatalog.All.Single(a => a.Id == BoostCatalog.Standby).Honest);
    }
}
