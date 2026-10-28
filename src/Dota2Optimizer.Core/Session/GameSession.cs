using System.Text.Json;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Session;

public sealed class WatchedGame
{
    /// <summary>Process name without ".exe" (cs2, dota2, GTA5…).</summary>
    public string Process { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Full path when the user picked the exe by hand; used for the per-game fullscreen optimisation flag.</summary>
    public string? Path { get; set; }
}

/// <summary>Settings of «Игровой режим» (real-time): what to pause while a game runs.</summary>
public sealed class GameModeConfig
{
    public bool Enabled { get; set; }
    public bool HighPriority { get; set; } = true;
    public List<WatchedGame> Games { get; set; } = new();
    public List<string> Processes { get; set; } = new();
    public List<string> Services { get; set; } = new();
}

/// <summary>What the session changed, written to disk before each change so it can be undone even after a crash.</summary>
public sealed class GameSessionState
{
    public string Game { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public List<RunningProcess> ClosedProcesses { get; set; } = new();
    public List<string> StoppedServices { get; set; } = new();
    public string? PriorityRaised { get; set; }
}

public sealed record SessionPreset(string Name, string Description, IReadOnlyList<string> Processes, IReadOnlyList<string> Services);

public static class GameModePresets
{
    /// <summary>Never closed or stopped, whatever the user list says: Windows itself, voice chat, streaming, launchers, anti-cheats.</summary>
    public static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "csrss", "winlogon", "wininit", "lsass", "services", "smss", "svchost", "dwm", "explorer", "audiodg", "fontdrvhost",
        "sihost", "ctfmon", "conhost", "RuntimeBroker", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost", "MsMpEng",
        "SecurityHealthService", "NisSrv", "steam", "steamwebhelper", "steamservice", "Discord", "obs64", "obs32", "EpicGamesLauncher",
        "EpicWebHelper", "Battle.net", "Agent", "EADesktop", "EABackgroundService", "upc", "UbisoftConnect", "RiotClientServices", "vgc", "vgtray",
        "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "BEServices", "FACEIT", "faceitclient", "FrameLab", "PresentMon", "nvcontainer",
        "NVDisplay.Container", "amdow", "RadeonSoftware", "atiesrxx", "atieclxx", "IntelGraphicsSoftware",
    };

    /// <summary>Services that must keep running during a game (audio, network, anti-cheats, Windows Update, security).</summary>
    public static readonly HashSet<string> ProtectedServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "Audiosrv", "AudioEndpointBuilder", "Dhcp", "Dnscache", "nsi", "netprofm", "NlaSvc", "WinDefend", "mpssvc", "BFE", "wuauserv", "UsoSvc",
        "BITS", "CryptSvc", "EventLog", "RpcSs", "Schedule", "Winmgmt", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "vgc", "vgk", "FACEIT",
        "XblAuthManager", "XboxGipSvc", "XboxNetApiSvc", "GamingServices", "GamingServicesNet", "Themes", "UxSms", "PlugPlay", "Power",
    };

    public static readonly IReadOnlyList<SessionPreset> All = new[]
    {
        new SessionPreset("Облака и синхронизация", "OneDrive, Dropbox и Google Диск не будут качать файлы и нагружать диск во время игры. После игры запустятся снова и досинхронизируют.",
            new[] { "OneDrive", "Dropbox", "GoogleDriveFS" }, Array.Empty<string>()),
        new SessionPreset("Обновляторы программ", "Службы обновления Google, Microsoft Edge и Adobe ничего не скачают во время игры. Обновления Windows не трогаются.",
            Array.Empty<string>(), new[] { "gupdate", "gupdatem", "edgeupdate", "edgeupdatem", "AdobeARMservice" }),
        new SessionPreset("Индексирование поиска", "Служба Windows Search на время игры не читает диск. Полезно на HDD.",
            Array.Empty<string>(), new[] { "WSearch" }),
        new SessionPreset("Рабочие мессенджеры", "Microsoft Teams и Skype закрываются на время игры и запускаются после. Discord не закрывается никогда.",
            new[] { "ms-teams", "Teams", "Skype" }, Array.Empty<string>()),
    };

    /// <summary>Anti-cheats that may refuse or flag a priority change from outside; the priority step is skipped for these games.</summary>
    public static readonly HashSet<string> NoPriorityGames = new(StringComparer.OrdinalIgnoreCase) { "VALORANT-Win64-Shipping", "VALORANT" };

