// Regenerates tools/crm-export.html from tools/crm-browser-export.js. Run after any change to the script:
//   node tools/build-export-page.js
// The page embeds the script's SHA-256; RepositoryConventionTests fails when the two drift apart.
const fs = require("fs");
const path = require("path");
const crypto = require("crypto");

const source = fs.readFileSync(path.join(__dirname, "crm-browser-export.js"), "utf8");
const sha256 = crypto.createHash("sha256").update(source, "utf8").digest("hex");
const href = "javascript:" + encodeURIComponent(source);

const page = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>CRM Export bookmarklet</title>
<style>
  body { font-family: Segoe UI, Arial, sans-serif; max-width: 46rem; margin: 2rem auto; padding: 0 1rem; line-height: 1.5; color: #1b1b1b; }
  a.button { display: inline-block; padding: .6rem 1.2rem; background: #0b6a3b; color: #fff; border-radius: 6px; text-decoration: none; font-weight: 600; }
  li { margin: .4rem 0; }
  code { background: #f1f1f1; padding: 0 .25rem; border-radius: 3px; }
  .warn { border-left: 4px solid #b3261e; padding: .4rem .8rem; background: #fdf0ef; }
</style>
</head>
<body data-script-sha256="${sha256}">
<h1>CRM Export</h1>
<p>Reads every workflow definition and activation (with XAML), your privileges, option-set labels and process stages
through <b>your own signed-in CRM session</b>, and saves them as one file <code>crm-export-&lt;time&gt;.json</code>.
Every request is a read (GET); nothing in CRM is changed.</p>

<p><a class="button" href="${href}">CRM Export</a></p>

<h2>One-time setup</h2>
<ol>
  <li>Show the bookmarks bar (<code>Ctrl+Shift+B</code>).</li>
  <li><b>Drag</b> the green <i>CRM Export</i> button above onto the bookmarks bar. (Clicking it here does nothing useful.)</li>
</ol>

<h2>Each export</h2>
<ol>
  <li>Sign in to CRM as usual and stay on a CRM page, e.g. <code>https://ahecrm.anadoluhayat.com.tr/main.aspx</code>.</li>
  <li>Click <i>CRM Export</i> on the bookmarks bar. It takes a few minutes; progress is visible under F12 → Console.</li>
  <li>When it finishes, the browser downloads <code>crm-export-&lt;time&gt;.json</code>.</li>
  <li>In the extractor's <code>appsettings.json</code>, set <code>"Run": { "ImportFile": "&lt;full path to that file&gt;" }</code> and run it.</li>
</ol>

<p class="warn"><b>The downloaded file contains production workflow definitions</b>, which may include URLs, user names or
passwords. Keep it on the company computer; do not email it or paste it into a chat.</p>

<p><small>Generated from <code>tools/crm-browser-export.js</code> (SHA-256 ${sha256}). If the bookmark stops working after
the script changes, drag the new button again.</small></p>
</body>
</html>
`;
fs.writeFileSync(path.join(__dirname, "crm-export.html"), page);
console.log(`crm-export.html written; script sha256 ${sha256}; bookmarklet ${href.length} characters`);
