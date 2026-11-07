// Safe mini-markdown for the AI Recruitment Assistant.
// Escapes HTML first, then formats. Links: only http(s) and site-relative.
(function (root) {
  "use strict";

  function esc(s) {
    return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  }

  function inline(s) {
    return esc(s)
      .replace(/`([^`]+)`/g, "<code>$1</code>")
      .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
      .replace(/(^|[^*\w])\*([^*\n]+)\*(?!\w)/g, "$1<em>$2</em>")
      .replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+|\/[^\s)]*)\)/g,
        '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>');
  }

  const BLOCK = /^(```|#{1,4}\s|\s*[-*•]\s|\s*\d+[.)]\s|\s*\|)/;

  function md(src) {
    const lines = String(src || "").replace(/\r/g, "").split("\n");
    let out = "", i = 0;
    while (i < lines.length) {
      const line = lines[i];
      if (/^```/.test(line)) {
        const buf = []; i++;
        while (i < lines.length && !/^```/.test(lines[i])) buf.push(lines[i++]);
        i++;
        out += "<pre><code>" + esc(buf.join("\n")) + "</code></pre>";
        continue;
      }
      if (/^\s*\|.*\|\s*$/.test(line)) {
        const rows = [];
        while (i < lines.length && /^\s*\|.*\|\s*$/.test(lines[i])) rows.push(lines[i++]);
        const cells = (r) => r.trim().replace(/^\||\|$/g, "").split("|").map((c) => c.trim());
        const body = rows.filter((r) => !/^\s*\|?\s*:?-{2,}/.test(r));
        if (body.length) {
          out += '<div class="aic-table"><table><thead><tr>'
            + cells(body[0]).map((c) => "<th>" + inline(c) + "</th>").join("")
            + "</tr></thead><tbody>"
            + body.slice(1).map((r) => "<tr>" + cells(r).map((c) => "<td>" + inline(c) + "</td>").join("") + "</tr>").join("")
            + "</tbody></table></div>";
        }
        continue;
      }
      const h = line.match(/^(#{1,4})\s+(.*)$/);
      if (h) { out += '<div class="aic-h">' + inline(h[2]) + "</div>"; i++; continue; }
      if (/^\s*[-*•]\s+/.test(line)) {
        out += "<ul>";
        while (i < lines.length && /^\s*[-*•]\s+/.test(lines[i])) {
          out += "<li>" + inline(lines[i++].replace(/^\s*[-*•]\s+/, "")) + "</li>";
        }
        out += "</ul>";
        continue;
      }
      if (/^\s*\d+[.)]\s+/.test(line)) {
        out += "<ol>";
        while (i < lines.length && /^\s*\d+[.)]\s+/.test(lines[i])) {
          out += "<li>" + inline(lines[i++].replace(/^\s*\d+[.)]\s+/, "")) + "</li>";
        }
        out += "</ol>";
        continue;
      }
      if (!line.trim()) { i++; continue; }
      const para = [];
      while (i < lines.length && lines[i].trim() && !BLOCK.test(lines[i])) para.push(inline(lines[i++]));
      out += "<p>" + para.join("<br>") + "</p>";
    }
    return out;
  }

  const api = { md: md, esc: esc };
  if (typeof module !== "undefined" && module.exports) module.exports = api;
  else root.aiRecruitmentMarkdown = api;
})(typeof window !== "undefined" ? window : globalThis);
