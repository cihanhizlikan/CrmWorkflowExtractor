using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Crm.Ir.Model;
using Crm.Ir.Text;

namespace Crm.Ir.Parsing;

/// <summary>
/// Parses designer-authored CRM workflow XAML as plain XML (§4.1) into IR steps. Recognition is by the designer's
/// regular vocabulary: <c>ConditionSequence</c>/<c>ConditionBranch</c> activity references for control flow, step
/// <c>Sequence</c>s named <c>UpdateStep3: …</c> holding an <c>UpdateEntity</c>/<c>CreateEntity</c>/… element, and
/// any element or activity reference from a non-Microsoft assembly as a partner custom activity.
///
/// <para>
/// <b>Written against the documented designer shape, not yet against production samples.</b> Every element is
/// accounted for as mapped, support or unmapped, so where the real XAML differs the coverage report says exactly
/// where — that is how this parser is meant to be corrected.
/// </para>
/// </summary>
public sealed partial class XamlWorkflowParser(OptionLabels labels)
{
    public ParseResult Parse(Guid workflowId, string xaml)
    {
        XDocument document = XDocument.Parse(xaml, LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidDataException("The XAML has no root element.");
        Context context = new(workflowId, labels, ExpressionIndex.Build(root));

        XElement? workflow = root.Descendants().FirstOrDefault(element => element.Name.LocalName == "Workflow"
            && element.Name.NamespaceName.Contains("Microsoft.Xrm.Sdk.Workflow.Activities", StringComparison.Ordinal));
        List<StepNode> steps;
        if (workflow is null)
        {
            context.Warn("", $"No <Workflow> element under <{root.Name.LocalName}>: not a designer-shaped process. Parsed as unmapped.");
            steps = [context.Unmapped(root, "0", "no Workflow element")];
        }
        else
        {
            steps = ParseSequence(workflow.Elements(), "", context);
        }

        foreach (string entity in (workflow ?? root).Descendants()
            .Where(element => element.Name.LocalName == "RetrieveEntity")
            .Select(element => element.Attribute("EntityName")?.Value)
            .OfType<string>())
        {
            context.EntitiesRead.Add(entity);
        }

        List<CoverageObservation> coverage = [];
        List<XamlLiteral> literals = [];
        foreach (XElement element in (workflow ?? root).DescendantsAndSelf())
        {
            string path = context.PathOf(element);
            CollectLiterals(element, path, literals);
            if (element == workflow)
            {
                continue;
            }
            coverage.Add(Observe(element, path, context));
        }

        return new ParseResult(
            steps,
            new WorkflowDependencies([.. context.ChildWorkflows.Order()], [.. context.CustomActivities.Order(StringComparer.Ordinal)]),
            new DataTouched(
                [.. context.EntitiesRead.Order(StringComparer.Ordinal)],
                [.. context.EntitiesWritten.Order(StringComparer.Ordinal)],
                [.. context.FieldsRead.Order(StringComparer.Ordinal)],
                [.. context.FieldsWritten.Order(StringComparer.Ordinal)]),
            context.Warnings,
            coverage,
            literals)
        {
            Parameters = DeclaredParameters(root)
        };
    }

    private static CoverageObservation Observe(XElement element, string path, Context context)
    {
        string construct = XamlNames.ConstructName(element);
        if (context.Anchors.TryGetValue(element, out StepKind kind))
        {
            return new CoverageObservation(construct, kind == StepKind.Unmapped ? CoverageStatus.Unmapped : CoverageStatus.Mapped, path);
        }
        if (XamlNames.IsPropertyElement(element) || XamlNames.Support.Contains(construct) || IsRelatedRecordLoad(element)
            || element.Name.LocalName is "Sequence" && context.InsideStep(element))
        {
            return new CoverageObservation(construct, CoverageStatus.Support, path);
        }
        context.Warn(path, $"Unrecognised element <{element.Name.LocalName}> ({construct}) — not reflected in the IR.");
        return new CoverageObservation(construct, CoverageStatus.Unmapped, path);
    }

    private static List<StepNode> ParseSequence(IEnumerable<XElement> elements, string parentPath, Context context)
    {
        List<StepNode> steps = [];
        foreach (XElement element in elements)
        {
            string path = parentPath.Length == 0
                ? steps.Count.ToString(CultureInfo.InvariantCulture)
                : string.Create(CultureInfo.InvariantCulture, $"{parentPath}/{steps.Count}");
            StepNode? step = ParseActivity(element, path, context);
            if (step is not null)
            {
                steps.Add(step);
            }
        }
        return steps;
    }

    private static StepNode? ParseActivity(XElement element, string path, Context context)
    {
        string? aqn = XamlNames.AssemblyQualifiedName(element);
        if (aqn is not null)
        {
            return ParseActivityReference(element, aqn, path, context);
        }
        if (element.Name.LocalName == "Sequence")
        {
            return ParseStepSequence(element, path, context);
        }
        if (XamlNames.ClientSteps.ContainsKey(element.Name.LocalName))
        {
            return ParseClientStep(element, element.Name.LocalName, path, context);
        }
        if (element.Name.LocalName == "Postpone")
        {
            // The element form, as the production server writes it; the fixtures' ActivityReference form is above.
            return context.Node(element, path, StepKind.Timeout, XamlNames.DisplayName(element), null, [], [], TimeoutDetail(element), [], "Postpone");
        }
        if (XamlNames.StepEvidence.Contains(element.Name.LocalName) || XamlNames.IsCustomActivityElement(element))
        {
            return FromEvidence(element, element, path, context);
        }
        if (XamlNames.IsPropertyElement(element) || XamlNames.Support.Contains(element.Name.LocalName) || IsRelatedRecordLoad(element))
        {
            return null;
        }
        return context.Unmapped(element, path, element.Name.LocalName);
    }

    private static StepNode? ParseActivityReference(XElement element, string aqn, string path, Context context)
    {
        string type = XamlNames.ShortTypeName(aqn);
        if (!XamlNames.IsMicrosoftAssembly(XamlNames.AssemblyOf(aqn)))
        {
            return FromEvidence(element, element, path, context);
        }
        return type switch
        {
            "ConditionSequence" => ParseCondition(element, path, context),
            "Postpone" => context.Node(element, path, StepKind.Timeout, XamlNames.DisplayName(element), null, [], [], TimeoutDetail(element), [], type),
            "Composite" => context.Node(element, path, StepKind.Sequence, XamlNames.DisplayName(element), null, [], [], null, [], type)
                with
            { Branches = [new Branch("", null, ParseSequence(Activities(element), path, context), [])] },
            "EvaluateCondition" or "EvaluateLogicalCondition" or "EvaluateExpression" or "ConvertCrmXrmTypes" => null,
            _ when XamlNames.ClientSteps.ContainsKey(type) => ParseClientStep(element, type, path, context),
            _ => context.Unmapped(element, path, type)
        };
    }

    /// <summary>A designer step: a named <c>Sequence</c> whose children hold one evidence element, or a plain grouping sequence.</summary>
    private static StepNode ParseStepSequence(XElement sequence, string path, Context context)
    {
        XElement? evidence = sequence.Elements().FirstOrDefault(child => XamlNames.StepEvidence.Contains(child.Name.LocalName)
            || XamlNames.IsCustomActivityElement(child)
            || (XamlNames.AssemblyQualifiedName(child) is string aqn && !XamlNames.IsMicrosoftAssembly(XamlNames.AssemblyOf(aqn))));

        // A designer "wait N days, then …" step writes the wait beside the action it delays, inside one Sequence.
        // The wait is a step of its own in the model: without it the diagram claims the action happens at once.
        List<XElement> waits = [.. sequence.Elements().Where(child => child.Name.LocalName == "Postpone")];
        if (waits.Count > 0)
        {
            StepNode group = context.Node(sequence, path, StepKind.Sequence, StepDisplayName(sequence), null, [], [], null, [], "Sequence");
            List<StepNode> steps = [];
            foreach (XElement wait in waits)
            {
                string waitPath = string.Create(CultureInfo.InvariantCulture, $"{path}/{steps.Count}");
                steps.Add(context.Node(wait, waitPath, StepKind.Timeout, XamlNames.DisplayName(wait), null, [], [], TimeoutDetail(wait), [], "Postpone"));
            }
            if (evidence is not null)
            {
                // Anchors the sequence a second time, which is what coverage should report: the action, not the group.
                steps.Add(FromEvidence(sequence, evidence, string.Create(CultureInfo.InvariantCulture, $"{path}/{steps.Count}"), context));
            }
            return group with { Branches = [new Branch("", null, steps, [])] };
        }

        if (evidence is not null)
        {
            return FromEvidence(sequence, evidence, path, context);
        }

        bool groupsSteps = sequence.Elements().Any(child => child.Name.LocalName == "Sequence" || XamlNames.AssemblyQualifiedName(child) is not null
            || XamlNames.ClientSteps.ContainsKey(child.Name.LocalName));
        if (groupsSteps)
        {
            StepNode group = context.Node(sequence, path, StepKind.Sequence, XamlNames.DisplayName(sequence), null, [], [], null, [], "Sequence");
            return group with { Branches = [new Branch("", null, ParseSequence(sequence.Elements(), path, context), [])] };
        }
        string hint = XamlNames.StepName(XamlNames.DisplayName(sequence))?.Kind ?? "unnamed";
        return context.Unmapped(sequence, path, $"Sequence({hint})");
    }

    private static StepNode FromEvidence(XElement anchor, XElement evidence, string path, Context context)
    {
        string displayName = StepDisplayName(anchor);
        string construct = XamlNames.ConstructName(evidence);
        context.Anchors[evidence] = StepKind.Sequence;
        string? entity = evidence.Attribute("EntityName")?.Value;
        List<FieldWrite> fields = FieldWrites(anchor, entity, context);

        if (XamlNames.IsCustomActivityElement(evidence) || XamlNames.AssemblyQualifiedName(evidence) is not null)
        {
            (string typeName, IReadOnlyList<NamedArgument> arguments) = CustomActivity(evidence, context);
            context.CustomActivities.Add(typeName);
            context.Anchors[evidence] = StepKind.CustomActivity;
            return context.Node(anchor, path, StepKind.CustomActivity, displayName, entity, fields, [], typeName, arguments, construct);
        }

        StepKind kind = evidence.Name.LocalName switch
        {
            "UpdateEntity" => StepKind.UpdateRecord,
            "CreateEntity" => StepKind.CreateRecord,
            "AssignEntity" => StepKind.AssignRecord,
            "SetState" => StepKind.ChangeStatus,
            "SendEmail" or "SendEmailFromTemplate" => StepKind.SendEmail,
            "StartChildWorkflow" => StepKind.StartChildWorkflow,
            _ => StepKind.StopWorkflow
        };
        context.Anchors[evidence] = kind;
        string? detail = kind switch
        {
            StepKind.StartChildWorkflow => ChildWorkflow(evidence, context),
            StepKind.StopWorkflow => evidence.Attributes().Any(attribute => attribute.Value.Contains("OperationStatus.Canceled", StringComparison.Ordinal)) ? "Canceled" : "Succeeded",
            StepKind.ChangeStatus => StatusDetail(evidence, context),
            _ => null
        };
        if (entity is not null && kind is StepKind.UpdateRecord or StepKind.CreateRecord or StepKind.AssignRecord or StepKind.ChangeStatus)
        {
            context.EntitiesWritten.Add(entity);
        }
        return context.Node(anchor, path, kind, displayName, entity, fields, [], detail, [], construct);
    }

    private static StepNode ParseCondition(XElement conditionSequence, string path, Context context)
    {
        bool wait = string.Equals(ArgumentText(conditionSequence, "Wait")?.Trim(), "True", StringComparison.OrdinalIgnoreCase);
        StepKind kind = wait ? StepKind.WaitCondition : StepKind.Condition;
        StepNode node = context.Node(conditionSequence, path, kind, StepDisplayName(conditionSequence), null, [], [], null, [], "ConditionSequence");

        List<Branch> branches = [];
        foreach (XElement child in Activities(conditionSequence))
        {
            string? aqn = XamlNames.AssemblyQualifiedName(child);
            if (aqn is not null && XamlNames.ShortTypeName(aqn) == "ConditionBranch")
            {
                AddBranches(child, path, branches, context);
            }
            else if ((aqn is not null && XamlNames.ShortTypeName(aqn) == "Postpone") || (aqn is null && child.Name.LocalName == "Postpone"))
            {
                string branchPath = string.Create(CultureInfo.InvariantCulture, $"{path}/{branches.Count}");
                StepNode timeout = context.Node(child, branchPath + "/0", StepKind.Timeout, XamlNames.DisplayName(child), null, [], [], TimeoutDetail(child), [], "Postpone");
                branches.Add(new Branch("Timeout", null, [timeout], []));
            }
        }
        if (branches.Count == 0)
        {
            context.Warn(path, "A condition with no recognisable branches.");
        }
        string? description = XamlNames.Keyed(conditionSequence, "Properties", "Description")?.Value is string written
            ? HtmlEntities.Decode(written)
            : null;
        return node with { Branches = branches, Detail = description };
    }

    /// <summary>A <c>ConditionBranch</c> gives its own branch; its <c>Else</c> is nothing, an otherwise-branch, or the next else-if.</summary>
    private static void AddBranches(XElement conditionBranch, string conditionPath, List<Branch> branches, Context context)
    {
        context.Anchors[conditionBranch] = StepKind.Condition;
        string branchPath = string.Create(CultureInfo.InvariantCulture, $"{conditionPath}/{branches.Count}");
        Predicate? predicate = context.PredicateOf(ExpressionIndex.VariableOf(ArgumentText(conditionBranch, "Condition")), conditionPath);
        XElement? then = XamlNames.Keyed(conditionBranch, "Properties", "Then");
        IReadOnlyList<StepNode> thenSteps = then is null ? [] : ParseSequence(Activities(then), branchPath, context);
        branches.Add(new Branch(predicate?.Text ?? "Condition", predicate, thenSteps, []));

        XElement? otherwise = XamlNames.Keyed(conditionBranch, "Properties", "Else");
        if (otherwise is null || otherwise.Name.LocalName == "Null")
        {
            return;
        }
        string? elseType = XamlNames.AssemblyQualifiedName(otherwise) is string aqn ? XamlNames.ShortTypeName(aqn) : null;
        if (elseType == "ConditionBranch")
        {
            AddBranches(otherwise, conditionPath, branches, context);
            return;
        }
        string otherwisePath = string.Create(CultureInfo.InvariantCulture, $"{conditionPath}/{branches.Count}");
        branches.Add(new Branch("Otherwise", null, ParseSequence(Activities(otherwise), otherwisePath, context), []));
    }

    private static List<FieldWrite> FieldWrites(XElement step, string? entity, Context context)
    {
        List<FieldWrite> fields = [];
        foreach (XElement set in step.Descendants().Where(element => element.Name.LocalName == "SetEntityProperty"))
        {
            string? attribute = set.Attribute("Attribute")?.Value;
            string? setEntity = set.Attribute("EntityName")?.Value ?? entity;
            if (attribute is null)
            {
                continue;
            }
            // Value="[UpdateStep1_2]" (attribute form) or <SetEntityProperty.Value>…ExpressionText="UpdateStep1_2"… (element form).
            string? variable = ExpressionIndex.VariableOf(set.Attribute("Value")?.Value) ?? set.Descendants()
                .Select(value => value.Attribute("ExpressionText")?.Value ?? (value.HasElements ? null : value.Value))
                .Select(ExpressionIndex.VariableOf)
                .FirstOrDefault(name => name is not null);
            IReadOnlyList<LiteralValue> values = [.. context.Index.ValuesOf(variable).Select(raw => new LiteralValue(raw, context.Labels.Resolve(setEntity, attribute, raw)))];
            fields.Add(new FieldWrite(attribute, values));
            if (setEntity is not null)
            {
                context.FieldsWritten.Add($"{setEntity}.{attribute}");
            }
        }
        return [.. fields.OrderBy(field => field.Field, StringComparer.Ordinal)];
    }

    /// <summary>A dialog page, dialog query, child dialog or business-rule action: its arguments captured verbatim.</summary>
    private static StepNode ParseClientStep(XElement element, string construct, string path, Context context)
    {
        (StepKind kind, string? action) = XamlNames.ClientSteps[construct];
        string? entity = element.Attribute("EntityName")?.Value;
        List<NamedArgument> arguments = [.. CapturedArguments(element, context)];
        if (construct == "InteractionPage")
        {
            // A page holds the prompts shown together; each becomes Prompt1.*, Prompt2.* on the page.
            int index = 0;
            foreach (XElement interaction in element.Descendants().Where(descendant => XamlNames.ConstructName(descendant) == "Interaction"))
            {
                index++;
                context.Anchors[interaction] = kind;
                string prefix = string.Create(CultureInfo.InvariantCulture, $"Prompt{index}.");
                arguments.AddRange(CapturedArguments(interaction, context).Select(argument => argument with { Name = prefix + argument.Name }));
            }
        }
        string? detail = kind switch
        {
            StepKind.StartChildWorkflow => ChildWorkflow(element, context),
            StepKind.FormAction => action,
            _ => null
        };
        if (entity is not null && kind == StepKind.DataQuery)
        {
            context.EntitiesRead.Add(entity);
        }
        return context.Node(element, path, kind, StepDisplayName(element), entity, FieldWrites(element, entity, context), [], detail, arguments, construct);
    }

    /// <summary>
    /// Every argument of an activity, verbatim: its attributes, its keyed <c>.Arguments</c>/<c>.Properties</c> entries
    /// (the ActivityReference form) and its property elements (the element form). Child activity collections are skipped.
    /// </summary>
    private static List<NamedArgument> CapturedArguments(XElement evidence, Context context)
    {
        List<NamedArgument> arguments = [];
        foreach (XAttribute attribute in evidence.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration
            && attribute.Name.Namespace == XNamespace.None && attribute.Name.LocalName is not ("DisplayName" or "AssemblyQualifiedName")))
        {
            arguments.Add(new NamedArgument(attribute.Name.LocalName, ArgumentValue(attribute.Value, context)));
        }
        foreach (XElement property in evidence.Elements().Where(XamlNames.IsPropertyElement))
        {
            string localName = property.Name.LocalName;
            if (localName.EndsWith(".Arguments", StringComparison.Ordinal) || localName.EndsWith(".Properties", StringComparison.Ordinal))
            {
                // ActivityReference form: <X.Arguments><InArgument x:Key="Url">…</InArgument></X.Arguments>
                foreach (XElement argument in property.Elements().Where(argument => XamlNames.Key(argument) is not ("Activities" or "Variables")))
                {
                    arguments.Add(new NamedArgument(XamlNames.Key(argument) ?? argument.Name.LocalName, ArgumentValue(Flatten(argument), context))
                    {
                        Output = Writes(argument.Name.LocalName)
                    });
                }
                continue;
            }
            // Element form: <partner:CallService.Url><InArgument>…</InArgument></partner:CallService.Url>
            string name = localName[(localName.IndexOf('.', StringComparison.Ordinal) + 1)..];
            arguments.Add(new NamedArgument(name, ArgumentValue(Flatten(property), context))
            {
                Output = property.Elements().Select(child => Writes(child.Name.LocalName)).FirstOrDefault(known => known is not null)
            });
        }
        return [.. arguments.OrderBy(argument => argument.Name, StringComparer.Ordinal)];
    }

