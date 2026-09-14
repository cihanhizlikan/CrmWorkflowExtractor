using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Crm.Extract.Runs;

/// <summary>One file of a run and its SHA-256, as recorded in the manifest.</summary>
public sealed record RunArtifact(string Path, long Bytes, string Sha256);

/// <summary>
/// <c>out/runs/&lt;yyyyMMdd-HHmmss&gt;/</c> (§8). The folder name is the UTC start time. <c>manifest.json</c> is
/// written LAST and its presence seals the folder: a sealed folder is never written again, by construction —
/// every write checks for the manifest first.
/// </summary>
public sealed class RunFolder
{
    public const string ManifestFileName = "manifest.json";

    public static readonly string[] Subfolders = ["raw", "ir", "bpmn", "clusters", "consolidated", "manual-review", "reports", "logs"];

    /// <summary>Deterministic JSON for everything this tool writes: indented, LF, readable Turkish, no BOM.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private RunFolder(string root, string runId)
    {
        Root = root;
        RunId = runId;
    }

    public string Root { get; }

    public string RunId { get; }

    public bool IsSealed
    {
        get { return File.Exists(System.IO.Path.Combine(Root, ManifestFileName)); }
    }

    /// <summary>Creates a new run folder. Never reuses an existing one: a clash within the same second gets a suffix.</summary>
    public static RunFolder Create(string outputRoot, DateTimeOffset startedUtc)
    {
        string runs = System.IO.Path.Combine(outputRoot, "runs");
        Directory.CreateDirectory(runs);
        string baseId = startedUtc.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string runId = baseId;
        for (int suffix = 2; Directory.Exists(System.IO.Path.Combine(runs, runId)); suffix++)
        {
            runId = string.Create(CultureInfo.InvariantCulture, $"{baseId}-{suffix}");
        }
        string root = System.IO.Path.Combine(runs, runId);
        foreach (string subfolder in Subfolders)
        {
            Directory.CreateDirectory(System.IO.Path.Combine(root, subfolder));
        }
        return new RunFolder(root, runId);
    }

    public string PathOf(string relative)
    {
        return System.IO.Path.Combine(Root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }

    public async Task WriteTextAsync(string relative, string content, CancellationToken token)
    {
        string path = WritablePath(relative);
        await File.WriteAllTextAsync(path, content.ReplaceLineEndings("\n"), Utf8NoBom, token);
    }

    /// <summary>Writes bytes exactly as given — for verbatim server payloads, whose line endings are evidence.</summary>
    public async Task WriteVerbatimAsync(string relative, string content, CancellationToken token)
    {
        string path = WritablePath(relative);
        await File.WriteAllTextAsync(path, content, Utf8NoBom, token);
    }

    public async Task WriteJsonAsync<T>(string relative, T value, CancellationToken token)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions) + "\n";
        await WriteTextAsync(relative, json, token);
    }

    /// <summary>Hashes every file in the folder, in ordinal path order, then writes the manifest last. After this the folder is read-only by contract.</summary>
    public async Task SealAsync(Func<IReadOnlyList<RunArtifact>, RunManifest> buildManifest, CancellationToken token)
    {
        List<RunArtifact> artifacts = [];
        foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(path => System.IO.Path.GetRelativePath(Root, path).Replace(System.IO.Path.DirectorySeparatorChar, '/'))
            .Where(path => !string.Equals(path, ManifestFileName, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            artifacts.Add(await HashAsync(file, token));
        }
        RunManifest manifest = buildManifest(artifacts);
        string path = WritablePath(ManifestFileName);
        string json = JsonSerializer.Serialize(manifest, JsonOptions) + "\n";
        await File.WriteAllTextAsync(path, json, Utf8NoBom, token);
    }

    private async Task<RunArtifact> HashAsync(string relative, CancellationToken token)
    {
        string path = PathOf(relative);
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, token);
        return new RunArtifact(relative, stream.Length, Convert.ToHexStringLower(hash));
    }

    private string WritablePath(string relative)
    {
        if (IsSealed)
        {
            throw new InvalidOperationException($"Run {RunId} is sealed; its folder is immutable.");
        }
        string path = PathOf(relative);
        string? directory = System.IO.Path.GetDirectoryName(path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }
        return path;
    }
}
