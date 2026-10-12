/*
 * CRM Workflow Extractor — process mining export (format crm-process-mining-export/1)
 *
 * WHAT IT DOES: measures how cases actually move through their stages — how often each stage is entered, how often
 * each step from one stage to the next happens, and how long it takes — through YOUR signed-in CRM browser session,
 * and saves the TOTALS as one small file, crm-process-mining-<time>.json.
 *
 * ANONYMOUS BY CONSTRUCTION: every row is reduced inside this browser tab. The file holds counts and durations per
 * stage and per step, nothing else: no case id, no customer, no product, no user, no exact time — only the first and
 * last DAY the history covers. A step taken fewer than MINIMUM_GROUP times carries its count but no durations, so a
 * rare path cannot point back to one case. While it runs, only the case being read is held in memory.
 *
 * WORKING TIME: beside calendar time, every step is measured in WORKING DAYS — the time that falls on a working day,
 * divided by 24. Saturdays, Sundays and the organisation's holidays do not count; the hours of a working day all do,
 * because no record says when an office opens. The holidays are CRM's own business-closure calendar; where it cannot
 * be read, only weekends are left out and the file says so. Day boundaries are this computer's time zone.
 *
 * WHERE THE HISTORY IS: no stage record says which cases passed through it, so the script finds the entity that does
 * from metadata — one with a lookup to the case (incident) AND to the stage record (ps_step). Nothing about it is
 * assumed by name. When several qualify, it asks which to use. A row's creation time is taken as the moment the case
 * entered that stage; consecutive rows of one case are one step.
 *
 * READ-ONLY: every request below is a GET (there is no "method" in this file, so fetch defaults to GET).
 *
 * HOW TO RUN: sign in to CRM, stay on a CRM page, click the "CRM Process Mining" bookmark (tools/crm-process-mining.html). Then set
 * Run:ProcessMiningFile to the downloaded file and re-run the extractor. The report it feeds is for Enterprise
 * Architecture only and is never part of the package delivered to the outsource partner.
 */
