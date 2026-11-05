using System.Collections.Concurrent;
using Ursacopy.Core;
using Ursacopy.Server.Data;

namespace Ursacopy.Server;

public sealed class RunState(string reason)
{
    public CopyProgress Progress { get; } = new();
    public CancellationTokenSource Cts { get; } = new();
    public string Reason { get; } = reason;
    /// <summary>True while waiting for another job: the external disk is written by one job at a time.</summary>
    public volatile bool Queued = true;
    public DateTime StartedUtc { get; } = DateTime.UtcNow;
}

/// <summary>Runs jobs one at a time (a single USB disk: two parallel copies would only make it seek) and exposes live progress.</summary>
public sealed class JobRunner
{
    readonly Repo _repo;
    readonly ILogger<JobRunner> _log;
    readonly ConcurrentDictionary<int, RunState> _states = new();
    readonly SemaphoreSlim _gate = new(1, 1);

    public JobRunner(Repo repo, ILogger<JobRunner> log, IHostApplicationLifetime life)
    {
        _repo = repo; _log = log;
        life.ApplicationStopping.Register(() => { foreach (var s in _states.Values) s.Cts.Cancel(); });
    }

    public RunState? Get(int jobId) => _states.GetValueOrDefault(jobId);

    /// <summary>Queues a run; false when this job is already running or queued.</summary>
    public bool Start(int jobId, string reason)
    {
        var state = new RunState(reason);
        if (!_states.TryAdd(jobId, state)) return false;
        _ = Task.Run(() => RunAsync(jobId, state));
        return true;
    }

    public void Cancel(int jobId) => Get(jobId)?.Cts.Cancel();

    async Task RunAsync(int jobId, RunState state)
    {
        bool entered = false;
        try
        {
            await _gate.WaitAsync(state.Cts.Token);
            entered = true;
            var job = _repo.GetJob(jobId);
            if (job is null) return;
            state.Queued = false;

            var run = new RunRow { Id = _repo.AddRun(job, state.Reason), JobId = job.Id, JobName = job.Name, StartedUtc = state.StartedUtc };
            CopyResult result;
            if (job.TargetId is null)
                result = new CopyResult(CopyStatus.Failed, "The external disk has not been initialised for this job.", [], TimeSpan.Zero);
            else
            {
                var options = new CopyOptions
                {
                    SourceDir = job.SourceDir, TargetDir = job.TargetDir, TargetId = job.TargetId, DestFolder = job.DestFolder,
                    Includes = job.IncludeList(), MirrorDeletes = job.MirrorDeletes, TrashRetentionDays = job.RetentionDays, Verify = job.Verify, WindowsDisk = job.WindowsDisk, Excludes = job.ExcludeList(),
                };
                result = await Task.Run(() => CopyEngine.Run(options, state.Progress, state.Cts.Token));
            }

            var p = state.Progress;
            run.EndedUtc = DateTime.UtcNow;
            run.Status = result.Status.ToString();
            run.Message = result.Message;
            run.Details = result.ErrorSamples.Count == 0 ? null : string.Join('\n', result.ErrorSamples);
            run.FilesSeen = p.FilesSeen; run.Copied = p.Copied; run.BytesCopied = p.BytesCopied;
            run.Unchanged = p.Unchanged; run.Deleted = p.Deleted; run.Errors = p.Errors;
            _repo.FinishRun(run);
            if (result.Status is CopyStatus.Success or CopyStatus.CompletedWithErrors) _repo.SetEstimate(job.Id, p.FilesSeen, p.BytesSeen);
            _log.LogInformation("Job '{Job}': {Status} ({Copied} copied, {Unchanged} unchanged, {Deleted} removed, {Errors} errors) in {Elapsed}",
                job.Name, result.Status, p.Copied, p.Unchanged, p.Deleted, p.Errors, Fmt.Duration(result.Elapsed));
        }
        catch (OperationCanceledException) { /* cancelled while queued: nothing was recorded */ }
        catch (Exception ex) { _log.LogError(ex, "Job {Id} failed unexpectedly", jobId); }
        finally
        {
            if (entered) _gate.Release();
            _states.TryRemove(jobId, out _);
        }
    }
}
