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
 *   - How far back the logs reach is worked out afterwards from the oldest run found, because asking the server for
 *     the oldest record means sorting the whole System Job table, which a production server cannot answer.
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
  const PARALLEL = 6;   // A browser allows six connections to one host; asking for more buys nothing.
  // How far back the aggregate looks. Grouping the whole System Job table did not come back inside a minute and a
  // half on this server; bounded by a date it is a small scan. The cost is honest and stated: a run older than
  // this is not seen, so "no logged run" becomes "nothing since that date", and the reports say exactly that.
  const LOOKBACK_DAYS = 400;
  // The aggregate is one query standing in for eighteen hundred; it may fairly take longer than one of them.
  const AGGREGATE_TIMEOUT_MS = 180000;

  const TIMEOUT_MS = 90000;

  const log = (...args) => console.log("%c[crm-usage]", "color:#06a", ...args);

  async function get(path, prefer) {
    const headers = { "Accept": "application/json", "OData-MaxVersion": "4.0", "OData-Version": "4.0" };
    if (prefer) {
      headers["Prefer"] = prefer;
    }
    // A lookup the server cannot answer quickly is given up on and recorded as a failed lookup, so one slow query
    // cannot stall the whole export.
    const stop = AbortSignal.timeout ? AbortSignal.timeout(TIMEOUT_MS) : undefined;
    const response = await fetch(path.startsWith("http") ? path : WEB_API + path, { credentials: "include", headers, signal: stop });
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

  // The same shape a lookup returns, so the importer cannot tell which way the answer was found: a row with a
  // createdon, or a null row meaning "this one has never run". Only createdon is read on the other side.
  function fromAggregate(newest, id) {
    const at = newest.get(String(id).toLowerCase());
    return { row: at ? { createdon: at } : null };
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

  // One question, asked once. "The newest run of every activation" is a group-by with a max, and CRM answers a
  // FetchXML aggregate in a single GET — against eighteen hundred filtered lookups, one per activation, each of
  // which this server was taking ten seconds to answer. The aggregate has its own limit (the server refuses when
  // it would have to scan too many rows), so a refusal is expected and falls back to asking record by record.
  async function newestBy(entitySet, entity, groupAttribute) {
    const fetchXml = `<fetch aggregate="true"><entity name="${entity}">`
      + `<attribute name="${groupAttribute}" groupby="true" alias="anahtar" />`
      + '<attribute name="createdon" aggregate="max" alias="son" />'
      + `<filter><condition attribute="createdon" operator="last-x-days" value="${LOOKBACK_DAYS}" /></filter>`
      + '</entity></fetch>';
    try {
      const page = await get(`${entitySet}?fetchXml=${encodeURIComponent(fetchXml)}`, undefined, AGGREGATE_TIMEOUT_MS);
      const newest = new Map();
      for (const row of page.value) {
        const key = row.anahtar && typeof row.anahtar === "object" ? row.anahtar.Value ?? row.anahtar.value : row.anahtar;
        if (key && row.son) {
          newest.set(String(key).toLowerCase(), row.son);
        }
      }
      log(`${entity}: one aggregate answered for ${newest.size} record(s), looking back ${LOOKBACK_DAYS} days`);
      return newest;
    } catch (error) {
      log(`${entity}: the aggregate was refused, falling back to one lookup per record —`, error.message);
      return null;
    }
  }

  const scannedSince = new Date(Date.now() - (LOOKBACK_DAYS * 86400000)).toISOString();
  const newestJob = await newestBy("asyncoperations", "asyncoperation", "workflowactivationid");
  const newestSession = await newestBy("processsessions", "processsession", "processid");

  const usage = {};
  let done = 0;
  let queries = 0;
  const startedAt = Date.now();
  async function evidenceFor(definition) {
    const activations = activationsOf.get(definition.workflowid) || [];
    queries += (newestJob ? 0 : activations.length)
      + (definition.category === 1 && !newestSession ? 1 + activations.length : 0);
    const jobs = [];
    for (const activation of activations) {
      jobs.push({
        activationId: activation,
        ...(newestJob ? fromAggregate(newestJob, activation)
          : await first(`asyncoperations?${JOB_COLUMNS}&$filter=_workflowactivationid_value eq ${activation}`))
      });
    }
    const sessions = [];
    if (definition.category === 1) {
      for (const processId of [definition.workflowid, ...activations]) {
        sessions.push({
          processId,
          ...(newestSession ? fromAggregate(newestSession, processId)
            : await first(`processsessions?${SESSION_COLUMNS}&$filter=_processid_value eq ${processId}`))
        });
      }
    }
    usage[definition.workflowid] = { activations, jobs, sessions };
    done++;
    if (done <= 3 || done % 10 === 0 || done === definitions.length) {
      const seconds = Math.round((Date.now() - startedAt) / 1000);
      log(`Usage ${done}/${definitions.length} — ${queries} lookups, ${seconds}s elapsed`);
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
    method: { jobs: newestJob ? "aggregate" : "per-record", sessions: newestSession ? "aggregate" : "per-record" },
    // Only where the aggregate answered: that source saw a window, and nothing before it. Where the per-record
    // path ran, every run ever logged was in reach and there is no window to declare.
    lookback: {
      days: LOOKBACK_DAYS,
      jobsSinceUtc: newestJob ? scannedSince : null,
      sessionsSinceUtc: newestSession ? scannedSince : null
    },
    usage
  };
  const stamp = started.toISOString().replace(/[-:]/g, "").replace("T", "-").slice(0, 15);
  const link = document.createElement("a");
  link.href = URL.createObjectURL(new Blob([JSON.stringify(exported)], { type: "application/json" }));
  link.download = `crm-usage-${stamp}.json`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  const lookups = Object.values(usage).flatMap(entry => [...entry.jobs, ...entry.sessions]);
  const errors = lookups.filter(entry => entry.error).length;
  log(`Done: ${definitions.length} definitions, ${errors} failed lookups. Saved ${link.download}`);
})().catch(error => console.error("[crm-usage] FAILED:", error));
