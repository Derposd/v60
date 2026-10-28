using Dota2Optimizer.Core.Backup;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Tweaks;

public sealed record StartupEntry(
    string Name,
    string Command,
    string Source,
    RegHive ApprovedHive,
    string ApprovedKey,
    bool Enabled)
{
    /// <summary>Id of the change set that switched this entry, so each entry has its own undo.</summary>
    public string TweakId => $"startup:{Source}:{Name}";

    public bool RequiresAdmin => ApprovedHive == RegHive.LocalMachine;

    /// <summary>Executable path from the command line, for the icon and the "file is missing" note.</summary>
    public string ExePath => StartupManager.ExeFromCommand(Command);
}

/// <summary>
/// Programs that start with Windows. Entries are switched the same way Task Manager does it — through the
/// «StartupApproved» flags — so nothing is deleted and the program's own Run value stays untouched.
/// </summary>
public sealed class StartupManager
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    // First byte 2 (or 6) = enabled, 3 (or 7) = disabled; the remaining 11 bytes hold a timestamp Task Manager fills in.
    public const string EnabledValue = "020000000000000000000000";
    public const string DisabledValue = "030000000000000000000000";

    private readonly IRegistry _reg;
    private readonly IFileSystem _fs;
    private readonly string _userStartup;
    private readonly string _commonStartup;

    public StartupManager(IRegistry reg, IFileSystem fs, string userStartupFolder, string commonStartupFolder)
    {
        _reg = reg;
        _fs = fs;
        _userStartup = userStartupFolder;
        _commonStartup = commonStartupFolder;
    }

    public static StartupManager ForCurrentUser(IRegistry reg, IFileSystem fs) => new(reg, fs,
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));

    public IReadOnlyList<StartupEntry> List()
    {
        var list = new List<StartupEntry>();
        AddRun(list, RegHive.CurrentUser, Run, "Реестр (пользователь)", $@"{Approved}\Run");
        AddRun(list, RegHive.LocalMachine, Run, "Реестр (все пользователи)", $@"{Approved}\Run");
        AddRun(list, RegHive.LocalMachine, Run32, "Реестр (32-бит, все пользователи)", $@"{Approved}\Run32");
        AddFolder(list, _userStartup, RegHive.CurrentUser, "Папка «Автозагрузка»");
        AddFolder(list, _commonStartup, RegHive.LocalMachine, "Папка «Автозагрузка» (все пользователи)");
        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>The single change that enables or disables an entry.</summary>
    public static PlannedAction Switch(StartupEntry e, bool enable) =>
        PlannedAction.SetReg(e.ApprovedHive, e.ApprovedKey, e.Name, new RegValue(RegKind.Binary, enable ? EnabledValue : DisabledValue),
            $"Автозагрузка «{e.Name}»: {(enable ? "включить" : "выключить")}");

    public static bool IsEnabled(RegValue? approved)
    {
        if (approved is not { Kind: RegKind.Binary } || approved.Data.Length < 2)
        {
            return true; // no flag yet = enabled
        }

        var first = Convert.ToByte(approved.Data[..2], 16);
        return (first & 0x1) == 0;
    }

    public static string ExeFromCommand(string command)
    {
        var c = command.Trim();
        if (c.StartsWith('"'))
        {
            var end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : c.Trim('"');
        }

        var exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? c[..(exe + 4)] : c.Split(' ')[0];
    }

    private void AddRun(List<StartupEntry> list, RegHive hive, string key, string source, string approvedKey)
    {
        foreach (var name in _reg.GetValueNames(hive, key).Where(n => n.Length > 0))
        {
            var cmd = _reg.GetValue(hive, key, name)?.Data ?? string.Empty;
            var enabled = IsEnabled(_reg.GetValue(hive, approvedKey, name));
            list.Add(new StartupEntry(name, cmd, source, hive, approvedKey, enabled));
        }
    }

    private void AddFolder(List<StartupEntry> list, string folder, RegHive hive, string source)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        foreach (var file in _fs.EnumerateFiles(folder, "*", recursive: false))
        {
            var name = PathEx.FileName(file);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var enabled = IsEnabled(_reg.GetValue(hive, $@"{Approved}\StartupFolder", name));
            list.Add(new StartupEntry(name, file, source, hive, $@"{Approved}\StartupFolder", enabled));
        }
    }
}

/// <summary>One startup entry as a tweak, so switching it goes through the same preview, backup and undo as everything else.</summary>
public sealed class StartupEntryTweak : Tweak
{
    public StartupEntryTweak(StartupEntry entry, bool enable)
    {
        Entry = entry;
        Enable = enable;
    }

    public StartupEntry Entry { get; }
    public bool Enable { get; }

    public override string Id => Entry.TweakId;
    public override string Title => $"Автозагрузка: {Entry.Name} — {(Enable ? "включить" : "выключить")}";
    public override TweakCategory Category => TweakCategory.Windows;
    public override Effectiveness Effectiveness => Effectiveness.HardwareDependent;
    public override string Explanation => "Программа перестаёт (или снова начинает) запускаться вместе с Windows. Сама программа не удаляется.";

    public override TweakPlan Plan(TweakContext ctx) =>
        Entry.Enabled == Enable
            ? Result(TweakStatus.AlreadyOptimal, "Уже так.")
            : Result(TweakStatus.CanApply, Entry.Command, new[] { StartupManager.Switch(Entry, Enable) });
}