    /// <summary>Common games detected without setup (process names).</summary>
    public static readonly IReadOnlyList<WatchedGame> KnownGames = new[]
    {
        new WatchedGame { Process = "cs2", Name = "Counter-Strike 2" },
        new WatchedGame { Process = "dota2", Name = "Dota 2" },
        new WatchedGame { Process = "GTA5", Name = "GTA V" },
        new WatchedGame { Process = "GTA5_Enhanced", Name = "GTA V Enhanced" },
        new WatchedGame { Process = "VALORANT-Win64-Shipping", Name = "Valorant" },
        new WatchedGame { Process = "FortniteClient-Win64-Shipping", Name = "Fortnite" },
        new WatchedGame { Process = "r5apex", Name = "Apex Legends" },
        new WatchedGame { Process = "r5apex_dx12", Name = "Apex Legends (DX12)" },
        new WatchedGame { Process = "Minecraft.Windows", Name = "Minecraft (Bedrock)" },
        new WatchedGame { Process = "RustClient", Name = "Rust" },
        new WatchedGame { Process = "TslGame", Name = "PUBG" },
        new WatchedGame { Process = "Overwatch", Name = "Overwatch 2" },
        new WatchedGame { Process = "League of Legends", Name = "League of Legends" },
        new WatchedGame { Process = "cod", Name = "Call of Duty" },
        new WatchedGame { Process = "RainbowSix", Name = "Rainbow Six Siege" },
        new WatchedGame { Process = "destiny2", Name = "Destiny 2" },
        new WatchedGame { Process = "eldenring", Name = "Elden Ring" },
        new WatchedGame { Process = "Cyberpunk2077", Name = "Cyberpunk 2077" },
    };
}

