using System.Diagnostics.Eventing.Reader;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Crash;

/// <summary>Reads relevant entries from the Application and System logs. Read-only, no admin needed.</summary>
public static class EventLogSource
{
    public static IReadOnlyList<RawEvent> Read(int days, int maxPerLog = 500)
    {
        var ms = (long)TimeSpan.FromDays(days).TotalMilliseconds;
        var app = $"*[System[(EventID=1000 or EventID=1001 or EventID=1002) and TimeCreated[timediff(@SystemTime) <= {ms}]]]";
        var sys = "*[System[(Provider[@Name='Display'] or Provider[@Name='nvlddmkm'] or Provider[@Name='amdkmdag'] or Provider[@Name='amdwddmg'] " +
                  "or Provider[@Name='Microsoft-Windows-Kernel-Power'] or Provider[@Name='Microsoft-Windows-WHEA-Logger']) " +
                  $"and (Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= {ms}]]]";
        var result = new List<RawEvent>();
        result.AddRange(ReadLog("Application", app, maxPerLog));
        result.AddRange(ReadLog("System", sys, maxPerLog));
        return result;
    }

    private static IEnumerable<RawEvent> ReadLog(string log, string xpath, int max)
    {
        var list = new List<RawEvent>();
        try
        {
            var query = new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (var i = 0; i < max; i++)
            {
                using var record = reader.ReadEvent();
                if (record is null)
                {
                    break;
                }

                string message;
                try
                {
                    message = record.FormatDescription() ?? string.Empty;
                }
                catch (EventLogException)
                {
                    message = string.Empty;
                }

                var props = record.Properties.Select(p => p.Value?.ToString() ?? string.Empty).ToList();
                list.Add(new RawEvent(record.TimeCreated ?? DateTime.MinValue, log, record.ProviderName ?? string.Empty, record.Id, message, props));
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        {
            Log.Warn($"Журнал {log}: {ex.Message}");
        }

        return list;
    }
}
