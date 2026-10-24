using System.Text.Json;
using System.Text.RegularExpressions;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Redux;

public sealed class ReduxManifestEntry
{
    /// <summary>Path relative to the GTA V folder, "/" separators.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>"Added" (the file did not exist) or "Replaced" (the original was moved to <see cref="Backup"/>).</summary>
    public string Action { get; set; } = "Added";
    public string? Backup { get; set; }
    public long Length { get; set; }
}

/// <summary>Everything needed to take one pack out again. Written to disk before the first file is touched.</summary>
public sealed class ReduxManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string GtaDir { get; set; } = string.Empty;
    public DateTime InstalledAt { get; set; }

    /// <summary>"Installing" while files are being copied (an interrupted install is rolled back), then "Installed".</summary>
    public string State { get; set; } = "Installing";
    public List<ReduxManifestEntry> Entries { get; set; } = new();
    public List<string> CreatedDirs { get; set; } = new();
    public long TotalBytes => Entries.Sum(e => e.Length);
}

public sealed record ReduxProgress(int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, string? Current);

public sealed class ReduxInstallOptions
{
    /// <summary>Required when the pack's root could not be detected (<see cref="ReduxRootKind.Unknown"/>).</summary>
    public bool AcceptUnknownRoot { get; set; }

    /// <summary>True while GTA V or the RAGE MP client is running: files in use cannot be replaced safely.</summary>
    public Func<bool>? IsGameRunning { get; set; }

    /// <summary>Free bytes on the drive of the given path. Tests replace it; the default asks the OS.</summary>
    public Func<string, long>? FreeBytes { get; set; }
}

public sealed record ReduxInstallResult(bool Success, string Message, ReduxManifest? Manifest, int Installed, int Replaced);

public sealed record ReduxUninstallResult(bool Success, int Restored, int Removed, IReadOnlyList<string> Problems);

/// <summary>
/// Puts a graphics pack into the GTA V folder and takes it out again. Safety rules:
/// only files the <see cref="ReduxPolicy"/> allows are copied; an original file is moved aside (same drive, no extra space) and restored on removal;
/// the manifest is written before the first change; a failed or cancelled install rolls itself back; two packs never touch the same file.
/// </summary>
public sealed class ReduxManager
{
    public const string WorkFolder = ".framelab";
    private const string TempSuffix = ".framelab-tmp";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _manifestDir;

    public ReduxManager(string manifestDir)
    {
        _manifestDir = manifestDir;
    }

    public static string DefaultManifestDir => Path.Combine(AppPaths.Root, "redux");