    /// <summary>Which way an argument goes, from the element that carries it. Null where the shape does not say.</summary>
    private static bool? Writes(string localName)
    {
        return localName switch
        {
            "OutArgument" or "InOutArgument" => true,
            "InArgument" => false,
            _ => null
        };
    }

    /// <summary>
    /// What the workflow ITSELF takes and returns, from the <c>x:Members</c> block every definition opens with.
    /// On a plain workflow these are CRM's plumbing and are dropped; on an ACTION they are its signature — the
    /// only description of what it is for that exists outside the compiled code it calls.
    /// </summary>
    private static IReadOnlyList<WorkflowParameter> DeclaredParameters(XElement root)
    {
        List<WorkflowParameter> parameters = [];
        foreach (XElement property in root.Elements().Where(element => element.Name.LocalName == "Members")
            .SelectMany(members => members.Elements().Where(element => element.Name.LocalName == "Property")))
        {
            string? name = property.Attribute("Name")?.Value;
            string type = property.Attribute("Type")?.Value ?? "";
            if (name is null || WorkflowPlumbing.Names.Contains(name))
            {
                continue;
            }
            bool output = type.StartsWith("OutArgument", StringComparison.Ordinal) || type.StartsWith("InOutArgument", StringComparison.Ordinal);
            parameters.Add(new WorkflowParameter(name, ShortArgumentType(type), output));
        }
        return parameters;
    }

