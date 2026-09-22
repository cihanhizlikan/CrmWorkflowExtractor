# CRM Web API service document — 2026-09-22

`crm-service-document-2026-09-22.json` is the maintainer's first successful read of the production Web API root,
`https://ahecrm.anadoluhayat.com.tr/api/data/v8.2/`, opened in a browser with the newly authorized account.

**Faithfulness:** the pasted text was missing its opening `{`. The file was rebuilt from the pasted entries; every
entry had `kind` = `EntitySet` and `url` equal to `name`, so the content is the same. 469 entity sets.

## What it confirms

- **Web API v8.2 answers**, and the account can read the service document.
- **Every entity set the tool reads exists:** `workflows`, `processstages`, `privileges`, `systemusers`,
  `sdkmessageprocessingsteps`, `sdkmessagefilters`, `sdkmessages`, `pluginassemblies`, `plugintypes`,
  `asyncoperations`, `workflowlogs`, `roles`, `EntityDefinitions`. (Functions such as `WhoAmI()` are never listed in
  a service document, so their absence here says nothing.)

## What it raises

1. **The URL has no organization segment** (`/api/data/v8.2/` directly under the host). On-premises Windows
   authentication normally uses `https://<server>/<org>/api/data/…`; a host-per-organization URL is the usual shape
   of an **internet-facing (IFD / claims) deployment**. Not certain: an on-premises server can also be bound to one
   organization per host name. **Settle it before the first run:** did the browser open the page silently (Windows
   authentication) or show an AD FS sign-in page? The tool detects IFD and stops with exit code 3.
2. **14 Business Process Flow entity sets** (`msdyn_bpf_*` ×12, `new_bpf_*` ×2) — BPFs are in use. The parser does
   not map BPF XAML yet; expect them as unmapped in `parse-coverage.md`.
3. **`north52_*` (8 sets, incl. `north52_formulas`)** — North52 Business Process Activities, a third-party rules
   engine. Logic written as North52 formulas runs through plugins and is **not in any workflow XAML**. If the partner
   used it, part of the business process is invisible to this tool.
4. **`ps_*` (128 sets)** — the implementation partner's own entities (policies, pension contracts, cases, leads…).
   Workflows will mostly run on these; `ps_` is the likely prefix of their custom workflow activities too.
5. **`uii_workflows`, `uii_workflowsteps`, `msdyusd_*`** — Unified Service Desk configuration. `uii_workflows` are
   USD agent workflows, **not** CRM processes; they are not in `workflows` and the tool rightly ignores them.
6. **`gsc_*`** — another add-on (form/option-set tooling, a password generator). Unknown vendor; not process logic.
7. **`workflowlogs` and `asyncoperations`** hold execution history. Not used by the tool, but they could show which
   workflows actually run — a strong signal for the architects when retiring redundant ones.