    public IReadOnlyList<ReduxManifest> Installed()
    {
        var list = new List<ReduxManifest>();
        if (!Directory.Exists(_manifestDir))
        {
            return list;
        }

        foreach (var file in Directory.EnumerateFiles(_manifestDir, "*.json"))
        {
            try
            {
                var m = JsonSerializer.Deserialize<ReduxManifest>(File.ReadAllText(file));
                if (m is not null && m.Id.Length > 0)
                {
                    list.Add(m);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Log.Warn($"Не удалось прочитать список редуксов {file}: {ex.Message}");
            }
        }

        return list.OrderBy(m => m.InstalledAt).ToList();
    }

    public ReduxInstallResult Install(IReduxSource source, ReduxPlan plan, string gtaDir, ReduxInstallOptions? options = null,
        IProgress<ReduxProgress>? progress = null, CancellationToken ct = default)
    {
        options ??= new ReduxInstallOptions();
        gtaDir = Path.GetFullPath(gtaDir);

        if (!File.Exists(Path.Combine(gtaDir, "GTA5.exe")))
        {
            return Fail("В выбранной папке нет GTA5.exe — укажите папку с установленной GTA V.");
        }

        if (!plan.CanInstall)
        {
            return Fail("В паке нет файлов, которые можно безопасно установить.");
        }

        if (plan.NeedsRootConfirmation && !options.AcceptUnknownRoot)
        {
            return Fail("Не удалось определить корневую папку пака. Подтвердите установку явно или распакуйте пак и выберите папку с mods / update.");
        }

        if (options.IsGameRunning?.Invoke() == true)
        {
            return Fail("Закройте GTA V и клиент RAGE MP: файлы, которые игра использует, нельзя заменять.");
        }

        var toInstall = plan.ToInstall.ToList();
        var targets = toInstall.Select(f => Target(gtaDir, f.GtaPath)).ToList();
        if (targets.Any(t => !IsInside(gtaDir, t)))
        {
            return Fail("В паке есть путь за пределами папки игры — установка отменена.");
        }

        var owners = Installed();
        var conflicts = toInstall
            .Where(f => owners.Any(m => m.Entries.Any(e => string.Equals(e.Path, f.GtaPath, StringComparison.OrdinalIgnoreCase))))
            .Select(f => f.GtaPath)
            .ToList();
        if (conflicts.Count > 0)
        {
            var owner = owners.First(m => m.Entries.Any(e => string.Equals(e.Path, conflicts[0], StringComparison.OrdinalIgnoreCase)));
            return Fail($"Файл {conflicts[0]} уже изменён другим паком («{owner.Name}»). Сначала удалите его, потом ставьте новый. Всего пересечений: {conflicts.Count}.");
        }

        var bytes = plan.InstallBytes;
        var free = (options.FreeBytes ?? DefaultFreeBytes)(gtaDir);
        if (free >= 0 && free < bytes + (256L << 20))
        {
            return Fail($"Не хватает места на диске: нужно {bytes / 1048576.0 / 1024:0.#} ГБ, свободно {free / 1048576.0 / 1024:0.#} ГБ.");
        }

        var manifest = new ReduxManifest
        {
            Id = MakeId(plan.Name),
            Name = plan.Name,
            GtaDir = gtaDir,
            InstalledAt = DateTime.Now,
        };

        var byPath = source.Entries.ToDictionary(e => e.Path.Replace('\\', '/').TrimStart('/'), StringComparer.Ordinal);
        long done = 0;
        var count = 0;
        var replaced = 0;
        try
        {
            Save(manifest);
            foreach (var file in toInstall)
            {
                ct.ThrowIfCancellationRequested();
                var dest = Target(gtaDir, file.GtaPath);
                if (Directory.Exists(dest))
                {
                    throw new IOException($"На месте файла {file.GtaPath} в игре лежит папка.");
                }

                var entry = new ReduxManifestEntry { Path = file.GtaPath, Length = file.Length };
                manifest.Entries.Add(entry);
                EnsureDirectory(Path.GetDirectoryName(dest)!, gtaDir, manifest);

                if (File.Exists(dest))
                {
                    var backup = Path.Combine(gtaDir, WorkFolder, "originals", manifest.Id, file.GtaPath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    entry.Action = "Replaced";
                    entry.Backup = backup;
                    Save(manifest); // write-ahead: after this line the original can always be found again
                    File.Move(dest, backup);
                    replaced++;
                }

                var tmp = dest + TempSuffix;
                using (var input = source.Open(byPath[file.PackPath]))
                using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    Copy(input, output, ct, n =>
                    {
                        done += n;
                        progress?.Report(new ReduxProgress(count, toInstall.Count, done, bytes, file.GtaPath));
                    });
                }

                if (new FileInfo(tmp).Length != file.Length)
                {
                    File.Delete(tmp);
                    throw new IOException($"Файл {file.GtaPath} записан не полностью.");
                }

                File.Move(tmp, dest, overwrite: true);
                count++;
                if (count % 200 == 0)
                {
                    Save(manifest);
                }
            }

            manifest.State = "Installed";
            Save(manifest);
            progress?.Report(new ReduxProgress(count, toInstall.Count, bytes, bytes, null));
            Log.Info($"Редукс «{manifest.Name}» установлен: {count} файлов, заменено оригиналов: {replaced}");
            return new ReduxInstallResult(true, $"Установлено файлов: {count}. Заменено оригинальных файлов игры: {replaced} (они сохранены и вернутся при удалении).", manifest, count, replaced);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or KeyNotFoundException or InvalidDataException)
        {
            Log.Error($"Установка редукса «{plan.Name}» прервана, откат", ex);
            var undo = Remove(manifest);
            var why = ex is OperationCanceledException ? "Установка отменена." : $"Установка не удалась: {ex.Message}";
            var tail = undo.Problems.Count == 0
                ? " Все изменения отменены, файлы игры возвращены."
                : $" Откат прошёл не полностью ({undo.Problems.Count}): проверьте целостность файлов игры в Steam / Rockstar Games Launcher.";
            return new ReduxInstallResult(false, why + tail, null, 0, 0);
        }

        static ReduxInstallResult Fail(string message) => new(false, message, null, 0, 0);
    }

    public ReduxUninstallResult Uninstall(string id)
    {
        var manifest = Installed().FirstOrDefault(m => m.Id == id);
        return manifest is null
            ? new ReduxUninstallResult(false, 0, 0, new[] { "Пак не найден в списке установленных." })
            : Remove(manifest);
    }

    private ReduxUninstallResult Remove(ReduxManifest manifest)
    {
        var problems = new List<string>();
        var restored = 0;
        var removed = 0;
        for (var i = manifest.Entries.Count - 1; i >= 0; i--)
        {
            var entry = manifest.Entries[i];
            var dest = Target(manifest.GtaDir, entry.Path);
            try
            {
                foreach (var leftover in new[] { dest + TempSuffix })
                {
                    if (File.Exists(leftover))
                    {
                        File.Delete(leftover);
                    }
                }

                if (entry.Action == "Replaced")
                {
                    if (entry.Backup is null || !File.Exists(entry.Backup))
                    {
                        // The original may still be in place if the install stopped before it was moved.
                        if (!File.Exists(dest))
                        {
                            problems.Add($"Нет оригинала для {entry.Path}");
                        }

                        continue;
                    }

                    if (File.Exists(dest))
                    {
                        File.Delete(dest);
                    }

                    File.Move(entry.Backup, dest);
                    restored++;
                }
                else if (File.Exists(dest))
                {
                    File.Delete(dest);
                    removed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{entry.Path}: {ex.Message}");
            }
        }

        // Folders the install created, deepest first, only when empty.
        foreach (var dir in manifest.CreatedDirs.OrderByDescending(d => d.Length))
        {
            TryDeleteEmpty(Path.Combine(manifest.GtaDir, dir.Replace('/', Path.DirectorySeparatorChar)));
        }

        var backups = Path.Combine(manifest.GtaDir, WorkFolder, "originals", manifest.Id);
        PruneEmpty(backups);
        TryDeleteEmpty(Path.Combine(manifest.GtaDir, WorkFolder, "originals"));
        TryDeleteEmpty(Path.Combine(manifest.GtaDir, WorkFolder));

        try
        {
            var file = ManifestPath(manifest.Id);
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"список установленного: {ex.Message}");
        }

        Log.Info($"Редукс «{manifest.Name}» удалён: возвращено оригиналов {restored}, удалено файлов {removed}, проблем {problems.Count}");
        return new ReduxUninstallResult(problems.Count == 0, restored, removed, problems);
    }

    private static void Copy(Stream input, Stream output, CancellationToken ct, Action<int> progress)
    {
        var buffer = new byte[1 << 20];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
            progress(read);
        }
    }