/// <summary>
/// «Игровой режим» in real time. When a watched game starts: closes the chosen background programs, stops the chosen services and raises the
/// game's priority. When it ends — or on the next start after a crash — everything is put back. Each change is journaled to disk first.
/// The game process is never read or modified; the only thing done to it is the optional priority change, the same as Task Manager.
/// </summary>
public sealed class GameSessionManager
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly IProcessControl _proc;
    private readonly IServiceControl _services;
    private readonly IFileSystem _fs;
    private readonly string _statePath;

    public GameSessionManager(IProcessControl proc, IServiceControl services, IFileSystem fs, string statePath)
    {
        _proc = proc;
        _services = services;
        _fs = fs;
        _statePath = statePath;
    }

    public GameSessionState? Current => Load();

    public bool Active => _fs.FileExists(_statePath);

    /// <summary>Running watched game, if any (first match).</summary>
    public static WatchedGame? RunningGame(GameModeConfig config, IReadOnlyCollection<string> running)
    {
        var set = new HashSet<string>(running, StringComparer.OrdinalIgnoreCase);
        return config.Games.Concat(GameModePresets.KnownGames).FirstOrDefault(g => set.Contains(g.Process));
    }

    /// <summary>Call every few seconds. Starts or ends the session; returns messages for the user.</summary>
    public IReadOnlyList<string> Tick(GameModeConfig config)
    {
        var running = _proc.RunningNames();
        var game = config.Enabled ? RunningGame(config, running) : null;
        var state = Load();
        if (game is null)
        {
            return state is null ? Array.Empty<string>() : Restore("Игра закрыта");
        }

        return state is null ? Begin(config, game, running) : Array.Empty<string>();
    }

    /// <summary>On app start: a session left over from a crash is undone if its game no longer runs.</summary>
    public IReadOnlyList<string> RecoverAfterCrash()
    {
        var state = Load();
        if (state is null)
        {
            return Array.Empty<string>();
        }

        var running = new HashSet<string>(_proc.RunningNames(), StringComparer.OrdinalIgnoreCase);
        return running.Contains(state.Game) ? Array.Empty<string>() : Restore("Восстановление после прошлого запуска");
    }

    public IReadOnlyList<string> Begin(GameModeConfig config, WatchedGame game, IReadOnlyCollection<string> running)
    {
        var messages = new List<string>();
        var state = new GameSessionState { Game = game.Process };
        Save(state); // journal first: even an empty session tells the next start to check

        var runningSet = new HashSet<string>(running, StringComparer.OrdinalIgnoreCase);
        foreach (var name in config.Processes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (GameModePresets.Protected.Contains(name) || name.Equals(game.Process, StringComparison.OrdinalIgnoreCase) || !runningSet.Contains(name))
            {
                continue;
            }

            foreach (var p in _proc.Find(name))
            {
                if (p.Path is null)
                {
                    // Without a path it could not be started again: leave it running.
                    continue;
                }

                state.ClosedProcesses.Add(p);
                Save(state);
                if (!_proc.Close(p.Pid, TimeSpan.FromSeconds(5)))
                {
                    state.ClosedProcesses.Remove(p);
                    Save(state);
                }
            }
        }

        foreach (var svc in config.Services.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (GameModePresets.ProtectedServices.Contains(svc) || _services.Query(svc) is not { Running: true })
            {
                continue;
            }

            state.StoppedServices.Add(svc);
            Save(state);
            if (!_services.Stop(svc))
            {
                state.StoppedServices.Remove(svc);
                Save(state);
            }
        }

        if (config.HighPriority && !GameModePresets.NoPriorityGames.Contains(game.Process))
        {
            state.PriorityRaised = game.Process;
            Save(state);
            if (!_proc.SetHighPriority(game.Process))
            {
                state.PriorityRaised = null;
                Save(state);
                messages.Add("Приоритет игры не изменён: процесс не разрешил (так бывает с античитами). Остальное работает.");
            }
        }

        var parts = new List<string>();
        if (state.ClosedProcesses.Count > 0)
        {
            parts.Add($"закрыто программ: {state.ClosedProcesses.Count}");
        }

        if (state.StoppedServices.Count > 0)
        {
            parts.Add($"остановлено служб: {state.StoppedServices.Count}");
        }

        if (state.PriorityRaised is not null)
        {
            parts.Add("приоритет «Высокий»");
        }

        messages.Insert(0, $"{game.Name} запущена — игровой режим: " + (parts.Count == 0 ? "фоновых программ из списка не найдено." : string.Join(", ", parts) + "."));
        Log.Info(messages[0]);
        return messages;
    }

    /// <summary>Puts everything back: services first (they were stopped last), then programs. The journal is removed only at the end.</summary>
    public IReadOnlyList<string> Restore(string reason)
    {
        var state = Load();
        if (state is null)
        {
            return Array.Empty<string>();
        }

        var messages = new List<string>();
        if (state.PriorityRaised is not null)
        {
            // The game has usually exited; if it still runs (manual restore), give it normal priority back.
            _proc.SetNormalPriority(state.PriorityRaised);
        }

        var failed = new List<string>();
        foreach (var svc in Enumerable.Reverse(state.StoppedServices))
        {
            if (!_services.Start(svc))
            {
                failed.Add($"служба {svc}");
            }
        }

        var running = new HashSet<string>(_proc.RunningNames(), StringComparer.OrdinalIgnoreCase);
        foreach (var p in state.ClosedProcesses.GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
        {
            // Started again by itself or by the user meanwhile: don't start a second copy.
            if (running.Contains(p.Name))
            {
                continue;
            }

            if (!_proc.Start(p.Path!, ArgumentsOf(p)))
            {
                failed.Add(p.Name);
            }
        }

        _fs.DeleteFile(_statePath);
        var restored = $"{reason}: возвращено программ — {state.ClosedProcesses.Count}, служб — {state.StoppedServices.Count}.";
        messages.Add(failed.Count == 0 ? restored : restored + $" Не удалось запустить: {string.Join(", ", failed)} — запустите вручную.");
        Log.Info(messages[0]);
        return messages;
    }

    /// <summary>The arguments part of a recorded command line (after the quoted or plain exe path).</summary>
    public static string? ArgumentsOf(RunningProcess p)
    {
        var cmd = p.CommandLine?.Trim();
        if (string.IsNullOrEmpty(cmd))
        {
            return null;
        }

        string rest;
        if (cmd.StartsWith('"'))
        {
            var end = cmd.IndexOf('"', 1);
            rest = end > 0 ? cmd[(end + 1)..] : string.Empty;
        }
        else
        {
            var exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            rest = exe > 0 ? cmd[(exe + 4)..] : string.Empty;
        }

        rest = rest.Trim();
        return rest.Length == 0 ? null : rest;
    }

    private GameSessionState? Load()
    {
        if (!_fs.FileExists(_statePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GameSessionState>(_fs.ReadAllText(_statePath), Json);
        }
        catch (JsonException ex)
        {
            Log.Error("Повреждён журнал игрового режима", ex);
            return new GameSessionState();
        }
    }

    private void Save(GameSessionState state) => _fs.WriteAllText(_statePath, JsonSerializer.Serialize(state, Json));
}

/// <summary>The config file of the real-time game mode.</summary>
public static class GameModeConfigStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static GameModeConfig Load(IFileSystem fs, string path)
    {
        try
        {
            if (fs.FileExists(path))
            {
                return JsonSerializer.Deserialize<GameModeConfig>(fs.ReadAllText(path), Json) ?? new GameModeConfig();
            }
        }
        catch (JsonException ex)
        {
            Log.Error("Не удалось прочитать настройки игрового режима", ex);
        }

        // First start: the two safest presets are on.
        var config = new GameModeConfig();
        config.Processes.AddRange(GameModePresets.All[0].Processes);
        config.Services.AddRange(GameModePresets.All[1].Services);
        return config;
    }

    public static void Save(IFileSystem fs, string path, GameModeConfig config) => fs.WriteAllText(path, JsonSerializer.Serialize(config, Json));
}