    /// <summary>
    /// <c>InArgument(x:String)</c> → <c>String</c>. The wrapper says the direction, which is kept separately, and
    /// the namespace prefix is XAML's own bookkeeping; what is left is the type a reader needs.
    /// </summary>
    private static string ShortArgumentType(string type)
    {
        int open = type.IndexOf('(', StringComparison.Ordinal);
        string inner = open < 0 ? type : type[(open + 1)..].TrimEnd(')');
        int colon = inner.LastIndexOf(':');
        return colon < 0 ? inner : inner[(colon + 1)..];
    }

    /// <summary>An argument's text; when it has none (a literal written as attributes), its descendants' attributes.</summary>
    private static string Flatten(XElement argument)
    {
        string text = argument.Value.Trim();
        if (text.Length > 0)
        {
            return text;
        }
        return string.Join(" ", argument.Descendants().SelectMany(element => element.Attributes())
            .Where(attribute => !attribute.IsNamespaceDeclaration && attribute.Name.Namespace == XNamespace.None)
            .Select(attribute => $"{attribute.Name.LocalName}={attribute.Value}"));
    }

    private static (string TypeName, IReadOnlyList<NamedArgument> Arguments) CustomActivity(XElement evidence, Context context)
    {
        string? aqn = XamlNames.AssemblyQualifiedName(evidence);
        string typeName;
        if (aqn is not null)
        {
            typeName = aqn;
        }
        else
        {
            (string clrNamespace, string assembly) = XamlNames.ClrNamespaceOf(evidence.Name.Namespace)!.Value;
            typeName = $"{clrNamespace}.{evidence.Name.LocalName}, {assembly}";
        }

        return (typeName, CapturedArguments(evidence, context));
    }

