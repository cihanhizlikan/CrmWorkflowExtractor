namespace Crm.Cli;

/// <summary>Process exit codes. Anything but 0 means the run folder must not be trusted as complete.</summary>
public enum ExitCode
{
    Success = 0,

    /// <summary>A reconciliation, privilege or request failure. The run folder is sealed with status <c>failed</c>.</summary>
    RunFailed = 1,

    /// <summary>Configuration is invalid; nothing touched the network and no run folder was created.</summary>
    ConfigurationInvalid = 2,

    /// <summary>§2.2: the server looks internet-facing (IFD). Stopped and reported.</summary>
    InternetFacingDeployment = 3,

    /// <summary>The server could not be reached after bounded retries.</summary>
    ServerUnreachable = 4,

    /// <summary>The server offers Windows authentication and rejected the credentials.</summary>
    AuthenticationRejected = 5
}
