using Crm.Extract.Http;
using Crm.Similarity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Crm.Cli.Configuration;

/// <summary>Settings bound from the <c>Output</c> section.</summary>
public sealed class OutputOptions
{
    public const string SectionName = "Output";

    /// <summary>Where <c>runs/</c> and <c>cache/</c> are created. A relative path resolves against the executable's folder.</summary>
    public string Root { get; set; } = "out";

    public string ResolvedRoot()
    {
        return Path.IsPathRooted(Root) ? Root : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, Root));
    }
}

/// <summary>Settings bound from the <c>Run</c> section.</summary>
public sealed class RunOptions
{
    public const string SectionName = "Run";

    /// <summary>Fail the run when a §2.4 privilege is held below organization depth. Only ever switched off deliberately.</summary>
    public bool RequireOrganizationReadPrivileges { get; set; } = true;

    /// <summary>
    /// When set to a sealed run id (e.g. <c>20260915-101500</c>), nothing is fetched: that run's raw/ evidence is copied into
    /// a new run and every offline stage is run again over it. This is how the parser is improved against real XAML
    /// away from the company network.
    /// </summary>
    public string ReprocessRunId { get; set; } = "";

    /// <summary>
    /// Path to a <c>crm-export-*.json</c> file saved by <c>tools/crm-browser-export.js</c>. When set, nothing is fetched: the
    /// export becomes this run's raw/ evidence and every stage after retrieval runs over it. A relative path resolves
    /// against the executable's folder.
    /// </summary>
    public string ImportFile { get; set; } = "";

    /// <summary>
    /// Path to a <c>crm-usage-*.json</c> file saved by <c>tools/crm-usage-export.js</c>: the last logged run of every
    /// workflow. Optional; it becomes this run's evidence, and a reprocessed run inherits it from its source run.
    /// </summary>
    public string UsageFile { get; set; } = "";

    /// <summary>
    /// A PNG to put on the guide's cover instead of the built-in logo. Empty means the built-in one. A relative
    /// path resolves against the executable's folder; a file that cannot be read is a warning, not a failure.
    /// </summary>
    public string LogoFile { get; set; } = "";
}

/// <summary>The bound, validated configuration for one run.</summary>
public sealed record ExtractorSettings(IOptions<CrmConnectionOptions> Crm, IOptions<OutputOptions> Output, IOptions<RunOptions> Run, IOptions<SimilarityOptions>? Similarity = null)
{
    /// <summary>
    /// Layers, lowest precedence first: the constants in <c>Program</c>, <c>appsettings.json</c>,
    /// <c>appsettings.Development.json</c>, then <c>CRMEXTRACT_</c>-prefixed environment variables. No command-line
    /// provider: the target host's command prompt is locked down (§10).
    /// </summary>
    public static IConfigurationRoot BuildConfiguration(IReadOnlyDictionary<string, string?> fallback, string baseDirectory)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(fallback)
            .AddJsonFile(Path.Combine(baseDirectory, "appsettings.json"), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine(baseDirectory, "appsettings.Development.json"), optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("CRMEXTRACT_")
            .Build();
    }

    public static ExtractorSettings Bind(IConfiguration configuration)
    {
        CrmConnectionOptions crm = new();
        configuration.GetSection(CrmConnectionOptions.SectionName).Bind(crm);
        OutputOptions output = new();
        configuration.GetSection(OutputOptions.SectionName).Bind(output);
        RunOptions run = new();
        configuration.GetSection(RunOptions.SectionName).Bind(run);
        SimilarityOptions similarity = new();
        configuration.GetSection(SimilarityOptions.SectionName).Bind(similarity);
        return new ExtractorSettings(Options.Create(crm), Options.Create(output), Options.Create(run), Options.Create(similarity));
    }
}
