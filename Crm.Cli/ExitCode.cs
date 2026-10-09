namespace Crm.Cli;

/// <summary>Process exit codes. Anything but 0 means the run folder must not be trusted as complete.</summary>
public enum ExitCode
{
    Success = 0,

    /// <summary>A reconciliation, privilege or file failure. The run folder is sealed with status <c>failed</c>.</summary>
    RunFailed = 1,

    /// <summary>
    /// Configuration is invalid — there is nothing to read — and no run folder was created. 3, 4 and 5 were the
    /// network path's (internet-facing deployment, server unreachable, credentials rejected) and are not reused, so a
    /// script that tests a code never reads a new meaning into an old number.
    /// </summary>
    ConfigurationInvalid = 2
}
