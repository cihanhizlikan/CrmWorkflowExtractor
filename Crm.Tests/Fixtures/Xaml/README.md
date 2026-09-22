# XAML fixtures — SYNTHETIC

These files are **hand-written from the documented shape of CRM process-designer XAML, not taken from production.**
They exercise the parser's logic. They do not prove the parser matches Anadolu Hayat's real workflows.

Handout §4.2 asks for anonymized production samples covering: a plain background workflow, a real-time workflow, a
child workflow, a workflow calling a partner activity, a wait/timeout, a Business Process Flow and a Dialog. When the
first company run exists, replace or add to these with anonymized copies from `raw/xaml/` (names, GUIDs, URLs and
literals replaced) and keep this note accurate about which files are which.

| File | Covers |
|---|---|
| `condition-update-stop.xaml` | condition with else-branch, update with an option-set value, stop as canceled |
| `child-and-custom.xaml` | child workflow call, partner custom activity with a hardcoded URL and password argument |
| `wait-timeout.xaml` | wait condition with a timeout |
| `unknown-construct.xaml` | a Microsoft activity and an element the parser does not know |
| `production-helpers.xaml` | the helper constructs the first production run reported as unmapped (`ConvertCrmXrmTypes`, `OptionSetValue`, `XrmTimeSpan`, `If`+`RetrieveEntity`, element-form `Postpone`). The construct **names** and where they sit come from that run's coverage report; the markup around them is still written from the documented shape |

Not covered yet, because their XAML shape could not be written credibly without a sample: Business Process Flow,
Dialog, business rule.
| `business-rule.xaml` | a business rule: condition, then show/hide + required level, otherwise error message + lock. **Construct names from the production coverage report; markup guessed** |
| `dialog.xaml` | a dialog: data query, a page of two prompts, a child dialog call. **Construct names from the production coverage report; markup guessed** |
