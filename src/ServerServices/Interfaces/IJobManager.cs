namespace ServerServices.Interfaces;

/// <summary>
/// Starts a long-running operation and wires its progress, completion and failure into the
/// <c>jobs</c> table and the user's message channel.
///
/// Extracted from the concrete <c>JobManager</c> so a controller that starts a job can be built in a
/// test without standing up the whole messaging and localization stack behind it. The concrete class
/// remains the only implementation.
/// </summary>
public interface IJobManager
{
    /// <summary>
    /// Registers the job, starts it, and returns its <c>jobs</c> row id. The runner executes on a
    /// background task; this returns as soon as it has been registered.
    /// </summary>
    Task<int> RunAndRegisterJob(IJobRunner jobRunner);
}
