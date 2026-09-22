/*
 * CRM Workflow Extractor — usage evidence export (format crm-usage-export/1)
 *
 * WHAT IT DOES: for every workflow, reads the most recent run CRM still has a record of, through YOUR signed-in CRM
 * browser session, and saves it as ONE small file, crm-usage-<time>.json. It holds dates, states and ids only — no
 * XAML, no record data.
 *
 *   - Background workflows: the newest System Job (asyncoperation) of each activation.
 *   - Dialogs: the newest dialog session (processsession) of the definition or its activation.
 *   - Real-time workflows leave a System Job only when they fail and error logging is on; business rules run in the
 *     browser and leave nothing. For those, "no record" says nothing at all.
 *   - The oldest System Job and dialog session still present, so the report can say how far back the logs reach.
 *
 * ABSENCE OF A RECORD IS NOT PROOF OF NON-USE: System Jobs are deleted by "delete job when completed" and by bulk
 * deletion jobs. A record found IS proof that the workflow ran.
 *
 * READ-ONLY: every request below is a GET (there is no "method" in this file, so fetch defaults to GET).
 *
 * HOW TO RUN: sign in to CRM, stay on a CRM page, click the "CRM Usage" bookmark (tools/crm-usage.html). Then set
 * Run:UsageFile to the downloaded file and re-run the extractor (with Run:ReprocessRunId or Run:ImportFile).
 */
(async () => {
  "use strict";
  const WEB_API = location.origin + "/api/data/v8.2/";   // IFD host: no organization segment. Change if needed.
  const PAGE_SIZE = 500;
  const PARALLEL = 4;

  const log = (...args) => console.log("%c[crm-usage]", "color:#06a", ...args);

  async function get(path, prefer) {
    const headers = { "Accept": "application/json", "OData-MaxVersion": "4.0", "OData-Version": "4.0" };
    if (prefer) {
      headers["Prefer"] = prefer;
    }
    const response = await fetch(path.startsWith("http") ? path : WEB_API + path, { credentials: "include", headers });
    const text = await response.text();
    if (!response.ok) {
      throw new Error(`GET ${path} -> ${response.status}: ${text.slice(0, 300)}`);
    }
    return JSON.parse(text);
  }

  async function getAll(path) {
    const rows = [];
    const seen = new Set();
    let next = path;
    while (next) {
      if (seen.has(next)) {
        throw new Error("The server repeated a nextLink: " + next);
      }
      seen.add(next);
      const page = await get(next, `odata.maxpagesize=${PAGE_SIZE}`);
      rows.push(...page.value);
      next = page["@odata.nextLink"] || null;
    }
    return rows;
  }

  async function first(path) {
    try {
      const page = await get(path);
      return { row: page.value.length > 0 ? page.value[0] : null };
    } catch (error) {
      return { error: String(error.message || error) };
    }
  }

  const started = new Date();
  log("Web API root:", WEB_API);

  const workflows = await getAll("workflows?$select=workflowid,name,category,type,mode,statecode,_parentworkflowid_value"
    + "&$filter=type eq 1 or type eq 2&$orderby=workflowid");
  const definitions = workflows.filter(row => row.type === 1);
  const activationsOf = new Map();
  for (const row of workflows.filter(row => row.type === 2 && row._parentworkflowid_value)) {
    const list = activationsOf.get(row._parentworkflowid_value) || [];
    list.push(row.workflowid);
    activationsOf.set(row._parentworkflowid_value, list);
  }
  log(`${definitions.length} definitions, ${workflows.length - definitions.length} activations`);

  const JOB_COLUMNS = "$select=createdon,statecode,statuscode&$orderby=createdon desc&$top=1";
  const SESSION_COLUMNS = "$select=createdon,statecode,statuscode&$orderby=createdon desc&$top=1";
  const horizon = {
    oldestWorkflowJob: await first("asyncoperations?$select=createdon&$filter=operationtype eq 10&$orderby=createdon asc&$top=1"),
    oldestDialogSession: await first("processsessions?$select=createdon&$orderby=createdon asc&$top=1")
  };

  const usage = {};
  let done = 0;
  async function evidenceFor(definition) {
    const activations = activationsOf.get(definition.workflowid) || [];
    const jobs = [];
    for (const activation of activations) {
      jobs.push({ activationId: activation, ...await first(`asyncoperations?${JOB_COLUMNS}&$filter=_workflowactivationid_value eq ${activation}`) });
    }
    const sessions = [];
    if (definition.category === 1) {
      for (const processId of [definition.workflowid, ...activations]) {
        sessions.push({ processId, ...await first(`processsessions?${SESSION_COLUMNS}&$filter=_processid_value eq ${processId}`) });
      }
    }
    usage[definition.workflowid] = { activations, jobs, sessions };
    done++;
    if (done % 50 === 0 || done === definitions.length) {
      log(`Usage ${done}/${definitions.length}`);
    }
  }
  for (let index = 0; index < definitions.length; index += PARALLEL) {
    await Promise.all(definitions.slice(index, index + PARALLEL).map(evidenceFor));
  }

  const exported = {
    format: "crm-usage-export/1",
    exportedAtUtc: new Date().toISOString(),
    startedAtUtc: started.toISOString(),
    webApiRoot: WEB_API,
    horizon,
    usage
  };
  const stamp = started.toISOString().replace(/[-:]/g, "").replace("T", "-").slice(0, 15);
  const link = document.createElement("a");
  link.href = URL.createObjectURL(new Blob([JSON.stringify(exported)], { type: "application/json" }));
  link.download = `crm-usage-${stamp}.json`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  const errors = Object.values(usage).flatMap(entry => [...entry.jobs, ...entry.sessions]).filter(entry => entry.error).length;
  log(`Done: ${definitions.length} definitions, ${errors} failed lookups. Saved ${link.download}`);
})().catch(error => console.error("[crm-usage] FAILED:", error));
