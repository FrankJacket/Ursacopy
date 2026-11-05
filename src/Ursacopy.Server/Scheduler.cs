using Ursacopy.Server.Data;

namespace Ursacopy.Server;

/// <summary>
/// Starts jobs: daily at a time of the server's time zone, or whenever the external disk shows up. A daily job whose disk is
/// absent at its time simply waits and runs as soon as the disk is connected.
/// </summary>
public sealed class Scheduler(Repo repo, JobRunner runner, ILogger<Scheduler> log) : BackgroundService
{
    readonly Dictionary<int, bool> _wasPresent = [];

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        try
        {
            do
            {
                try { Tick(); }
                catch (Exception ex) { log.LogError(ex, "Scheduler tick failed"); }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    void Tick()
    {
        var now = DateTime.Now;
        foreach (var j in repo.ListJobs())
        {
            bool present = DiskStatus.Check(j).State == DiskState.Connected;
            _wasPresent.TryGetValue(j.Id, out var was);
            _wasPresent[j.Id] = present;
            if (!j.Enabled || !present || runner.Get(j.Id) is not null) continue;

            string? reason = null;
            if (j.ScheduleMode == Schedule.OnPlug && !was) reason = "Disk connected";
            else if (j.ScheduleMode == Schedule.Daily && TimeSpan.TryParse(j.DailyTime, out var tod))
            {
                var slot = now.Date + tod;
                var last = repo.LastStartedUtc(j.Id) ?? j.CreatedUtc; // a job created after today's slot waits for tomorrow's
                if (now >= slot && DateTime.SpecifyKind(last, DateTimeKind.Utc).ToLocalTime() < slot) reason = "Schedule";
            }
            if (reason is not null && runner.Start(j.Id, reason)) log.LogInformation("Starting job '{Job}' ({Reason})", j.Name, reason);
        }
    }
}
