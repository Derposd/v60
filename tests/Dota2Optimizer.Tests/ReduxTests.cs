using System.IO.Compression;
using Dota2Optimizer.Core.Redux;

namespace Dota2Optimizer.Tests;

public sealed class ReduxTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "framelab-redux-" + Guid.NewGuid().ToString("N"));
    private readonly string _gta;
    private readonly ReduxManager _manager;

    public ReduxTests()
    {
        _gta = Path.Combine(_tmp, "GTA V");
        Directory.CreateDirectory(_gta);
        File.WriteAllText(Path.Combine(_gta, "GTA5.exe"), "exe");
        _manager = new ReduxManager(Path.Combine(_tmp, "manifests"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmp, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A pack held in memory; <paramref name="failOn"/> makes opening that file throw, like a damaged archive.</summary>
    private sealed class FakeSource : IReduxSource
    {
        private readonly Dictionary<string, byte[]> _files;
        private readonly string? _failOn;

        public FakeSource(string name, Dictionary<string, string> files, string? failOn = null)
        {
            Name = name;
            _files = files.ToDictionary(kv => kv.Key, kv => System.Text.Encoding.UTF8.GetBytes(kv.Value));
            _failOn = failOn;
        }

        public string Name { get; }
        public IReadOnlyList<ReduxEntry> Entries => _files.Select(kv => new ReduxEntry(kv.Key, kv.Value.Length)).ToList();

        public Stream Open(ReduxEntry entry) =>
            entry.Path == _failOn ? throw new IOException("диск отвалился") : new MemoryStream(_files[entry.Path]);

        public void Dispose()
        {
        }
    }

    private static ReduxInstallOptions Roomy() => new() { FreeBytes = _ => long.MaxValue };

    private string InGta(string rel) => Path.Combine(_gta, rel.Replace('/', Path.DirectorySeparatorChar));

    private static FakeSource Pack(string name = "NaturalLook") => new(name, new Dictionary<string, string>
    {
        ["Readme.txt"] = "read me",
        ["Preview.jpg"] = "jpg",
        ["Pack/mods/update/x64/dlcpacks/look/dlc.rpf"] = "new dlc",
        ["Pack/mods/common/data/visualsettings.dat"] = "new visual",
        ["Pack/x64a.rpf"] = "new x64a",
    });

    // ---- policy and analysis ----

    [Fact]
    public void Root_is_found_inside_a_wrapper_folder_and_documentation_is_left_out()
    {
        var plan = ReduxAnalyzer.Analyze(Pack());

        Assert.Equal(ReduxRootKind.Detected, plan.RootKind);
        Assert.Equal("Pack/", plan.PayloadPrefix);
        Assert.Equal(3, plan.InstallCount);
        Assert.Contains(plan.Skipped, f => f.PackPath == "Readme.txt");
        Assert.Contains(plan.ToInstall, f => f.GtaPath == "mods/common/data/visualsettings.dat");
        Assert.Contains(plan.Warnings, w => w.Contains("основные архивы игры"));
        Assert.Contains(plan.Warnings, w => w.Contains("RPF"));
        Assert.Contains(plan.Warnings, w => w.Contains("правил вашего проекта"));
    }

    [Fact]
    public void Code_scripts_and_unsafe_paths_are_never_installed()
    {
        var source = new FakeSource("Evil", new Dictionary<string, string>
        {
            ["mods/ok.ytd"] = "x",
            ["ScriptHookV.dll"] = "x",
            ["scripts/hack.asi"] = "x",
            ["scripts/run.ps1"] = "x",
            ["GTA5.exe"] = "x",
            ["commandline.txt"] = "-scriptfolder",
            ["mods/../../outside.ytd"] = "x",
            ["C:/Windows/evil.ytd"] = "x",
            ["mods/nul.ytd"] = "x",
            ["mods/texture.ytd.exe"] = "x",
            ["mods/readme.psd"] = "x",
        });

        var plan = ReduxAnalyzer.Analyze(source);

        Assert.Equal(new[] { "mods/ok.ytd" }, plan.ToInstall.Select(f => f.GtaPath).ToArray());
        // Everything except the one data file is either blocked (code, bad path) or skipped (unknown type).
        Assert.Equal(10, plan.Blocked.Count() + plan.Skipped.Count());
        Assert.Contains(plan.Blocked, f => f.PackPath == "ScriptHookV.dll");
        Assert.Contains(plan.Blocked, f => f.PackPath == "commandline.txt");
        Assert.Contains(plan.Blocked, f => f.PackPath == "mods/texture.ytd.exe");
        Assert.Contains(plan.Skipped, f => f.PackPath == "mods/readme.psd");
    }

    [Fact]
    public void Advantage_mods_are_called_out_and_an_unknown_root_needs_confirmation()
    {
        var source = new FakeSource("Odd", new Dictionary<string, string>
        {
            ["stuff/a.ytd"] = "x",
            ["other/xray_walls.ytd"] = "x",
        });

        var plan = ReduxAnalyzer.Analyze(source);

        Assert.Equal(ReduxRootKind.Unknown, plan.RootKind);
        Assert.True(plan.NeedsRootConfirmation);
        Assert.Contains(plan.Warnings, w => w.Contains("считается читом"));

        var result = _manager.Install(source, plan, _gta, Roomy());
        Assert.False(result.Success);
        Assert.Contains("корневую папку", result.Message);
        Assert.False(File.Exists(InGta("stuff/a.ytd")));

        Assert.True(_manager.Install(source, plan, _gta, new ReduxInstallOptions { AcceptUnknownRoot = true, FreeBytes = _ => long.MaxValue }).Success);
    }

    // ---- install / uninstall ----

    [Fact]
    public void Install_copies_files_keeps_originals_and_uninstall_puts_everything_back()
    {
        File.WriteAllText(InGta("x64a.rpf"), "ORIGINAL x64a");
        Directory.CreateDirectory(InGta("mods/common/data"));
        File.WriteAllText(InGta("mods/common/data/visualsettings.dat"), "ORIGINAL visual");

        var source = Pack();
        var result = _manager.Install(source, ReduxAnalyzer.Analyze(source), _gta, Roomy());

        Assert.True(result.Success, result.Message);
        Assert.Equal(3, result.Installed);
        Assert.Equal(2, result.Replaced);
        Assert.Equal("new x64a", File.ReadAllText(InGta("x64a.rpf")));
        Assert.Equal("new dlc", File.ReadAllText(InGta("mods/update/x64/dlcpacks/look/dlc.rpf")));
        Assert.False(File.Exists(InGta("Readme.txt")));
        Assert.Single(_manager.Installed());
        Assert.Equal("Installed", _manager.Installed()[0].State);

        var undo = _manager.Uninstall(result.Manifest!.Id);

        Assert.True(undo.Success, string.Join("; ", undo.Problems));
        Assert.Equal(2, undo.Restored);
        Assert.Equal(1, undo.Removed);
        Assert.Equal("ORIGINAL x64a", File.ReadAllText(InGta("x64a.rpf")));
        Assert.Equal("ORIGINAL visual", File.ReadAllText(InGta("mods/common/data/visualsettings.dat")));
        Assert.False(File.Exists(InGta("mods/update/x64/dlcpacks/look/dlc.rpf")));
        Assert.False(Directory.Exists(InGta("mods/update")), "folders the install created are removed");
        Assert.True(Directory.Exists(InGta("mods/common/data")), "folders that were already there stay");
        Assert.False(Directory.Exists(InGta(ReduxManager.WorkFolder)));
        Assert.Empty(_manager.Installed());
    }

    [Fact]
    public void A_failure_halfway_rolls_everything_back()
    {
        File.WriteAllText(InGta("x64a.rpf"), "ORIGINAL x64a");
        var source = new FakeSource("Broken", Pack().Entries.ToDictionary(e => e.Path, e => "data-" + e.Path), failOn: "Pack/x64a.rpf");

        var result = _manager.Install(source, ReduxAnalyzer.Analyze(source), _gta, Roomy());

        Assert.False(result.Success);
        Assert.Contains("отменены", result.Message);
        Assert.Equal("ORIGINAL x64a", File.ReadAllText(InGta("x64a.rpf")));
        Assert.False(File.Exists(InGta("mods/common/data/visualsettings.dat")));
        Assert.False(Directory.Exists(InGta("mods")));
        Assert.Empty(_manager.Installed());
        Assert.Empty(Directory.EnumerateFiles(_gta, "*" + "tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Cancelling_rolls_back_too()
    {
        var source = Pack();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = _manager.Install(source, ReduxAnalyzer.Analyze(source), _gta, Roomy(), ct: cts.Token);

        Assert.False(result.Success);
        Assert.Contains("отменена", result.Message);
        Assert.Empty(_manager.Installed());
    }

    [Fact]
    public void Two_packs_never_share_a_file()
    {
        var first = Pack("First");
        Assert.True(_manager.Install(first, ReduxAnalyzer.Analyze(first), _gta, Roomy()).Success);

        var second = Pack("Second");
        var result = _manager.Install(second, ReduxAnalyzer.Analyze(second), _gta, Roomy());

        Assert.False(result.Success);
        Assert.Contains("First", result.Message);
        Assert.Single(_manager.Installed());
    }

    [Fact]
    public void Install_is_refused_when_the_game_runs_the_folder_is_wrong_or_the_disk_is_full()
    {
        var source = Pack();
        var plan = ReduxAnalyzer.Analyze(source);

        var running = _manager.Install(source, plan, _gta, new ReduxInstallOptions { IsGameRunning = () => true, FreeBytes = _ => long.MaxValue });
        Assert.False(running.Success);
        Assert.Contains("Закройте GTA V", running.Message);

        var wrong = _manager.Install(source, plan, _tmp, Roomy());
        Assert.False(wrong.Success);
        Assert.Contains("GTA5.exe", wrong.Message);

        var full = _manager.Install(source, plan, _gta, new ReduxInstallOptions { FreeBytes = _ => 10 });
        Assert.False(full.Success);
        Assert.Contains("места", full.Message);

        Assert.False(File.Exists(InGta("x64a.rpf")));
        Assert.Empty(_manager.Installed());
    }

    [Fact]
    public void A_pack_with_only_code_installs_nothing()
    {
        var source = new FakeSource("OnlyDll", new Dictionary<string, string> { ["mods/a.dll"] = "x", ["mods/b.asi"] = "x" });
        var result = _manager.Install(source, ReduxAnalyzer.Analyze(source), _gta, Roomy());

        Assert.False(result.Success);
        Assert.Contains("безопасно", result.Message);
    }

    // ---- sources ----

    [Fact]
    public void Zip_and_folder_sources_list_the_same_files_and_zip_slip_names_are_blocked()
    {
        var zipPath = Path.Combine(_tmp, "pack.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "Look\\mods\\a.ytd", "Look/x64a.rpf", "../../evil.ytd" })
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write("data");
            }
        }

        using var source = new ZipReduxSource(zipPath);
        var plan = ReduxAnalyzer.Analyze(source);

        Assert.Equal("pack", source.Name);
        Assert.Contains(plan.ToInstall, f => f.GtaPath == "mods/a.ytd");
        Assert.DoesNotContain(plan.ToInstall, f => f.PackPath.Contains(".."));

        var folder = Path.Combine(_tmp, "unpacked", "mods");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.ytd"), "data");
        using var fs = new FolderReduxSource(Path.Combine(_tmp, "unpacked"));
        Assert.Equal("mods/a.ytd", Assert.Single(fs.Entries).Path);
        using var stream = fs.Open(fs.Entries[0]);
        Assert.Equal(4, stream.Length);
    }

    [Fact]
    public void Installing_from_a_real_zip_works_end_to_end()
    {
        var zipPath = Path.Combine(_tmp, "pack.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(zip.CreateEntry("Look/mods/update/a.ytd").Open());
            w.Write("zipped");
        }

        using var source = new ZipReduxSource(zipPath);
        var result = _manager.Install(source, ReduxAnalyzer.Analyze(source), _gta, Roomy());

        Assert.True(result.Success, result.Message);
        Assert.Equal("zipped", File.ReadAllText(InGta("mods/update/a.ytd")));
        Assert.True(_manager.Uninstall(result.Manifest!.Id).Success);
        Assert.False(Directory.Exists(InGta("mods")));
    }
}
