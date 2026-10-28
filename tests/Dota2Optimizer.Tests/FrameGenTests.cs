using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.SystemInfo;
using Dota2Optimizer.Core.Upscale;

namespace Dota2Optimizer.Tests;

public class FrameGenTests
{
    private const string Game = @"D:\SteamLibrary\steamapps\common\Cyberpunk 2077";
    private const string Exe = Game + @"\bin\x64\Cyberpunk2077.exe";
    private const string Release = @"C:\FrameLab\downloads\OptiScaler-0.7";
    private readonly InMemoryFileSystem _fs = new();

    public FrameGenTests()
    {
        _fs.WriteAllText(Exe, "exe");
        _fs.WriteAllText(Game + @"\bin\x64\nvngx_dlss.dll", "dlss");
        _fs.WriteAllText(Release + @"\OptiScaler.dll", "opti");
        _fs.WriteAllText(Release + @"\OptiScaler.ini", "ini");
        _fs.WriteAllText(Release + @"\libxess.dll", "xess");
        _fs.WriteAllText(Release + @"\D3D12_Optiscaler\D3D12Core.dll", "core");
        _fs.WriteAllText(Release + @"\setup_windows.bat", "bat");
        _fs.WriteAllText(Release + @"\Licenses\LICENSE", "gpl");
    }

    [Fact]
    public void Plan_copies_optiscaler_as_dxgi_and_keeps_the_games_own_libraries()
    {
        _fs.WriteAllText(Game + @"\bin\x64\libxess.dll", "game's xess");

        var plan = OptiScalerInstaller.Plan(_fs, Exe, Release);

        Assert.Null(plan.Blocked);
        Assert.Equal("dxgi.dll", plan.ProxyName);
        Assert.Contains("DLSS", plan.Upscalers);
        var targets = plan.Actions.Select(a => a.Path).ToList();
        Assert.Contains(Game + @"\bin\x64\dxgi.dll", targets);
        Assert.Contains(Game + @"\bin\x64\OptiScaler.ini", targets);
        Assert.Contains(Game + @"\bin\x64\D3D12_Optiscaler\D3D12Core.dll", targets);
        Assert.DoesNotContain(Game + @"\bin\x64\libxess.dll", targets);
        Assert.DoesNotContain(targets, t => t!.EndsWith(".bat") || t.Contains("Licenses"));
        Assert.All(plan.Actions, a => Assert.Equal(ActionKind.CopyFile, a.Kind));
    }

    [Fact]
    public void Existing_dxgi_from_reshade_is_left_alone()
    {
        _fs.WriteAllText(Game + @"\bin\x64\dxgi.dll", "reshade");
        Assert.Equal("winmm.dll", OptiScalerInstaller.Plan(_fs, Exe, Release).ProxyName);
    }

    [Fact]
    public void Anti_cheat_in_the_game_root_blocks_the_install()
    {
        _fs.WriteAllText(Game + @"\EasyAntiCheat\EasyAntiCheat_EOS_Setup.exe", "eac");
        var plan = OptiScalerInstaller.Plan(_fs, Exe, Release);

        Assert.NotNull(plan.Blocked);
        Assert.Contains("Easy Anti-Cheat", plan.AntiCheats);
        Assert.Empty(plan.Actions);
    }

    [Fact]
    public void Known_online_games_are_blocked_even_without_anti_cheat_files()
    {
        const string cs2 = @"D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\game\bin\win64\cs2.exe";
        _fs.WriteAllText(cs2, "x");
        Assert.NotNull(OptiScalerInstaller.Plan(_fs, cs2, Release).Blocked);
    }

    [Fact]
    public void Game_without_an_upscaler_is_refused()
    {
        const string exe = @"C:\Games\Old\old.exe";
        _fs.WriteAllText(exe, "x");
        Assert.Contains("нет DLSS", OptiScalerInstaller.Plan(_fs, exe, Release).Blocked);
    }

    [Fact]
    public void Undo_restores_the_game_folder_exactly()
    {
        _fs.WriteAllText(Game + @"\bin\x64\winmm.dll", "unused");
        var plan = OptiScalerInstaller.Plan(_fs, Exe, Release);
        var exec = new ChangeExecutor(_fs, new InMemoryRegistry(), new NoCommands(), new BackupStore(_fs, @"C:\b"));

        var r = exec.Apply("opti", "OptiScaler", plan.Actions, false);
        Assert.Equal("opti", _fs.ReadAllText(Game + @"\bin\x64\dxgi.dll"));
        exec.Rollback(r.ChangeSetId!);

        Assert.False(_fs.FileExists(Game + @"\bin\x64\dxgi.dll"));
        Assert.False(_fs.FileExists(Game + @"\bin\x64\OptiScaler.ini"));
        Assert.Equal("unused", _fs.ReadAllText(Game + @"\bin\x64\winmm.dll"));
    }

    [Fact]
    public void Unreal_game_root_is_two_levels_above_binaries()
    {
        Assert.Equal(@"C:\Games\Stalker2", OptiScalerInstaller.GameRoot(_fs, @"C:\Games\Stalker2\Stalker2\Binaries\Win64"));
        Assert.Equal(Game, OptiScalerInstaller.GameRoot(_fs, Game + @"\bin\x64"));
    }

    [Fact]
    public void Lossless_scaling_is_found_in_a_steam_library_and_advice_fits_the_card()
    {
        _fs.WriteAllText(@"E:\Steam\steamapps\appmanifest_993090.acf", "x");
        _fs.WriteAllText(@"E:\Steam\steamapps\common\Lossless Scaling\LosslessScaling.exe", "x");
        Assert.NotNull(LosslessScaling.Find(_fs, new[] { @"D:\SteamLibrary", @"E:\Steam" }));

        var weak = LosslessScaling.Recommendations(GpuFamily.Of(new GpuInfo { Name = "NVIDIA GeForce GTX 1050 Ti", Vendor = GpuVendor.Nvidia }), 144);
        Assert.Contains(weak, l => l.Contains("72"));
        Assert.Contains(weak, l => l.Contains("Performance"));
    }

    private sealed class NoCommands : ICommandRunner
    {
        public CommandResult Run(string fileName, string arguments, TimeSpan? timeout = null) => new(0, string.Empty, string.Empty);
    }
}
