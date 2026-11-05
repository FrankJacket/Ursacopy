namespace Ursacopy.Core;

/// <summary>
/// Progress and time left of a run, from what the previous complete run saw. Pure arithmetic on counters the engine already
/// keeps: it costs nothing (no extra scan of the source).
/// </summary>
public static class ProgressEstimator
{
    /// <summary>Not shown before this much of the run is done and this much time has passed: the first seconds say nothing.</summary>
    const double MinFraction = 0.03;
    static readonly TimeSpan MinElapsed = TimeSpan.FromSeconds(10);

    /// <param name="bytesSeen">Size of the files looked at so far (copied or already up to date).</param>
    /// <param name="filesSeen">Number of files looked at so far.</param>
    /// <param name="estimatedBytes">Size seen by the last complete run; 0 when there was none.</param>
    /// <param name="estimatedFiles">Number of files seen by the last complete run; 0 when there was none.</param>
    /// <returns>Fraction done (0 to 0.99, or null without a previous run) and time left (null while it would be a guess).</returns>
    public static (double? Fraction, TimeSpan? Remaining) Estimate(
        long bytesSeen, long filesSeen, long estimatedBytes, long estimatedFiles, TimeSpan elapsed)
    {
        double f;
        if (estimatedBytes > 0) f = (double)bytesSeen / estimatedBytes;
        else if (estimatedFiles > 0) f = (double)filesSeen / estimatedFiles;
        else return (null, null);

        f = Math.Clamp(f, 0, 0.99); // the source may have grown since the last run: never show 100 % before the end
        if (f < MinFraction || elapsed < MinElapsed) return (f, null);
        return (f, TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - f) / f));
    }
}
