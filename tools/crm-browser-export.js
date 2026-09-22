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

  const count = await get("workflows/$count");
  const workflows = await getAll(`workflows?$select=${columns.join(",")}&$orderby=workflowid`);
  log(`Inventory: $count ${count}, retrieved ${workflows.length}`);

  const wanted = workflows.filter(row => row.type === 1 || row.type === 2);
  const xaml = {};
  const xamlErrors = {};
  let done = 0;
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
    count, columns, workflows, xaml, xamlErrors, optionSets, processStages
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
