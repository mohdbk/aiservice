// node --test tests/js
const test = require("node:test");
const assert = require("node:assert");
const { md } = require("../../ai_recruitment/public/js/ai_markdown.js");

test("bold, list and table render", () => {
  const html = md("**Top 3**\n\n| # | Name |\n|---|---|\n| 1 | Layla |\n\n- one\n- two");
  assert.match(html, /<strong>Top 3<\/strong>/);
  assert.match(html, /<th>#<\/th><th>Name<\/th>/);
  assert.match(html, /<td>Layla<\/td>/);
  assert.match(html, /<ul><li>one<\/li><li>two<\/li><\/ul>/);
});

test("HTML is escaped", () => {
  const html = md("<script>alert(1)</script> <img src=x onerror=alert(1)>");
  assert.doesNotMatch(html, /<script>|<img/);
  assert.match(html, /&lt;script&gt;/);
});

test("only safe links become anchors", () => {
  const html = md("[bad](javascript:alert(1)) [ok](https://example.com) [rel](/app/job-applicant)");
  assert.doesNotMatch(html, /href="javascript/);
  assert.match(html, /href="https:\/\/example.com"/);
  assert.match(html, /href="\/app\/job-applicant"/);
});

test("attribute injection via quotes is neutralised", () => {
  const html = md('[x](https://e.com" onmouseover="alert(1))');
  assert.doesNotMatch(html, /" onmouseover="/);
});

test("code blocks are escaped and preserved", () => {
  const html = md("```\na < b && c\n```");
  assert.match(html, /<pre><code>a &lt; b &amp;&amp; c<\/code><\/pre>/);
});

test("Arabic text passes through", () => {
  assert.match(md("**مرحبا** بك"), /<strong>مرحبا<\/strong> بك/);
});
