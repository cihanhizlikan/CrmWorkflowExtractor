/*
 * CRM Workflow Extractor — browser export (format crm-browser-export/1)
 *
 * WHAT IT DOES: reads, through YOUR signed-in CRM browser session, everything the extractor would fetch itself —
 * who you are, your privileges, the workflow inventory and its count, the XAML of every definition and activation,
 * option-set labels, Business Process Flow stages and the security roles that may start a process by hand —
 * and saves it as ONE file, crm-export-<time>.json.
 *
 * NO PERSON IS NAMED. The security part counts the holders of a role and names the TEAMS; it never sends a user
 * name, a login or an id, so the file carries no personnel list.
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
  // The security tables are three or four columns wide, and there can be thousands of role memberships. At the
  // inventory's page size that is hundreds of round trips for a few hundred kilobytes.
  const THIN_PAGE_SIZE = 500;
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
    "_createdby_value", "_modifiedby_value", "_ownerid_value", "versionnumber",
    // The business unit owning the RECORD: what a security role's read depth is measured against.
    "_owningbusinessunit_value"];
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

  async function getAll(path, pageSize) {
    const prefer = `odata.maxpagesize=${pageSize ?? PAGE_SIZE},odata.include-annotations="OData.Community.Display.V1.FormattedValue"`;
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

  // WHO MAY START A PROCESS BY HAND.
  //
  // CRM has no per-workflow permission: there is no record saying "role X may run workflow Y". Starting one by
  // hand needs prvExecuteWorkflowJob, which a security role either carries or does not, plus the right to read
  // the process record (prvReadWorkflow). So what is read here is the ROLES and how many hold them; what varies
  // per workflow — the on-demand flag, the run-as setting, the owner — is already in the inventory above.
  //
  // The intersect tables' entity SET names are not their logical names and differ by version, so they are asked
  // for rather than assumed. Users are counted, never listed: the question is which group, and a list of every
  // person in the company answers nothing while carrying all of their names out of the building.
  async function readRunAuthority() {
    const RUN = "prvExecuteWorkflowJob";
    const READ = "prvReadWorkflow";
    try {
      const wanted = ["roleprivileges", "systemuserroles", "teamroles"];
      const definitions = await get("EntityDefinitions?$select=LogicalName,EntitySetName&$filter="
        + encodeURIComponent(wanted.map(name => `LogicalName eq '${name}'`).join(" or ")));
      const sets = {};
      for (const row of definitions.value) {
        sets[row.LogicalName] = row.EntitySetName;
      }
      const absent = wanted.filter(name => !sets[name]);
      if (absent.length) {
        log("Run authority: this server does not report", absent.join(", "));
        return { roles: [], teams: [], note: "Bu sunucu şu tabloları bildirmiyor: " + absent.join(", ") + "." };
      }

      const privileges = await get("privileges?$select=privilegeid,name&$filter="
        + encodeURIComponent(`name eq '${RUN}' or name eq '${READ}'`));
      const idOf = name => (privileges.value.find(row => row.name === name) || {}).privilegeid;
      const runId = idOf(RUN);
      const readId = idOf(READ);
      if (!runId) {
        log(`Run authority: '${RUN}' does not exist in this organisation`);
        return { roles: [], teams: [], note: `'${RUN}' bu kurulumda yok; elle çalıştırma yetkisi okunamadı.` };
      }

      const lookup = (row, name) => row[name] ?? row["_" + name + "_value"];
      const grantFilter = [runId, readId].filter(Boolean).map(id => `privilegeid eq ${id}`).join(" or ");
      const grants = await getAll(`${sets.roleprivileges}?$filter=${encodeURIComponent(grantFilter)}`, THIN_PAGE_SIZE);
      const runRoles = [...new Set(grants.filter(row => lookup(row, "privilegeid") === runId).map(row => lookup(row, "roleid")).filter(Boolean))];
      const teamRows = await getAll("teams?$select=teamid,name,teamtype", THIN_PAGE_SIZE);
      const teams = teamRows.map(row => ({ teamid: row.teamid, name: row.name, teamtype: row.teamtype }));
      if (!runRoles.length) {
        log(`Run authority: no security role holds '${RUN}'`);
        return { roles: [], teams, note: `Hiçbir güvenlik rolü '${RUN}' taşımıyor.` };
      }

      const roleFilter = encodeURIComponent(runRoles.map(id => `roleid eq ${id}`).join(" or "));
      const roles = await getAll("roles?$select=roleid,name,_businessunitid_value", THIN_PAGE_SIZE);
      const userRoles = await getAll(`${sets.systemuserroles}?$filter=${roleFilter}`, THIN_PAGE_SIZE);
      const teamRoles = await getAll(`${sets.teamroles}?$filter=${roleFilter}`, THIN_PAGE_SIZE);
      log(`Run authority: ${runRoles.length} role(s), ${userRoles.length} user membership(s), ${teamRoles.length} team membership(s)`);

      const named = {};
      for (const row of roles) {
        named[row.roleid] = { name: row.name, unit: row["_businessunitid_value@OData.Community.Display.V1.FormattedValue"] || null };
      }
      const teamName = {};
      for (const team of teams) {
        teamName[team.teamid] = team.name;
      }
      const deepest = (privilegeId) => {
        const best = {};
        for (const row of grants) {
          if (lookup(row, "privilegeid") !== privilegeId) {
            continue;
          }
          const role = lookup(row, "roleid");
          best[role] = Math.max(best[role] || 0, row.privilegedepthmask || 0);
        }
        return best;
      };
      const runDepth = deepest(runId);
      const readDepth = readId ? deepest(readId) : {};
      const users = {};
      for (const row of userRoles) {
        const role = lookup(row, "roleid");
        users[role] = (users[role] || 0) + 1;
      }
      const byRole = {};
      for (const row of teamRoles) {
        const role = lookup(row, "roleid");
        const team = lookup(row, "teamid");
        (byRole[role] = byRole[role] || []).push(teamName[team] || team);
      }
      return {
        roles: runRoles.map(id => ({
          roleId: id,
          name: (named[id] || {}).name || "",
          businessUnit: (named[id] || {}).unit || null,
          runDepthMask: runDepth[id] || 0,
          processDepthMask: readDepth[id] || 0,
          users: users[id] || 0,
          teams: (byRole[id] || []).sort()
        })).sort((left, right) => right.users - left.users || left.name.localeCompare(right.name)),
        teams,
        note: null
      };
    } catch (error) {
      log("Run authority could not be read:", error.message);
      return { roles: [], teams: [], note: String(error.message || error) };
    }
  }

  const runAuthority = await readRunAuthority();

  let processStages = [];
  try {
    processStages = await getAll("processstages?$select=processstageid,stagename,stagecategory,_processid_value,primaryentitytypecode");
  } catch (error) {
    log("Process stages could not be read:", error.message);
  }

  // THE DOCUMENT IS TOO BIG TO EXIST TWICE. Writing it has failed twice on the TEST organisation, and each
  // failure cost a twenty-minute run:
  //   JSON.stringify(whole document)  -> RangeError: Invalid string length. One string cannot hold 512 million
  //                                      characters, and six thousand XAML documents pass that on their own.
  //   an ARRAY of every piece         -> no error at all. The escaped copies sat beside the originals, about a
  //                                      gigabyte of strings in one tab, and the page died between two lines
  //                                      with no "Done", no "FAILED" and no file.
  //
  // So pieces are produced ONE AT A TIME and handed to the browser in segments. Each segment Blob moves its bytes
  // out of the JavaScript heap the moment it is built, so the heap never holds more than one segment on top of
  // what was retrieved — whatever the size of the organisation.
  //
  // Only the outer two levels are split; that is where the big collections are (xaml, workflows, optionSets) and
  // everything below is small enough to stringify in one go.
  function* jsonPieces(value, depth) {
    if (depth >= 2 || value === null || typeof value !== "object") {
      yield JSON.stringify(value) ?? "null";
      return;
    }
    if (Array.isArray(value)) {
      yield "[";
      for (let index = 0; index < value.length; index++) {
        if (index) {
          yield ",";
        }
        yield* jsonPieces(value[index], depth + 1);
      }
      yield "]";
      return;
    }
    yield "{";
    let first = true;
    for (const key of Object.keys(value)) {
      if (value[key] === undefined) {
        continue;   // JSON.stringify drops such a key, and so must this.
      }
      yield (first ? "" : ",") + JSON.stringify(key) + ":";
      first = false;
      yield* jsonPieces(value[key], depth + 1);
    }
    yield "}";
  }

  // Characters per segment. Small enough that the heap never grows with the organisation, large enough that a
  // six-hundred-megabyte document is a few dozen segments rather than a few million.
  const SEGMENT_CHARS = 8 * 1024 * 1024;

  // Every step announces itself. The last failure printed nothing between "Plug-in registry" and silence, which
  // told nobody where it died; whatever happens next, the last line printed names the stage it happened in.
  async function writeDocument(document_, stamp) {
    log("Assembling the file…");
    const segments = [];
    let buffer = [];
    let held = 0;
    for (const piece of jsonPieces(document_, 0)) {
      buffer.push(piece);
      held += piece.length;
      if (held >= SEGMENT_CHARS) {
        segments.push(new Blob(buffer));
        buffer = [];
        held = 0;
      }
    }
    segments.push(new Blob(buffer));
    let blob = new Blob(segments, { type: "application/json" });
    let name = `crm-export-${stamp}.json`;
    log(`Assembled ${(blob.size / 1048576).toFixed(0)} MB in ${segments.length} segment(s)`);

    // Gzip when the browser has it. XAML compresses about tenfold, which turns a file nobody can move or keep
    // into an ordinary one. The extractor recognises it by its first two bytes, not by its name.
    if (typeof CompressionStream === "function") {
      try {
        blob = await new Response(blob.stream().pipeThrough(new CompressionStream("gzip"))).blob();
        name += ".gz";
        log(`Compressed to ${(blob.size / 1048576).toFixed(1)} MB`);
      } catch (error) {
        log("Compression failed; the file is saved uncompressed:", error.message);
      }
    } else {
      log("This browser has no CompressionStream; the file is saved uncompressed.");
    }

    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = name;
    document.body.appendChild(link);
    link.click();
    link.remove();
    log("Download started.");
    return { name, size: blob.size };
  }

  const exported = {
    format: "crm-browser-export/1",
    exportedAtUtc: new Date().toISOString(),
    startedAtUtc: started.toISOString(),
    webApiRoot: WEB_API,
    whoAmI, user, privileges, userPrivileges, workflowAttributes,
    count, rawCount, countSource, columns, workflows, xaml, xamlErrors, optionSets, processStages, plugins,
    runAuthority
  };
  const stamp = started.toISOString().replace(/[-:]/g, "").replace("T", "-").slice(0, 15);
  const saved = await writeDocument(exported, stamp);
  log(`Done: ${workflows.length} workflows, ${Object.keys(xaml).length} XAML, ${Object.keys(xamlErrors).length} XAML errors.`);
  log(`Saved ${saved.name} — ${(saved.size / 1048576).toFixed(1)} MB`);
})().catch(error => console.error("[crm-export] FAILED:", error));
