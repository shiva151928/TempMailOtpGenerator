import { readFileSync } from 'node:fs';
import { detect } from './detector.mjs';
const cases = JSON.parse(readFileSync(new URL('./cases.json', import.meta.url)));
let fail = 0;
for (const c of cases.positive) {
  const trace = []; const r = detect(c, trace);
  const ok = r && r.code === c.expected;
  if (!ok) { fail++; console.log(`FAIL+ ${c.name}: expected ${c.expected}, got ${JSON.stringify(r)}\n   ${trace.join('\n   ')}`); }
  else console.log(`ok+   ${c.name}: ${r.code} conf=${r.confidence} via ${r.phrase}`);
}
for (const c of cases.negative) {
  const trace = []; const r = detect(c, trace);
  if (r) { fail++; console.log(`FAIL- ${c.name}: expected none, got ${JSON.stringify(r)}\n   ${trace.join('\n   ')}`); }
  else console.log(`ok-   ${c.name}`);
}
console.log(`\n${cases.positive.length + cases.negative.length - fail}/${cases.positive.length + cases.negative.length} passed`);
process.exit(fail ? 1 : 0);
