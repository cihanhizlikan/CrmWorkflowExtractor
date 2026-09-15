using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Crm.Bpmn;

/// <summary>
/// Validates BPMN files against the OMG BPMN 2.0 schemas (§6.2), embedded in this assembly and resolved in-process —
/// no network, no files beside the executable. A file that fails is a failure, not a warning.
/// </summary>
public static class BpmnSchemaValidator
{
    private const string BaseUri = "embedded://schemas/";

    private static readonly Lazy<XmlSchemaSet> Schemas = new(Load);

    public static IReadOnlyList<string> Validate(XDocument document)
    {
        List<string> errors = [];
        XDocument copy = new(document);
        copy.Validate(Schemas.Value, (_, args) =>
        {
            if (args.Severity == XmlSeverityType.Error)
            {
                errors.Add($"line {args.Exception?.LineNumber}: {args.Message}");
            }
        });
        return errors;
    }

    private static XmlSchemaSet Load()
    {
        EmbeddedResolver resolver = new();
        XmlSchemaSet set = new() { XmlResolver = resolver };
        XmlReaderSettings settings = new() { XmlResolver = resolver, DtdProcessing = DtdProcessing.Prohibit };
        using Stream root = resolver.Open("BPMN20.xsd");
        using XmlReader reader = XmlReader.Create(root, settings, BaseUri + "BPMN20.xsd");
        set.Add(null, reader);
        set.Compile();
        return set;
    }

    /// <summary>Resolves the schemas' relative includes and imports to the embedded copies, and refuses anything else.</summary>
    private sealed class EmbeddedResolver : XmlResolver
    {
        public Stream Open(string fileName)
        {
            return typeof(BpmnSchemaValidator).Assembly.GetManifestResourceStream("Crm.Bpmn.Schemas." + fileName)
                ?? throw new FileNotFoundException($"Embedded BPMN schema '{fileName}' is missing from the build.");
        }

        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            string fileName = absoluteUri.Segments[^1];
            return Open(fileName);
        }
    }
}
