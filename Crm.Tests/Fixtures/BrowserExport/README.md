# Browser export fixture — SYNTHETIC

`mock-crm-export.json` was produced on 2026-09-22 by running the real `tools/crm-browser-export.js` in a browser
against a local mock of the CRM 8.2 Web API that served the synthetic XAML fixtures in `../Xaml/`. It contains no
production data. It proves that the file the browser script writes is the file `Run:ImportFile` reads.

Mock contents: 51 workflow records (3 definitions with activations, 44 more definitions, 1 template), one XAML request
that fails with 500 (record `…000000000007`), and a workflow entity without the `businessprocesstype` column.
