using System.Text.RegularExpressions;

namespace Dota2Optimizer.Core.Infrastructure;

/// <summary>Start type in the words <c>sc.exe config start=</c> uses, so a backup can be put back with the same tool.</summary>
public static class ServiceStart
{
    public const string Auto = "auto";
    public const string DelayedAuto = "delayed-auto";
    public const string Manual = "demand";
    public const string Disabled = "disabled";

    public static string Ru(string start) => start switch
    {
        Auto => "автоматически",
        DelayedAuto => "автоматически (отложенный запуск)",
        Manual => "вручную",
        Disabled => "отключена",
        _ => start,
    };
}

public sealed record ServiceState(string Name, string Start, bool Running);

public interface IServiceControl
{
    /// <summary>Null when the service is not installed (or is a driver we never touch).</summary>
    ServiceState? Query(string name);

    void SetStart(string name, string start);
    bool Stop(string name);
    bool Start(string name);
}

/// <summary>
/// Windows services through the registry (read) and sc.exe (change). Both are the documented, supported ways:
/// sc.exe updates the service control manager at once, a direct registry write would only apply after a reboot.
/// Boot and system drivers (start type 0/1) are reported as missing so no tweak can touch them.
/// </summary>
public sealed partial class ServiceControl : IServiceControl
{
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";

    // sc.exe exit codes that mean "already in the wanted state".
    private const int NotActive = 1062;
    private const int AlreadyRunning = 1056;

    private readonly IRegistry _reg;
    private readonly ICommandRunner _cmd;

    public ServiceControl(IRegistry reg, ICommandRunner cmd)
    {
        _reg = reg;
        _cmd = cmd;
    }

    // "STATE : 4  RUNNING": a single digit 1–7 followed by the state word. TYPE has two digits, exit codes are followed by "(0x…)".
    [GeneratedRegex(@":\s*([1-7])\s+[A-Z_]{4,}")]
    private static partial Regex StateLine();

    /// <summary>Only the name and letters/digits/._- are allowed: the name ends up on an sc.exe command line.</summary>
    public static bool IsValidName(string name) => name.Length is > 0 and < 128 && name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.');

    public ServiceState? Query(string name)
    {
        if (!IsValidName(name))
        {
            return null;
        }

        var key = $@"{ServicesKey}\{name}";
        var start = _reg.GetValue(RegHive.LocalMachine, key, "Start")?.AsInt();
        if (start is null or < 2 or > 4)
        {
            return null;
        }

        var delayed = _reg.GetValue(RegHive.LocalMachine, key, "DelayedAutostart")?.AsInt() == 1;
        var text = start switch
        {
            2 => delayed ? ServiceStart.DelayedAuto : ServiceStart.Auto,
            3 => ServiceStart.Manual,
            _ => ServiceStart.Disabled,
        };
        return new ServiceState(name, text, IsRunning(name));
    }

    public void SetStart(string name, string start)
    {
        Guard(name);
        var r = _cmd.Run("sc.exe", $"config {name} start= {start}");
        if (!r.Success)
        {
            throw new InvalidOperationException($"Не удалось изменить тип запуска службы {name}: код {r.ExitCode} {r.StdOut.Trim()}");
        }
    }

    public bool Stop(string name)
    {
        Guard(name);
        var r = _cmd.Run("sc.exe", $"stop {name}");
        return r.Success || r.ExitCode == NotActive;
    }

    public bool Start(string name)
    {
        Guard(name);
        var r = _cmd.Run("sc.exe", $"start {name}");
        return r.Success || r.ExitCode == AlreadyRunning;
    }

    /// <summary>"STATE : 4 RUNNING" — the number is the same in every Windows language.</summary>
    public static bool? ParseRunning(string scQueryOutput)
    {
        var m = StateLine().Match(scQueryOutput);
        return m.Success ? m.Groups[1].Value is "4" or "2" : null;
    }

    private bool IsRunning(string name)
    {
        var r = _cmd.Run("sc.exe", $"query {name}", TimeSpan.FromSeconds(10));
        return ParseRunning(r.StdOut) ?? false;
    }

    private static void Guard(string name)
    {
        if (!IsValidName(name))
        {
            throw new InvalidOperationException($"Недопустимое имя службы: {name}");
        }
    }
}
