/*
 * CRM Workflow Extractor — process mining export (format crm-process-mining-export/1)
 *
 * WHAT IT DOES: measures how cases actually move through their stages — how often each stage is entered, how often
 * each step from one stage to the next happens, and how long it takes — through YOUR signed-in CRM browser session,
 * and saves the TOTALS as one small file, crm-surec-<time>.json.
 *
 * ANONYMOUS BY CONSTRUCTION: every row is reduced inside this browser tab. The file holds counts and durations per
 * stage and per step, nothing else: no case id, no customer, no product, no user, no exact time — only the first and
 * last DAY the history covers. A step taken fewer than MINIMUM_GROUP times carries its count but no durations, so a
 * rare path cannot point back to one case. While it runs, only the case being read is held in memory.
 *
 * WHERE THE HISTORY IS: no stage record says which cases passed through it, so the script finds the entity that does
 * from metadata — one with a lookup to the case (incident) AND to the stage record (ps_step). Nothing about it is
 * assumed by name. When several qualify, it asks which to use. A row's creation time is taken as the moment the case
 * entered that stage; consecutive rows of one case are one step.
 *
 * READ-ONLY: every request below is a GET (there is no "method" in this file, so fetch defaults to GET).
 *
 * HOW TO RUN: sign in to CRM, stay on a CRM page, click the "CRM Süreç" bookmark (tools/crm-surec.html). Then set
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

  const log = (...args) => console.log("%c[crm-surec]", "color:#7a3", ...args);

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

  async function lookupsTo(entity) {
    const page = await get(`EntityDefinitions(LogicalName='${entity}')/OneToManyRelationships?$select=ReferencingEntity,ReferencingAttribute`);
    return page.value;
  }

  const started = new Date();
  log("Web API root:", WEB_API);

  // Every entity with a lookup to both the case and the stage record, in every pairing of those lookups.
  const toStage = await lookupsTo(STAGE);
  const toCase = await lookupsTo(CASE);
  const candidates = [];
  for (const stage of toStage) {
    for (const kase of toCase.filter(relation => relation.ReferencingEntity === stage.ReferencingEntity)) {
      if (stage.ReferencingEntity === CASE || stage.ReferencingEntity === STAGE) {
        continue;
      }
      candidates.push({ entity: stage.ReferencingEntity, caseAttribute: kase.ReferencingAttribute, stageAttribute: stage.ReferencingAttribute });
    }
  }
  for (const candidate of candidates) {
    try {
      const definition = await get(`EntityDefinitions(LogicalName='${candidate.entity}')?$select=EntitySetName`);
      candidate.entitySet = definition.EntitySetName;
      // $count stops at 5000 on this version; enough to tell a history from a table that holds a handful of rows.
      const counted = await get(`${candidate.entitySet}?$select=createdon&$count=true&$top=1`);
      candidate.rows = counted["@odata.count"] ?? null;
    } catch (error) {
      candidate.rows = null;
      candidate.error = String(error.message || error);
    }
  }
  log("Entities with a lookup to both the case and the stage:", candidates);

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
            steps.set(key, []);
          }
          steps.get(key).push((at - current.at) / 3600000);
        } else {
          close();
          cases++;
        }
        current = { kase, stage, at };
      }
    });
    close();
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
  const transitions = [...steps.entries()].map(([key, hours]) => {
    const [from, to] = key.split("|");
    const step = { from, to, count: hours.length };
    if (hours.length >= MINIMUM_GROUP) {
      const sorted = hours.sort((one, other) => one - other);
      step.meanHours = round(sorted.reduce((sum, value) => sum + value, 0) / sorted.length);
      step.medianHours = round(at(sorted, 0.5));
      step.p90Hours = round(at(sorted, 0.9));
    }
    return step;
  });
  const day = time => time === null ? null : new Date(time).toISOString().slice(0, 10);

  const exported = {
    format: "crm-process-mining-export/1",
    exportedAtUtc: new Date().toISOString(),
    startedAtUtc: started.toISOString(),
    webApiRoot: WEB_API,
    source: chosen ? { entity: chosen.entity, entitySet: chosen.entitySet, caseAttribute: chosen.caseAttribute, stageAttribute: chosen.stageAttribute, chosenBy } : null,
    candidates: candidates.map(candidate => ({ entity: candidate.entity, caseAttribute: candidate.caseAttribute, stageAttribute: candidate.stageAttribute, rows: candidate.rows, error: candidate.error ?? null })),
    minimumGroup: MINIMUM_GROUP,
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
  link.download = `crm-surec-${stamp}.json`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  log(`Done: ${cases} cases, ${events} stage entries, ${transitions.length} kinds of step, ${skipped} rows skipped. Saved ${link.download}`);
})().catch(error => console.error("[crm-surec] FAILED:", error));
