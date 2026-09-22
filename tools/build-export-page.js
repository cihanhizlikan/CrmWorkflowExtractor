// Regenerates the bookmarklet pages from their scripts. Run after any change to either script:
//   node tools/build-export-page.js
// Each page embeds its script's SHA-256; BrowserExportImportTests fails when a page and its script drift apart.
const fs = require("fs");
const path = require("path");
const crypto = require("crypto");

const pages = [
  {
    script: "crm-browser-export.js",
    page: "crm-export.html",
    title: "CRM Export",
    colour: "#0b6a3b",
    what: `Reads every workflow definition and activation (with XAML), your privileges, option-set labels and process stages
through <b>your own signed-in CRM session</b>, and saves them as one file <code>crm-export-&lt;time&gt;.json</code>.
Every request is a read (GET); nothing in CRM is changed.`,
    file: "crm-export-&lt;time&gt;.json",
    duration: "It takes a few minutes",
    setting: `<code>"Run": { "ImportFile": "&lt;full path to that file&gt;" }</code>`,
    warning: `<b>The downloaded file contains production workflow definitions</b>, which may include URLs, user names or
passwords. Keep it on the company computer; do not email it or paste it into a chat.`
  },
  {
    script: "crm-usage-export.js",
    page: "crm-usage.html",
    title: "CRM Usage",
    colour: "#0b4f8a",
    what: `For every workflow, reads the most recent run CRM still has a record of (System Jobs, dialog sessions) through
<b>your own signed-in CRM session</b>, and saves it as one small file <code>crm-usage-&lt;time&gt;.json</code> — dates,
states and ids only. Every request is a read (GET); nothing in CRM is changed.
<br><br><b>A record found proves the workflow ran. No record proves nothing</b>: System Jobs are deleted routinely,
real-time workflows log only failures, and business rules are never logged.`,
    file: "crm-usage-&lt;time&gt;.json",
    duration: "It makes one or two small requests per workflow, so it takes several minutes",
    setting: `<code>"Run": { "UsageFile": "&lt;full path to that file&gt;" }</code> together with
<code>ReprocessRunId</code> (or <code>ImportFile</code>)`,
    warning: `The file holds workflow ids and run dates, no record data. Keep it with the export all the same.`
  }
];

for (const spec of pages) {
  const source = fs.readFileSync(path.join(__dirname, spec.script), "utf8");
  const sha256 = crypto.createHash("sha256").update(source, "utf8").digest("hex");
  const href = "javascript:" + encodeURIComponent(source);
  const page = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>${spec.title} bookmarklet</title>
<style>
  body { font-family: Segoe UI, Arial, sans-serif; max-width: 46rem; margin: 2rem auto; padding: 0 1rem; line-height: 1.5; color: #1b1b1b; }
  a.button { display: inline-block; padding: .6rem 1.2rem; background: ${spec.colour}; color: #fff; border-radius: 6px; text-decoration: none; font-weight: 600; }
  li { margin: .4rem 0; }
  code { background: #f1f1f1; padding: 0 .25rem; border-radius: 3px; }
  .warn { border-left: 4px solid #b3261e; padding: .4rem .8rem; background: #fdf0ef; }
</style>
</head>
<body data-script-sha256="${sha256}">
<h1>${spec.title}</h1>
<p>${spec.what}</p>

<p><a class="button" href="${href}">${spec.title}</a></p>

<h2>One-time setup</h2>
<ol>
  <li>Show the bookmarks bar (<code>Ctrl+Shift+B</code>).</li>
  <li><b>Drag</b> the <i>${spec.title}</i> button above onto the bookmarks bar. (Clicking it here does nothing useful.)</li>
</ol>

<h2>Each run</h2>
<ol>
  <li>Sign in to CRM as usual and stay on a CRM page, e.g. <code>https://ahecrm.anadoluhayat.com.tr/main.aspx</code>.</li>
  <li>Click <i>${spec.title}</i> on the bookmarks bar. ${spec.duration}; progress is visible under F12 → Console.</li>
  <li>When it finishes, the browser downloads <code>${spec.file}</code>.</li>
  <li>In the extractor's <code>appsettings.json</code>, set ${spec.setting} and run it.</li>
</ol>

<p class="warn">${spec.warning}</p>

<p><small>Generated from <code>tools/${spec.script}</code> (SHA-256 ${sha256}). If the bookmark stops working after
the script changes, drag the new button again.</small></p>
</body>
</html>
`;
  fs.writeFileSync(path.join(__dirname, spec.page), page);
  console.log(`${spec.page} written; script sha256 ${sha256}; bookmarklet ${href.length} characters`);
}