    /// <summary>A custom activity argument, verbatim — with a designer variable replaced by the literal it holds, when known.</summary>
    private static string ArgumentValue(string raw, Context context)
    {
        string? variable = ExpressionIndex.VariableOf(raw);
        if (variable is null || !context.Index.Literals.TryGetValue(variable, out IReadOnlyList<string>? values))
        {
            return HtmlEntities.Decode(raw);
        }
        return string.Join(", ", values);
    }

    private static string? ChildWorkflow(XElement evidence, Context context)
    {
        Match match = evidence.Attributes().Select(attribute => GuidPattern().Match(attribute.Value)).FirstOrDefault(found => found.Success)
            ?? GuidPattern().Match(evidence.Value);
        if (match.Success && Guid.TryParse(match.Value, out Guid child))
        {
            context.ChildWorkflows.Add(child);
            return child.ToString("D");
        }
        context.Warn(context.PathOf(evidence), "A child workflow call whose target id could not be read.");
        return null;
    }

    /// <summary>
    /// Which state a <c>SetState</c> step moves the record to.
    ///
    /// <para>
    /// A status change is not a field write — it is CRM's own <c>SetState</c> message, the only supported way to
    /// move a record between Active, Inactive, Resolved and the rest, and it sets <c>statecode</c> and
    /// <c>statuscode</c> together as a pair. So the state does not arrive as a <see cref="FieldWrite"/> and has to
    /// be read from the step itself.
    /// </para>
    ///
    /// <para>
    /// It arrives TWO ways, and only one of them was being read. A state the designer built through an expression
    /// is a variable, which the index resolves. A state typed straight into the designer is a literal, written as
    /// a nested <c>&lt;mxs:OptionSetValue Value="1" /&gt;</c> under <c>SetState.State</c> — no variable anywhere,
    /// so the box came out reading "Durum değiştir: phonecall" and never said what it changed the status TO
    /// (maintainer, 2026-10-08). The number is resolved to its own label where metadata has one.
    /// </para>
    /// </summary>
    private static string? StatusDetail(XElement evidence, Context context)
    {
        List<string> values = [.. evidence.DescendantsAndSelf()
            .SelectMany(element => element.Attributes().Select(attribute => attribute.Value).Append(element.HasElements ? "" : element.Value))
            .SelectMany(ExpressionIndex.VariablesIn)
            .SelectMany(context.Index.ValuesOf)
            .Where(value => value != ExpressionIndex.Dynamic)
            .Distinct(StringComparer.Ordinal)];
        values.AddRange(Literals(evidence, context.Labels).Where(value => !values.Contains(value, StringComparer.Ordinal)));
        return values.Count == 0 ? null : string.Join(", ", values);
    }

