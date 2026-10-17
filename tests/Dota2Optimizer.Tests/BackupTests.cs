using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Tests;

public class BackupTests
{
    private sealed class FakeCommands : ICommandRunner
    {
        public List<string> Calls { get; } = new();
        public Func<string, int> ExitCode { get; set; } = _ => 0;

        public CommandResult Run(string fileName, string arguments, TimeSpan? timeout = null)
        {
            var line = $"{fileName} {arguments}";
            Calls.Add(line);
            return new CommandResult(ExitCode(line), string.Empty, string.Empty);
        }
    }

    private readonly InMemoryFileSystem _fs = new();
    private readonly InMemoryRegistry _reg = new();
    private readonly FakeCommands _cmd = new();
    private readonly ChangeExecutor _exec;

    public BackupTests()
    {
        AppPaths.OverrideRoot(Path.Combine(Path.GetTempPath(), "d2o-tests"));
        _exec = new ChangeExecutor(_fs, _reg, _cmd, new BackupStore(_fs, @"C:\backup"));
    }

    [Fact]
    public void Dry_run_changes_nothing_but_previews()
    {
        _reg.SetValue(RegHive.CurrentUser, @"Software\Test", "A", RegValue.Dword(1));
        var actions = new[] { PlannedAction.SetReg(RegHive.CurrentUser, @"Software\Test", "A", RegValue.Dword(0), "test") };

        var result = _exec.Apply("t", "Test", actions, dryRun: true);

        Assert.True(result.DryRun);
        Assert.Null(result.ChangeSetId);
        Assert.Equal("1", _reg.GetValue(RegHive.CurrentUser, @"Software\Test", "A")!.Data);
        Assert.Equal("1", result.Lines[0].Before);
        Assert.Equal("0", result.Lines[0].After);
        Assert.Empty(_exec.Store.Load());
    }

    [Fact]
    public void Registry_change_is_rolled_back_to_previous_value()
    {
        _reg.SetValue(RegHive.CurrentUser, @"Software\Test", "A", RegValue.Dword(1));
        var r = _exec.Apply("t", "Test", new[] { PlannedAction.SetReg(RegHive.CurrentUser, @"Software\Test", "A", RegValue.Dword(0), "test") }, false);
        Assert.Equal("0", _reg.GetValue(RegHive.CurrentUser, @"Software\Test", "A")!.Data);

        var rb = _exec.Rollback(r.ChangeSetId!);

        Assert.True(rb.Success);
        Assert.Equal("1", _reg.GetValue(RegHive.CurrentUser, @"Software\Test", "A")!.Data);
        Assert.True(_exec.Store.Find(r.ChangeSetId!)!.RolledBack);
    }

    [Fact]
    public void Value_that_did_not_exist_is_deleted_on_rollback()
    {
        var r = _exec.Apply("t", "Test", new[] { PlannedAction.SetReg(RegHive.CurrentUser, @"Software\Test", "New", RegValue.Str("x"), "test") }, false);
        Assert.NotNull(_reg.GetValue(RegHive.CurrentUser, @"Software\Test", "New"));

        _exec.Rollback(r.ChangeSetId!);

        Assert.Null(_reg.GetValue(RegHive.CurrentUser, @"Software\Test", "New"));
    }

    [Fact]
    public void File_is_backed_up_and_restored_exactly()
    {
        const string path = @"C:\Games\dota\cfg\video.txt";
        _fs.WriteAllText(path, "original\r\ncontent");

        var r = _exec.Apply("t", "Video", new[] { PlannedAction.WriteFile(path, "changed", "test") }, false);
        Assert.Equal("changed", _fs.ReadAllText(path));
        var record = _exec.Store.Find(r.ChangeSetId!)!.Records.Single();
        Assert.True(record.Existed);
        Assert.Equal("original\r\ncontent", _fs.ReadAllText(record.BackupFile!));

        _exec.Rollback(r.ChangeSetId!);

        Assert.Equal("original\r\ncontent", _fs.ReadAllText(path));
    }

    [Fact]
    public void Created_file_is_removed_on_rollback()
    {
        const string path = @"C:\Games\dota\cfg\autoexec.cfg";
        var r = _exec.Apply("t", "New file", new[] { PlannedAction.WriteFile(path, "x", "test") }, false);
        Assert.True(_fs.FileExists(path));

        _exec.Rollback(r.ChangeSetId!);

        Assert.False(_fs.FileExists(path));
    }