(async () => {
  "use strict";
  const WEB_API = location.origin + "/api/data/v8.2/";   // IFD host: no organization segment. Change if needed.
  const PAGE_SIZE = 5000;
  const TIMEOUT_MS = 120000;
  const CASE = "incident";
  const STAGE = "ps_step";
  // Fewer steps than this and the durations are left out: a distribution of three is three cases.
  const MINIMUM_GROUP = 5;

  const log = (...args) => console.log("%c[crm-process-mining]", "color:#7a3", ...args);

  async function get(path, prefer) {
    const headers = { "Accept": "application/json", "OData-MaxVersion": "4.0", "OData-Version": "4.0" };
    if (prefer) {
      headers["Prefer"] = prefer;
    }
    const stop = AbortSignal.timeout ? AbortSignal.timeout(TIMEOUT_MS) : undefined;
    const response = await fetch(path.startsWith("http") ? path : WEB_API + path, { credentials: "include", headers, signal: stop });
    const text = await response.text();
    if (!response.ok) {
      throw new Error(`GET ${path} -> ${response.status}: ${text.slice(0, 300)}`);
    }
    return JSON.parse(text);
  }

  // Page by page, each handed over and dropped: the history can run to millions of rows, and none of them is kept.
  async function eachPage(path, handle) {
    const seen = new Set();
    let next = path;
    let pages = 0;
    while (next) {
      if (seen.has(next)) {
        throw new Error("The server repeated a nextLink: " + next);
      }
      seen.add(next);
      const page = await get(next, `odata.maxpagesize=${PAGE_SIZE}`);
      handle(page.value);
      pages++;
      if (pages % 10 === 0) {
        log(`History pages read: ${pages}`);
      }
      next = page["@odata.nextLink"] || null;
    }
  }

  // An error CRM sends back can carry ids — the calling user's and the record owner's, in an access refusal — and this
  // file holds totals and nothing else. The reason stays; the ids do not.
  const reason = error => String(error.message || error).replace(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi, "<id>");

  async function lookupsTo(entity) {
    const page = await get(`EntityDefinitions(LogicalName='${entity}')/OneToManyRelationships?$select=ReferencingEntity,ReferencingAttribute`);
    return page.value;
  }

  const started = new Date();
  log("Web API root:", WEB_API);

  // THE HOLIDAYS: the organisation's business-closure calendar, the one CRM's own service calendars subtract. Its rules
  // are reached through the calendar; on some versions they are a table of their own. Either way only start and end
  // are kept. Names of custom tables that look like a holiday list are recorded too — names only — in case the SLA
  // engine keeps its own.
  async function readHolidays() {
    const result = { source: null, intervals: [], candidates: [] };
    try {
      const definitions = await get("EntityDefinitions?$select=LogicalName,IsCustomEntity");
      result.candidates = definitions.value.filter(row => row.IsCustomEntity && /holiday|tatil|bayram|closure|calendar|takvim/i.test(row.LogicalName))
        .map(row => row.LogicalName).sort();
    } catch (error) {
      log("Entity names could not be read:", error.message);
    }
    let calendar = null;
    try {
      const organizations = await get("organizations?$select=businessclosurecalendarid");
      calendar = organizations.value[0] && organizations.value[0].businessclosurecalendarid;
    } catch (error) {
      log("The organisation's closure calendar could not be found:", error.message);
    }
    if (!calendar) {
      return result;
    }
    const rule = row => {
      const start = Date.parse(row.effectiveintervalstart || row.starttime);
      const end = row.effectiveintervalend ? Date.parse(row.effectiveintervalend) : start + (row.duration || 0) * 60000;
      return Number.isNaN(start) || Number.isNaN(end) || end <= start ? null : [start, end];
    };
    const ways = [
      ["calendar_calendar_rules", async () => (await get(`calendars(${calendar})?$select=name&$expand=calendar_calendar_rules($select=starttime,duration,effectiveintervalstart,effectiveintervalend)`)).calendar_calendar_rules],
      ["calendarrules", async () => (await get(`calendarrules?$select=starttime,duration,effectiveintervalstart,effectiveintervalend&$filter=_calendarid_value eq ${calendar}`)).value]
    ];
    for (const [way, read] of ways) {
      try {
        const rows = await read();
        if (Array.isArray(rows)) {
          result.intervals = rows.map(rule).filter(Boolean).sort((one, other) => one[0] - other[0]);
          result.source = way;
          return result;
        }
      } catch (error) {
        log(`Holidays could not be read as ${way}:`, error.message);
      }
    }
    return result;
  }
  const holidays = await readHolidays();
  log(`Holidays: ${holidays.intervals.length} from ${holidays.source ?? "nowhere — weekends only"}`);

  // Working milliseconds between two instants: what falls on a weekday, less what falls in a holiday. Each whole day
  // in between is worked out once and remembered, so the history costs one pass over the calendar, not one per step.
  const DAY = 86400000;
  const dayStart = time => { const date = new Date(time); return new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime(); };
  const nextDay = start => { const date = new Date(start); return new Date(date.getFullYear(), date.getMonth(), date.getDate() + 1).getTime(); };
  const weekend = start => { const weekday = new Date(start).getDay(); return weekday === 0 || weekday === 6; };
  const holidayIn = (from, to) => {
    let overlap = 0;
    for (const [start, end] of holidays.intervals) {
      if (start >= to) {
        break;
      }
      overlap += Math.max(0, Math.min(end, to) - Math.max(start, from));
    }
    return overlap;
  };
  const workIn = (from, to) => from >= to || weekend(dayStart(from)) ? 0 : (to - from) - holidayIn(from, to);
  const wholeDays = new Map();   // day start -> working ms from that day's start to the start of the reference day
  const workBetween = (from, to) => {
    if (to <= from) {
      return 0;
    }
    const firstDay = dayStart(from);
    const lastDay = dayStart(to);
    if (firstDay === lastDay) {
      return workIn(from, to);
    }
    let total = workIn(from, nextDay(firstDay)) + workIn(lastDay, to);
    for (let day = nextDay(firstDay); day < lastDay; day = nextDay(day)) {
      if (!wholeDays.has(day)) {
        wholeDays.set(day, workIn(day, nextDay(day)));
      }
      total += wholeDays.get(day);
    }
    return total;
  };

  // Every entity with a lookup to both the case and the stage record, in every pairing of those lookups. An activity's
  // "regarding" field can point at either, so every activity table qualifies through it — e-mail, phone call, mailbox
  // tracking folder — and none of them is a stage history: they are named and left out rather than probed. (On the
  // first production run every probe of them came back 400.)
  const REGARDING = "regardingobjectid";
  const candidates = [];
  const activityTables = new Set();
  let discoveryError = null;
  try {
    const toStage = await lookupsTo(STAGE);
    const toCase = await lookupsTo(CASE);
    for (const stage of toStage) {
      for (const kase of toCase.filter(relation => relation.ReferencingEntity === stage.ReferencingEntity)) {
        if (stage.ReferencingEntity === CASE || stage.ReferencingEntity === STAGE) {
          continue;
        }
        if (stage.ReferencingAttribute === REGARDING || kase.ReferencingAttribute === REGARDING) {
          activityTables.add(stage.ReferencingEntity);
          continue;
        }
        candidates.push({ entity: stage.ReferencingEntity, caseAttribute: kase.ReferencingAttribute, stageAttribute: stage.ReferencingAttribute });
      }
    }
  } catch (error) {
    discoveryError = reason(error);
    log("The relationships to the case and the stage record could not be read:", discoveryError);
  }
  for (const candidate of candidates) {
    try {
      const definition = await get(`EntityDefinitions(LogicalName='${candidate.entity}')?$select=EntitySetName`);
      candidate.entitySet = definition.EntitySetName;
      // One row a page and the total, without $top — which together with $count is what the server refused. The
      // total stops at 5000 on this version; enough to tell a history from a table that holds a handful of rows.
      const counted = await get(`${candidate.entitySet}?$select=_${candidate.caseAttribute}_value&$count=true`, "odata.maxpagesize=1");
      candidate.rows = counted["@odata.count"] ?? null;
    } catch (error) {
      candidate.rows = null;
      candidate.error = reason(error);
    }
  }
  log("Candidates for the stage history:", candidates.map(candidate => `${candidate.entity} (${candidate.caseAttribute} / ${candidate.stageAttribute}): ${candidate.error ?? (candidate.rows ?? "?") + " rows"}`));
  if (activityTables.size > 0) {
    log("Activity tables left out (their 'regarding' field reaches the case and the stage):", [...activityTables].sort());
  }

  let chosen = null;
  let chosenBy = null;
  const readable = candidates.filter(candidate => candidate.entitySet && !candidate.error);
  if (readable.length === 1) {
    chosen = readable[0];
    chosenBy = "tek aday";
  } else if (readable.length > 1) {
    const list = readable.map((candidate, index) => `${index + 1}) ${candidate.entity} — ${candidate.caseAttribute} / ${candidate.stageAttribute} — ${candidate.rows ?? "?"}${candidate.rows === 5000 ? "+" : ""} satır`).join("\n");
    const answer = typeof prompt === "function" ? prompt("Aşama geçmişini hangisi tutuyor? Numarasını yazın:\n" + list, "1") : null;
    const index = Number.parseInt(answer ?? "", 10) - 1;
    if (index >= 0 && index < readable.length) {
      chosen = readable[index];
      chosenBy = "kullanıcı seçti";
    } else {
      chosen = [...readable].sort((one, other) => (other.rows ?? 0) - (one.rows ?? 0))[0];
      chosenBy = "en çok satırı olan";
    }
  }

  const entries = new Map();
  const lastRecorded = new Map();
  const steps = new Map();
  let cases = 0;
  let events = 0;
  let skipped = 0;
  let first = null;
  let last = null;
  let historyError = null;
  if (chosen) {
    log(`Reading the stage history from ${chosen.entity} (${chosenBy})`);
    const caseValue = `_${chosen.caseAttribute}_value`;
    const stageValue = `_${chosen.stageAttribute}_value`;
    // In case order, then time order: one case at a time, so only that case is ever held.
    let current = null;
    const close = () => {
      if (current) {
        lastRecorded.set(current.stage, (lastRecorded.get(current.stage) ?? 0) + 1);
      }
    };
    // A table the signed-in user may not read answers 403. That is an answer, not a crash: the file is still saved,
    // saying which table and why, and nothing half-read is kept — a partial history would read as a whole one.
    try {
      await eachPage(`${chosen.entitySet}?$select=createdon,${caseValue},${stageValue}&$orderby=${caseValue} asc,createdon asc`, rows => {
        for (const row of rows) {
          const kase = row[caseValue];
          const stage = row[stageValue];
          const at = Date.parse(row.createdon);
          if (!kase || !stage || Number.isNaN(at)) {
            skipped++;
            continue;
          }
          events++;
          first = first === null || at < first ? at : first;
          last = last === null || at > last ? at : last;
          entries.set(stage, (entries.get(stage) ?? 0) + 1);
          if (current && current.kase === kase) {
            const key = `${current.stage}|${stage}`;
            if (!steps.has(key)) {
              steps.set(key, { hours: [], workDays: [] });
            }
            steps.get(key).hours.push((at - current.at) / 3600000);
            steps.get(key).workDays.push(workBetween(current.at, at) / DAY);
          } else {
            close();
            cases++;
          }
          current = { kase, stage, at };
        }
      });
      close();
    } catch (error) {
      historyError = reason(error);
      log(`The stage history in ${chosen.entity} could not be read:`, historyError);
      entries.clear();
      lastRecorded.clear();
      steps.clear();
      cases = 0;
      events = 0;
      skipped = 0;
      first = null;
      last = null;
    }
  } else {
    log("No entity has a lookup to both the case and the stage record: there is no stage history to measure.");
  }

  const round = value => Math.round(value * 10) / 10;
  // Linear interpolation between the two nearest values: the median of an even count is the middle of its middle two.
  const at = (sorted, share) => {
    const position = share * (sorted.length - 1);
    const below = Math.floor(position);
    const above = Math.min(sorted.length - 1, below + 1);
    return sorted[below] + (sorted[above] - sorted[below]) * (position - below);
  };
  const round2 = value => Math.round(value * 100) / 100;
  const transitions = [...steps.entries()].map(([key, measured]) => {
    const [from, to] = key.split("|");
    const step = { from, to, count: measured.hours.length };
    if (measured.hours.length >= MINIMUM_GROUP) {
      const hours = measured.hours.sort((one, other) => one - other);
      step.meanHours = round(hours.reduce((sum, value) => sum + value, 0) / hours.length);
      step.medianHours = round(at(hours, 0.5));
      step.p90Hours = round(at(hours, 0.9));
      const workDays = measured.workDays.sort((one, other) => one - other);
      step.meanWorkDays = round2(workDays.reduce((sum, value) => sum + value, 0) / workDays.length);
      step.medianWorkDays = round2(at(workDays, 0.5));
      step.p90WorkDays = round2(at(workDays, 0.9));
    }
    return step;
  });
  const day = time => time === null ? null : new Date(time).toISOString().slice(0, 10);

  const exported = {
    format: "crm-process-mining-export/1",
    exportedAtUtc: new Date().toISOString(),
    startedAtUtc: started.toISOString(),
    webApiRoot: WEB_API,
    source: chosen ? { entity: chosen.entity, entitySet: chosen.entitySet, caseAttribute: chosen.caseAttribute, stageAttribute: chosen.stageAttribute, chosenBy, error: historyError } : null,
    discoveryError,
    activityTables: [...activityTables].sort(),
    candidates: candidates.map(candidate => ({ entity: candidate.entity, caseAttribute: candidate.caseAttribute, stageAttribute: candidate.stageAttribute, rows: candidate.rows, error: candidate.error ?? null })),
    minimumGroup: MINIMUM_GROUP,
    workingTime: {
      weekend: ["Cumartesi", "Pazar"],
      holidaySource: holidays.source,
      holidays: holidays.intervals.length,
      holidayCandidates: holidays.candidates,
      timeZoneOffsetMinutes: -started.getTimezoneOffset()
    },
    window: { fromDate: day(first), toDate: day(last) },
    cases,
    events,
    skipped,
    stages: [...entries.entries()].map(([stage, count]) => ({ stage, entries: count, lastRecorded: lastRecorded.get(stage) ?? 0 })),
    transitions
  };
  const stamp = started.toISOString().replace(/[-:]/g, "").replace("T", "-").slice(0, 15);
  const link = document.createElement("a");
  link.href = URL.createObjectURL(new Blob([JSON.stringify(exported)], { type: "application/json" }));
  link.download = `crm-process-mining-${stamp}.json`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  if (historyError) {
    log(`Saved ${link.download} WITHOUT a history: ${chosen.entity} could not be read — ${historyError.replace(/^GET .*? -> /, "").slice(0, 200)}. The extractor will say so.`);
    return;
  }
  log(`Done: ${cases} cases, ${events} stage entries, ${transitions.length} kinds of step, ${skipped} rows skipped. Saved ${link.download}`);
})().catch(error => console.error("[crm-process-mining] FAILED:", error));