    /// <summary>
    /// The states written as literals on the step, named as a reader would say them. <c>SetState.State</c> carries
    /// <c>statecode</c> and <c>SetState.Status</c> carries <c>statuscode</c>, so each number is resolved against
    /// the attribute it actually belongs to.
    /// </summary>
    private static IEnumerable<string> Literals(XElement evidence, OptionLabels labels)
    {
        string? entity = evidence.Attribute("EntityName")?.Value;
        foreach (XElement property in evidence.DescendantsAndSelf())
        {
            string attribute = property.Name.LocalName switch
            {
                "SetState.State" => "statecode",
                "SetState.Status" => "statuscode",
                _ => ""
            };
            if (attribute.Length == 0)
            {
                continue;
            }
            foreach (XElement option in property.Descendants().Where(child => child.Name.LocalName == "OptionSetValue"))
            {
                if (option.Attribute("Value")?.Value is not string raw)
                {
                    continue;
                }
                string? label = labels.Resolve(entity, attribute, raw);
                yield return label is null ? raw : $"{label} ({raw})";
            }
        }
    }

    private static string? TimeoutDetail(XElement postpone)
    {
        string? until = ArgumentText(postpone, "PostponeUntil")
            ?? postpone.Attribute("PostponeUntil")?.Value
            ?? postpone.Elements().FirstOrDefault(child => child.Name.LocalName == "Postpone.PostponeUntil")?.Value;
        return until is null ? null : until.Trim();
    }