    [Fact]
    public void Command_undo_is_executed_on_rollback()
    {
        var r = _exec.Apply("p", "Power", new[] { PlannedAction.Run("powercfg", "/setactive HIGH", "powercfg", "/setactive OLD", "power", false) }, false);
        _exec.Rollback(r.ChangeSetId!);
        Assert.Equal(new[] { "powercfg /setactive HIGH", "powercfg /setactive OLD" }, _cmd.Calls);
    }

    [Fact]
    public void Failure_midway_rolls_back_completed_steps()
    {
        _reg.SetValue(RegHive.CurrentUser, @"Software\Test", "A", RegValue.Dword(1));
        _cmd.ExitCode = line => line.Contains("boom") ? 5 : 0;
        var actions = new[]
        {
            PlannedAction.SetReg(RegHive.CurrentUser, @"Software\Test", "A", RegValue.Dword(0), "step 1"),
            PlannedAction.Run("tool", "boom", "tool", "undo", "step 2", false),
        };

        var r = _exec.Apply("t", "Test", actions, false);

        Assert.False(r.Success);
        Assert.Equal("1", _reg.GetValue(RegHive.CurrentUser, @"Software\Test", "A")!.Data);
        var set = _exec.Store.Load().Single();
        Assert.True(set.Failed);
        Assert.True(set.RolledBack);
    }

    [Fact]
    public void RollbackAll_unwinds_overlapping_changes_newest_first()
    {
        _reg.SetValue(RegHive.CurrentUser, @"Software\Test", "A", RegValue.Dword(1));
        _exec.Apply("t1", "First", new[] { PlannedAction.SetReg(RegHive.CurrentUser, @"Software\Test", "A", RegValue.Dword(2), "1→2") }, false);
        Thread.Sleep(5);
        _exec.Apply("t2", "Second", new[] { PlannedAction.SetReg(RegHive.CurrentUser, @"Software\Test", "A", RegValue.Dword(3), "2→3") }, false);

        var rb = _exec.RollbackAll(includeAdmin: true);

        Assert.True(rb.Success);
        Assert.Equal("1", _reg.GetValue(RegHive.CurrentUser, @"Software\Test", "A")!.Data);
        Assert.Empty(_exec.Store.Active());
    }

    [Fact]
    public void RollbackAll_skips_admin_sets_when_not_elevated()
    {
        _exec.Apply("hklm", "Admin", new[] { PlannedAction.SetReg(RegHive.LocalMachine, @"SOFTWARE\Test", "A", RegValue.Dword(1), "admin") }, false);
        _exec.Apply("hkcu", "User", new[] { PlannedAction.SetReg(RegHive.CurrentUser, @"Software\Test", "B", RegValue.Dword(1), "user") }, false);

        var rb = _exec.RollbackAll(includeAdmin: false);

        Assert.False(rb.Success);
        Assert.Single(_exec.Store.Active());
        Assert.Equal("hklm", _exec.Store.Active()[0].TweakId);
        Assert.NotNull(_reg.GetValue(RegHive.LocalMachine, @"SOFTWARE\Test", "A"));
        Assert.Null(_reg.GetValue(RegHive.CurrentUser, @"Software\Test", "B"));
    }

    [Fact]
    public void Later_conflicts_are_detected()
    {
        var first = _exec.Apply("t1", "First", new[] { PlannedAction.SetReg(RegHive.CurrentUser, @"Software\Test", "A", RegValue.Dword(2), "x") }, false);
        Thread.Sleep(5);
        _exec.Apply("t2", "Second", new[] { PlannedAction.SetReg(RegHive.CurrentUser, @"Software\Test", "A", RegValue.Dword(3), "y") }, false);

        Assert.Single(_exec.Store.LaterConflicts(first.ChangeSetId!));
    }

    [Fact]
    public void Manifest_is_written_before_the_change_executes()
    {
        // A command that inspects the manifest at execution time proves the write-ahead order.
        var store = _exec.Store;
        var seenRecords = -1;
        var exec = new ChangeExecutor(_fs, _reg, new DelegateRunner(() => seenRecords = store.Load().Single().Records.Count), store);

        exec.Apply("p", "Power", new[] { PlannedAction.Run("x", "y", "x", "z", "d", false) }, false);

        Assert.Equal(1, seenRecords);
    }

    private sealed class DelegateRunner : ICommandRunner
    {
        private readonly Action _onRun;

        public DelegateRunner(Action onRun) => _onRun = onRun;

        public CommandResult Run(string fileName, string arguments, TimeSpan? timeout = null)
        {
            _onRun();
            return new CommandResult(0, string.Empty, string.Empty);
        }
    }
}