    private static void EnsureDirectory(string dir, string gtaDir, ReduxManifest manifest)
    {
        var missing = new Stack<string>();
        var current = dir;
        while (!Directory.Exists(current) && IsInside(gtaDir, current) && !string.Equals(Path.GetFullPath(current), gtaDir, StringComparison.OrdinalIgnoreCase))
        {
            missing.Push(current);
            current = Path.GetDirectoryName(current)!;
        }

        while (missing.Count > 0)
        {
            var d = missing.Pop();
            Directory.CreateDirectory(d);
            manifest.CreatedDirs.Add(Path.GetRelativePath(gtaDir, d).Replace('\\', '/'));
        }
    }

    private static string Target(string gtaDir, string gtaRelative) =>
        Path.GetFullPath(Path.Combine(gtaDir, gtaRelative.Replace('/', Path.DirectorySeparatorChar)));

    private static bool IsInside(string root, string path)
    {
        var rel = Path.GetRelativePath(root, Path.GetFullPath(path));
        return !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel);
    }

    private static void TryDeleteEmpty(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be removed is only clutter.
        }
    }

    /// <summary>Deletes empty sub-folders bottom-up, then the folder itself if nothing is left.</summary>
    private static void PruneEmpty(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var d in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            TryDeleteEmpty(d);
        }

        TryDeleteEmpty(root);
    }

    private static long DefaultFreeBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return root is null ? -1 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    private static string MakeId(string name)
    {
        var slug = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9а-яё]+", "-").Trim('-');
        if (slug.Length > 32)
        {
            slug = slug[..32].Trim('-');
        }

        return $"{(slug.Length == 0 ? "pack" : slug)}-{Guid.NewGuid().ToString("N")[..6]}";
    }

    private string ManifestPath(string id) => Path.Combine(_manifestDir, id + ".json");

    private void Save(ReduxManifest manifest)
    {
        Directory.CreateDirectory(_manifestDir);
        var text = JsonSerializer.Serialize(manifest, Json);
        var path = ManifestPath(manifest.Id);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }
}