    /// <summary>
    /// <c>&lt;If&gt;</c> whose only work is <c>RetrieveEntity</c>: the designer loading a related record (the owner of the
    /// case, say) so later steps can read its fields. Machinery, not a step. Any other <c>If</c> stays unmapped.
    /// </summary>
    private static bool IsRelatedRecordLoad(XElement element)
    {
        if (element.Name.LocalName != "If")
        {
            return false;
        }
        List<XElement> work = [.. element.Descendants().Where(descendant => !XamlNames.IsPropertyElement(descendant)
            && !XamlNames.Support.Contains(XamlNames.ConstructName(descendant)))];
        return work.Count == 0 && element.Descendants().Any(descendant => descendant.Name.LocalName == "RetrieveEntity");
    }

    private static string StepDisplayName(XElement element)
    {
        string displayName = XamlNames.DisplayName(element);
        return XamlNames.StepName(displayName)?.Description ?? displayName;
    }

    private static IEnumerable<XElement> Activities(XElement activityReference)
    {
        return XamlNames.Keyed(activityReference, "Properties", "Activities")?.Elements() ?? [];
    }

    private static string? ArgumentText(XElement activityReference, string key)
    {
        return XamlNames.Keyed(activityReference, "Arguments", key)?.Value;
    }

    private static void CollectLiterals(XElement element, string path, List<XamlLiteral> literals)
    {
        foreach (XAttribute attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
        {
            literals.Add(new XamlLiteral(path, HtmlEntities.Decode(attribute.Value)));
        }
        foreach (XText text in element.Nodes().OfType<XText>())
        {
            literals.Add(new XamlLiteral(path, HtmlEntities.Decode(text.Value)));
        }
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    /// <summary>Per-parse state: step anchors for coverage, what the workflow reads and writes, and warnings.</summary>
    private sealed class Context(Guid workflowId, OptionLabels labels, ExpressionIndex index)
    {
        public OptionLabels Labels { get; } = labels;

        public ExpressionIndex Index { get; } = index;

        public Dictionary<XElement, StepKind> Anchors { get; } = [];

        public Dictionary<XElement, string> AnchorPaths { get; } = [];

        public List<ParseWarning> Warnings { get; } = [];

        public HashSet<Guid> ChildWorkflows { get; } = [];

        public HashSet<string> CustomActivities { get; } = new(StringComparer.Ordinal);

        public HashSet<string> EntitiesRead { get; } = new(StringComparer.Ordinal);

        public HashSet<string> EntitiesWritten { get; } = new(StringComparer.Ordinal);

        public HashSet<string> FieldsRead { get; } = new(StringComparer.Ordinal);

        public HashSet<string> FieldsWritten { get; } = new(StringComparer.Ordinal);

        public StepNode Node(XElement anchor, string path, StepKind kind, string displayName, string? entity, IReadOnlyList<FieldWrite> fields,
            IReadOnlyList<Branch> branches, string? detail, IReadOnlyList<NamedArgument> arguments, string construct)
        {
            Anchors[anchor] = kind;
            AnchorPaths[anchor] = path;
            return new StepNode(path, kind, displayName, entity, fields, branches, detail, arguments, construct, [new StepSource(workflowId, path)]);
        }

        public StepNode Unmapped(XElement anchor, string path, string construct)
        {
            Warn(path, $"Unmapped construct '{construct}'.");
            return Node(anchor, path, StepKind.Unmapped, XamlNames.DisplayName(anchor), null, [], [], null, [], construct);
        }

        public void Warn(string path, string message)
        {
            Warnings.Add(new ParseWarning(path, message));
        }

        public string PathOf(XElement element)
        {
            for (XElement? current = element; current is not null; current = current.Parent)
            {
                if (AnchorPaths.TryGetValue(current, out string? path))
                {
                    return path;
                }
            }
            return "";
        }

        public bool InsideStep(XElement element)
        {
            return element.Ancestors().Any(AnchorPaths.ContainsKey);
        }

        public Predicate? PredicateOf(string? variable, string path)
        {
            if (variable is null)
            {
                Warn(path, "A branch condition that does not refer to a condition variable.");
                return null;
            }
            if (Index.Logicals.TryGetValue(variable, out LogicalCombination? logical))
            {
                Predicate? left = PredicateOf(logical.Left, path);
                Predicate? right = PredicateOf(logical.Right, path);
                string word = ConditionWords.Join(logical.Operator);
                return new Predicate($"({left?.Text ?? Subject(logical.Left)}) {word} ({right?.Text ?? Subject(logical.Right)})", left?.Entity, left?.Attribute, logical.Operator,
                    [.. (left?.Values ?? []).Concat(right?.Values ?? [])]);
            }
            if (!Index.Comparisons.TryGetValue(variable, out Comparison? comparison))
            {
                Warn(path, $"Condition variable '{variable}' has no recognisable comparison.");
                return null;
            }
            AttributeRead? read = Index.Reads.GetValueOrDefault(comparison.OperandVariable);
            if (read is not null)
            {
                EntitiesRead.Add(read.Entity);
                FieldsRead.Add($"{read.Entity}.{read.Attribute}");
            }
            List<LiteralValue> values = [.. comparison.ParameterVariables
                .SelectMany(Index.ValuesOf)
                .Select(raw => new LiteralValue(raw, Labels.Resolve(read?.Entity, read?.Attribute, raw)))];
            string shown = string.Join(", ", values.Select(value => value.Resolved is null ? value.Raw : $"{value.Resolved} ({value.Raw})"));
            string subject = Subject(comparison.OperandVariable);
            string text = string.Join(" ", new[] { subject, ConditionWords.Of(comparison.Operator), shown }.Where(part => part.Length > 0));
            return new Predicate(text, read?.Entity, read?.Attribute, comparison.Operator, values) { Subject = subject };
        }

        /// <summary>
        /// What a condition compares, named as well as the definition allows: the field it read, else the activity
        /// and output argument that produced the value, else the designer's own variable. The variable is a poor
        /// name but a traceable one — the diagram used to print a bare question mark here, which named nothing.
        /// </summary>
        private string Subject(string variable)
        {
            if (Index.Reads.GetValueOrDefault(variable) is AttributeRead read)
            {
                return $"{read.Entity}.{read.Attribute}";
            }
            if (Index.Writers.GetValueOrDefault(variable) is VariableSource source)
            {
                return source.Text;
            }
            return variable;
        }
    }
}
