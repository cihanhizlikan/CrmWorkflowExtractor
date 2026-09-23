/*
 * CRM Workflow Extractor — browser export (format crm-browser-export/1)
 *
 * WHAT IT DOES: reads, through YOUR signed-in CRM browser session, everything the extractor would fetch itself —
 * who you are, your privileges, the workflow inventory and its count, the XAML of every definition and activation,
 * option-set labels and Business Process Flow stages — and saves it as ONE file, crm-export-<time>.json.
 *
 * READ-ONLY: every request below is a GET (search this file for "method": there is none, so fetch defaults to GET).
 * Nothing is created, changed or deleted in CRM.
 *
 * HOW TO RUN:
 *   1. Sign in to CRM as usual and stay on a CRM page (e.g. https://ahecrm.anadoluhayat.com.tr/main.aspx).
 *   2. Press F12 → "Console" tab. (If the browser says pasting is blocked, type:  allow pasting  and press Enter.)
 *   3. Paste this whole file and press Enter. Progress is printed; the download starts when it finishes.
 *   4. Keep the file somewhere safe: it contains production XAML, which may hold URLs or passwords.
 *      Do NOT email it around or paste it into a chat. Point the extractor at it with Run:ImportFile.
 */
(async () => {
  "use strict";
  const WEB_API = location.origin + "/api/data/v8.2/";   // IFD host: no organization segment. Change if needed.
  const PAGE_SIZE = 20;
  const PARALLEL_XAML = 4;
  // No request waits forever. A locked-down 8.2 server can leave one pending with no answer and no error, and
  // then the console shows nothing at all — which is what a reader takes for "the script is broken".
  const TIMEOUT_MS = 120000;
  // The count is the first thing asked and the least important: this server has already been seen answering it
  // with -1, and there is a FetchXML aggregate behind it. It is not worth two minutes of anyone's morning.
  const COUNT_TIMEOUT_MS = 20000;

  // The extractor's §3.1 inventory columns (everything except xaml). Checked against metadata below.
  const COLUMNS = ["workflowid", "name", "uniquename", "description", "category", "type", "mode", "scope", "statecode",
    "statuscode", "primaryentity", "ondemand", "subprocess", "asyncautodelete", "istransacted", "syncworkflowlogonfailure",
    "rank", "runas", "triggeroncreate", "triggerondelete", "triggeronupdateattributelist", "createstage", "updatestage",
    "deletestage", "businessprocesstype", "processorder", "languagecode", "iscrmuiworkflow", "ismanaged",
    "componentstate", "_parentworkflowid_value", "_activeworkflowid_value", "createdon", "modifiedon",
    "_createdby_value", "_modifiedby_value", "_ownerid_value", "versionnumber"];
  const PRIVILEGES = ["prvReadWorkflow", "prvReadProcessStage", "prvReadAsyncOperation", "prvReadPluginAssembly",
    "prvReadPluginType", "prvReadSdkMessageProcessingStep"];

  const log = (...args) => console.log("%c[crm-export]", "color:#0a6", ...args);

  async function get(path, prefer, timeoutMs) {
    const headers = { "Accept": "application/json", "OData-MaxVersion": "4.0", "OData-Version": "4.0" };
    if (prefer) {
      headers["Prefer"] = prefer;
    }
    let response;
    try {
      response = await fetch(path.startsWith("http") ? path : WEB_API + path,
        { credentials: "include", headers, signal: AbortSignal.timeout(timeoutMs ?? TIMEOUT_MS) });
    } catch (error) {
      throw new Error(error.name === "TimeoutError"
        ? `GET ${path} -> cevap gelmedi (${(timeoutMs ?? TIMEOUT_MS) / 1000} sn)`
        : `GET ${path} -> ${error.message}`);
    }
    const text = await response.text();
    if (!response.ok) {
      throw new Error(`GET ${path} -> ${response.status}: ${text.slice(0, 300)}`);
    }
    return path.endsWith("$count") ? Number(text.trim()) : JSON.parse(text);
  }

  async function getAll(path) {
    const prefer = `odata.maxpagesize=${PAGE_SIZE},odata.include-annotations="OData.Community.Display.V1.FormattedValue"`;
    const rows = [];
    const seen = new Set();
    let next = path;
    while (next) {
      if (seen.has(next)) {
        throw new Error("The server repeated a nextLink: " + next);
      }
      seen.add(next);
      const page = await get(next, prefer);
      rows.push(...page.value);
      next = page["@odata.nextLink"] || null;
    }
    return rows;
  }

  const started = new Date();
  log("Web API root:", WEB_API);

  const whoAmI = await get("WhoAmI()");
  const user = await get(`systemusers(${whoAmI.UserId})?$select=fullname,domainname`);
  log("Signed in as", user.domainname, "(" + user.fullname + ")");

  const privilegeFilter = encodeURIComponent(PRIVILEGES.map(name => `name eq '${name}'`).join(" or "));
  const privileges = await get(`privileges?$select=privilegeid,name&$filter=${privilegeFilter}`);
  const userPrivileges = await get(`systemusers(${whoAmI.UserId})/Microsoft.Dynamics.CRM.RetrieveUserPrivileges()`);

  const workflowAttributes = await get("EntityDefinitions(LogicalName='workflow')/Attributes?$select=LogicalName");
  const attributeNames = new Set(workflowAttributes.value.map(row => row.LogicalName.toLowerCase()));
  const attributeOf = column => column.startsWith("_") && column.endsWith("_value") ? column.slice(1, -"_value".length) : column;
  const columns = COLUMNS.filter(column => attributeNames.has(attributeOf(column)));
  const missing = COLUMNS.filter(column => !attributeNames.has(attributeOf(column)));
  if (missing.length) {
    log("Columns not on this server (left out):", missing.join(", "));
  }

  // The production 8.2 server answers workflows/$count with -1 ("no count available"); fall back to a FetchXML
  // aggregate, which is also a GET. "count" stays -1 only if neither gives a number.
  log("Counting workflows…");
  let rawCount = -1;
  try {
    rawCount = await get("workflows/$count", undefined, COUNT_TIMEOUT_MS);
  } catch (error) {
    log("$count did not answer; using the aggregate instead:", error.message);
  }
  let count = rawCount;
  let countSource = "$count";
  if (rawCount < 0) {
    const fetchXml = '<fetch aggregate="true"><entity name="workflow"><attribute name="workflowid" alias="n" aggregate="count" /></entity></fetch>';
    try {
      const aggregate = await get("workflows?fetchXml=" + encodeURIComponent(fetchXml), undefined, COUNT_TIMEOUT_MS);
      count = aggregate.value.length === 1 && typeof aggregate.value[0].n === "number" ? aggregate.value[0].n : -1;
      countSource = "fetchXml aggregate";
    } catch (error) {
      log("Aggregate count failed:", error.message);
    }
  }
  const workflows = await getAll(`workflows?$select=${columns.join(",")}&$orderby=workflowid`);
  log(`Inventory: count ${count} (${countSource}), retrieved ${workflows.length}`);

  const wanted = workflows.filter(row => row.type === 1 || row.type === 2);
  const xaml = {};
  const xamlErrors = {};
  let done = 0;
  // A CRM workflow cannot call a service; a custom activity can, and its endpoint is written in the activity's own
  // assembly rather than passed in from the workflow. The assembly is a field on the record, so the addresses are
  // read out of its string constants here, in the browser — only the extracted text travels, never the megabytes.
  const FRAMEWORK_URL = /:\/\/(schemas\.|www\.w3\.org|www\.omg\.org|docs\.oasis|go\.microsoft\.com|tempuri\.org\/?$)/i;

  function addressesIn(base64) {
    const raw = atob(base64);
    const found = new Set();
    // Once as bytes and once with the zero bytes removed, because a .NET string constant is stored as UTF-16.
    for (const text of [raw, raw.replace(/\0/g, "")]) {
      for (const match of text.matchAll(/(https?|ftp|net\.tcp):\/\/[^\s"'<>\\\x00-\x1f]+/gi)) {
        const address = match[0].replace(/[.,;)"']+$/, "");
        if (!FRAMEWORK_URL.test(address)) {
          found.add(address);
        }
      }
    }
    return [...found].sort();
  }

  async function readPlugins() {
    let assemblies = [];
    let types = [];
    let steps = [];
    try {
      assemblies = await getAll("pluginassemblies?$select=pluginassemblyid,name,version,sourcetype,ismanaged");
      types = await getAll("plugintypes?$select=plugintypeid,typename,friendlyname,isworkflowactivity,workflowactivitygroupname,_pluginassemblyid_value");
      steps = await getAll("sdkmessageprocessingsteps?$select=sdkmessageprocessingstepid,name,configuration,stage,mode,statecode,_plugintypeid_value");
    } catch (error) {
      log("Plug-in registry could not be read:", error.message);
      return { assemblies: [], types: [], steps: [] };
    }
    // Only the assemblies a workflow's custom activities come from: the rest are plug-ins, whose own endpoints are
    // not what the diagrams are missing.
    const wanted = new Set(types.filter(type => type.isworkflowactivity).map(type => type._pluginassemblyid_value));
    let scanned = 0;
    for (const assembly of assemblies) {
      if (!wanted.has(assembly.pluginassemblyid)) {
        continue;
      }
      try {
        const record = await get(`pluginassemblies(${assembly.pluginassemblyid})?$select=content`);
        assembly.addresses = record.content ? addressesIn(record.content) : [];
      } catch (error) {
        assembly.addresses = [];
        log(`Assembly ${assembly.name} could not be read:`, error.message);
      }
      scanned++;
      log(`Assemblies scanned ${scanned}/${wanted.size}`);
    }
    return { assemblies, types, steps };
  }

  async function fetchXaml(row) {
    try {
      const record = await get(`workflows(${row.workflowid})?$select=xaml`);
      xaml[row.workflowid] = record.xaml ?? null;
    } catch (error) {
      xamlErrors[row.workflowid] = String(error.message || error);
    }
    done++;
    if (done % 25 === 0 || done === wanted.length) {
      log(`XAML ${done}/${wanted.length}`);
    }
  }
  for (let index = 0; index < wanted.length; index += PARALLEL_XAML) {
    await Promise.all(wanted.slice(index, index + PARALLEL_XAML).map(fetchXaml));
  }

  const optionSets = {};
  const entities = [...new Set(workflows.map(row => row.primaryentity).filter(entity => entity && entity !== "none"))].sort();
  for (const entity of entities) {
    optionSets[entity] = {};
    for (const type of ["PicklistAttributeMetadata", "StatusAttributeMetadata", "StateAttributeMetadata"]) {
      try {
        optionSets[entity][type] = await get(`EntityDefinitions(LogicalName='${entity}')/Attributes/Microsoft.Dynamics.CRM.${type}?$select=LogicalName&$expand=OptionSet($select=Options)`);
      } catch (error) {
        optionSets[entity][type] = { error: String(error.message || error) };
      }
    }
  }
  log(`Option-set metadata for ${entities.length} entities`);

  const plugins = await readPlugins();
  log(`Plug-in registry: ${plugins.assemblies.length} assemblies, ${plugins.types.length} types, ${plugins.steps.length} steps`);

  let processStages = [];
  try {
    processStages = await getAll("processstages?$select=processstageid,stagename,stagecategory,_processid_value,primaryentitytypecode");
  } catch (error) {
    log("Process stages could not be read:", error.message);
  }

  const exported = {
    format: "crm-browser-export/1",
    exportedAtUtc: new Date().toISOString(),
    startedAtUtc: started.toISOString(),
    webApiRoot: WEB_API,
    whoAmI, user, privileges, userPrivileges, workflowAttributes,
    count, rawCount, countSource, columns, workflows, xaml, xamlErrors, optionSets, processStages, plugins
  };
  const stamp = started.toISOString().replace(/[-:]/g, "").replace("T", "-").slice(0, 15);
  const link = document.createElement("a");
  link.href = URL.createObjectURL(new Blob([JSON.stringify(exported)], { type: "application/json" }));
  link.download = `crm-export-${stamp}.json`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  log(`Done: ${workflows.length} workflows, ${Object.keys(xaml).length} XAML, ${Object.keys(xamlErrors).length} XAML errors. Saved ${link.download}`);
})().catch(error => console.error("[crm-export] FAILED:", error));
