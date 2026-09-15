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

Not covered yet, because their XAML shape could not be written credibly without a sample: Business Process Flow,
Dialog, business rule.
