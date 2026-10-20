// Faithful JS port of OtpDetector.cs / TextNormalizer.cs used only to exercise the heuristic offline.
// .NET '$' (end, or before a final \n) is emulated with (?=\n?$).
const E = '(?=\\n?$)';
const rx = (p, i = false) => new RegExp(p, 'gu' + (i ? 'i' : ''));
const rx1 = (p, i = false) => new RegExp(p, 'u' + (i ? 'i' : ''));

export function normalize(text) {
  if (!text) return '';
  let cleaned = '';
  for (const c of text) {
    if (c === '\r') continue;
    if ('   \t\f\v'.includes(c)) { cleaned += ' '; continue; }
    if ('​‌‍⁠﻿­'.includes(c)) continue;
    cleaned += c;
  }
  let out = ''; let blank = 0;
  for (const raw of cleaned.split('\n')) {
    const line = raw.replace(/ {2,}/g, ' ').trim();
    if (line.length === 0) { blank++; if (blank === 1 && out.length > 0) out += '\n'; continue; }
    blank = 0; out += line + '\n';
  }
  return out.replace(/\s+$/u, '');
}

// Simplified stand-in for the AngleSharp-based HtmlTextExtractor (harness only).
export function htmlToText(html) {
  if (!html) return '';
  let s = html.replace(/<(head|script|style|title|noscript|template|svg)[\s\S]*?<\/\1>/gi, '');
  s = s.replace(/<br\s*\/?>/gi, '\n');
  s = s.replace(/<\/?(td|th)\b[^>]*>/gi, ' ');
  s = s.replace(/<\/?(address|article|aside|blockquote|caption|center|dd|details|div|dl|dt|fieldset|figcaption|figure|footer|form|h[1-6]|header|hr|li|main|nav|ol|p|pre|section|summary|table|tbody|tfoot|thead|tr|ul)\b[^>]*>/gi, '\n');
  s = s.replace(/<[^>]+>/g, '');
  s = s.replace(/&copy;/g, '©').replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>');
  return normalize(s);
}

const KEYWORDS = [
  [String.raw`\bverification\s+(?:code|pin|number)\b`, 1.0, true, 'verification code'],
  [String.raw`\bone[\s-]?time\s+(?:password|passcode|pass\s?code|pin|code)\b`, 1.0, true, 'one-time password'],
  [String.raw`\botp\b`, 1.0, true, 'OTP'],
  [String.raw`\bsecurity\s+(?:code|pin)\b`, 1.0, true, 'security code'],
  [String.raw`\bpass\s?code\b`, 0.95, true, 'passcode'],
  [String.raw`\b(?:log[\s-]?in|sign[\s-]?in)\s+code\b`, 0.95, true, 'login code'],
  [String.raw`\b(?:authentication|auth|authorization)\s+code\b`, 0.95, true, 'authentication code'],
  [String.raw`\b(?:confirmation|activation|validation|access|approval)\s+code\b`, 0.9, true, 'confirmation code'],
  [String.raw`\b(?:2fa|mfa|two[\s-]?factor|two[\s-]?step|2[\s-]?step)\b`, 0.9, true, 'two-factor'],
  [String.raw`\bverification\b`, 0.85, true, 'verification'],
  [String.raw`\bverify(?:ing)?\b`, 0.8, false, 'verify'],
  [String.raw`\bcode\b`, 0.7, false, 'code'],
  [String.raw`\bpin\b`, 0.7, false, 'PIN'],
  [String.raw`\bconfirm(?:ation)?\b`, 0.5, false, 'confirm'],
].map(([p, w, s, l]) => ({ re: rx(p, true), weight: w, strong: s, label: l }));

const PLAIN = rx(String.raw`(?<![\p{L}\p{N}_$€£¥₹#+@&/\\=])(?<![\p{N}][.,:\-/])([0-9]{4,8})(?![\p{L}\p{N}_@%$€£¥₹])(?![.,:\-/][\p{N}])`);
const GROUPED = rx(String.raw`(?<![\p{L}\p{N}_$€£¥₹#+@&/\\=])(?<![\p{N}][.,:\-/\s])(?<!\)\s?)([0-9]{3,4})([ \-])([0-9]{3,4})(?![\p{L}\p{N}_@%$€£¥₹])(?![.,:\-/\s]?[\p{N}])`);
const SPACED = rx(String.raw`(?<![\p{L}\p{N}_])(?<![\p{N}] )((?:[0-9] ){3,7}[0-9])(?![\p{L}\p{N}_])(?! [0-9])`);
const URL = rx(String.raw`(?:https?:\/\/|www\.)\S+`, true);
const EMAIL = rx(String.raw`[\p{L}\p{N}._%+\-]+@[\p{L}\p{N}.\-]+\.\p{L}{2,}`);

const VALUE_CUE_BEFORE = rx1(String.raw`(?:\bis|[:：=])\s*` + E, true);
const ACTION_BEFORE = rx1(String.raw`\b(?:enter|type|input|paste)\b[^\n.]{0,25}` + E, true);
const CUE_AFTER = rx1(String.raw`^\s*(?:is|was)\s+(?:your|the)\b`, true);
const NEGATIVE_BEFORE = rx1(
  String.raw`(?:\b(?:order|invoice|receipt|tracking|ticket|ref|reference|transaction|txn|booking|reservation|serial|sku|zip|postal|pin\s?code|suite|apt|flight|phone|mobile|cell|tel|telephone|fax|call|whatsapp|amount|total|price|balance|paid|usd|inr|eur|gbp|aud|cad|rs|qty|quantity|points|miles|version|build|ext|extension|year|fy|batch|lot|employee|student|roll|policy|claim)\b\.?\s*(?:no\.?|number|num|#|id)?\s*[:#\-]?\s*${E})` +
  String.raw`|(?:\b(?:account|acct|customer|member|membership|user|client|application|registration|request|session|item|model|part|unit|room|box|route|emp)\s*(?:no\.?|number|num|#|id)\s*[:#\-]?\s*${E})` +
  String.raw`|(?:\b(?:id|no\.|number|num)\s*[:#\-]?\s*${E})` +
  String.raw`|(?:\b(?:ending|ends)\s+(?:in|with)\s*${E})` +
  String.raw`|(?:[x*•]{2,}\s*${E})` +
  String.raw`|(?:\(\s*[0-9]{2,4}\s*\)\s*${E})` +
  String.raw`|(?:\+\s?[0-9]{1,3}[\s.\-]?${E})`, true);
const NEGATIVE_AFTER = rx1(
  String.raw`^\s?(?:%|(?:percent|km|mi|kg|lbs?|g|mb|gb|kb|tb|px|pt|pts|points|miles|usd|inr|eur|gbp|dollars?|rupees?|rs|items?|users?|people|views|followers|likes|times|years?|yrs|months?|days?|hours?|hrs|minutes?|mins?|seconds?|secs?|am|pm|calories|kcal|steps)\b)` +
  String.raw`|^\s\p{Lu}\p{L}+\s+(?:street|st|avenue|ave|road|rd|boulevard|blvd|lane|ln|drive|dr|parkway|pkwy|way|court|ct|place|pl)\b`, true);
const ANTI_BEFORE = rx1(String.raw`\b(?:promo(?:tion(?:al)?)?|coupon|discount|voucher|gift\s*card|referral|offer|reward|tracking|order|invoice|reference|booking)\s+(?:code|number|no\.?|#)`, true);
const DIGIT_SPACE_BEFORE = rx1(String.raw`[0-9] ` + E);
const SPACE_DIGIT_AFTER = rx1(String.raw`^ [0-9]`);
const DATE_CONTEXT = rx1(String.raw`\b(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?|mon(?:day)?|tue(?:s(?:day)?)?|wed(?:nesday)?|thu(?:rs(?:day)?)?|fri(?:day)?|saturday|sunday|years?|fy|since|copyright|rights\s+reserved|established|founded)\b|©|\(c\)`, true);
const DATE_BEFORE = rx1(String.raw`(?:\bmay|\bq[1-4]|\b[0-9]{1,2}(?:st|nd|rd|th)?,?)\s*` + E, true);

const lengthBonus = (n) => ({ 6: 12, 4: 6, 5: 6, 8: 6, 7: 4 }[n] ?? 0);

function prepare(text) {
  if (!text || !text.trim()) return '';
  if (text.length > 20000) text = text.slice(0, 20000);
  let n = normalize(text);
  n = n.replace(URL, ' ');
  n = n.replace(EMAIL, ' ');
  return n;
}

function findKeywords(text) {
  const hits = [];
  for (const k of KEYWORDS) for (const m of text.matchAll(k.re)) hits.push({ k, start: m.index, end: m.index + m[0].length });
  return hits;
}

function isStandalone(text, start, end) {
  const ls = start === 0 ? 0 : text.lastIndexOf('\n', start - 1) + 1;
  let le = text.indexOf('\n', end); if (le < 0) le = text.length;
  const line = text.slice(ls, le).trim().replace(/[.!。]+$/u, '');
  return line.length === end - start;
}

function score(text, start, length, code, kind, source, hits, subjectHit, trace) {
  const end = start + length;
  let kw = 0, phrase = null;
  for (const h of hits) {
    let d, dir;
    if (h.end <= start) { d = start - h.end; dir = 1.0; }
    else if (h.start >= end) { d = h.start - end; dir = 0.85; }
    else continue;
    if (d > 160) continue;
    const pts = 60 * h.k.weight * (1 - d / 160) * dir;
    if (pts > kw) { kw = pts; phrase = h.k.label; }
  }
  if (subjectHit) { const sp = 20 * subjectHit.k.weight; if (sp > kw) { kw = sp; phrase = subjectHit.k.label; } }
  if (kind === 'grouped' && kw < 30) return null;
  if (kind === 'spaced' && kw < 35) return null;
  const before = text.slice(Math.max(0, start - 40), start);
  const after = text.slice(end, Math.min(text.length, end + 40));
  const notes = [`kw=${kw.toFixed(1)}(${phrase})`];
  if (code.length === 4) {
    const n = Number(code);
    if (n >= 1900 && n <= 2099) {
      const win = text.slice(Math.max(0, start - 30), Math.min(text.length, end + 30));
      if (DATE_CONTEXT.test(win) || DATE_BEFORE.test(before)) { trace?.push(`${code}: rejected (date)`); return null; }
      kw -= 12; notes.push('year-12');
    }
  }
  let s = kw + lengthBonus(code.length);
  if (isStandalone(text, start, end)) { s += 20; notes.push('standalone+20'); }
  if (VALUE_CUE_BEFORE.test(before)) { s += 10; notes.push('is/:+10'); }
  if (CUE_AFTER.test(after)) { s += 15; notes.push('is-your+15'); }
  if (ACTION_BEFORE.test(before)) { s += 10; notes.push('enter+10'); }
  if (source === 'subject') { s += 5; notes.push('subject+5'); }
  if (NEGATIVE_BEFORE.test(before)) { s -= 45; notes.push('negBefore-45'); }
  if (NEGATIVE_AFTER.test(after)) { s -= 40; notes.push('negAfter-40'); }
  if (ANTI_BEFORE.test(before)) { s -= 40; notes.push('anti-40'); }
  if (kind === 'plain' && (DIGIT_SPACE_BEFORE.test(before) || SPACE_DIGIT_AFTER.test(after))) { s -= 50; notes.push('spacedNum-50'); }
  trace?.push(`${code} [${kind}/${source}] score=${s.toFixed(1)} ${notes.join(' ')}`);
  return { code, score: s, source, phrase, index: start };
}

function* candidates(text, source, hits, subjectHit, trace) {
  for (const m of text.matchAll(PLAIN)) { const c = score(text, m.index, m[0].length, m[1], 'plain', source, hits, subjectHit, trace); if (c) yield c; }
  for (const m of text.matchAll(GROUPED)) { const code = m[1] + m[3]; if (code.length < 6 || code.length > 8) continue; const c = score(text, m.index, m[0].length, code, 'grouped', source, hits, subjectHit, trace); if (c) yield c; }
  for (const m of text.matchAll(SPACED)) { const code = m[1].replaceAll(' ', ''); const c = score(text, m.index, m[0].length, code, 'spaced', source, hits, subjectHit, trace); if (c) yield c; }
}

export function detect({ subject, text, html }, trace) {
  const segs = [];
  const sub = prepare(subject); if (sub) segs.push([sub, 'subject']);
  const t = prepare(text); if (t) segs.push([t, 'body']);
  if (html && html.trim()) { const h = prepare(htmlToText(html)); if (h && h !== t) segs.push([h, 'body']); }
  if (!segs.length) return null;
  const hitsPer = segs.map(([s]) => findKeywords(s));
  if (hitsPer.every((h) => h.length === 0)) return null;
  const strong = hitsPer.some((h) => h.some((x) => x.k.strong));
  let subjectHit = null;
  if (segs[0][1] === 'subject' && hitsPer[0].length) subjectHit = hitsPer[0].reduce((a, b) => (b.k.weight > a.k.weight ? b : a));
  const best = new Map(); const srcs = new Map();
  segs.forEach(([s, source], i) => {
    for (const c of candidates(s, source, hitsPer[i], source === 'body' ? subjectHit : null, trace)) {
      if (!srcs.has(c.code)) srcs.set(c.code, new Set()); srcs.get(c.code).add(c.source);
      if (!best.has(c.code) || c.score > best.get(c.code).score) best.set(c.code, c);
    }
  });
  if (!best.size) return null;
  const ranked = [...best.values()].map((c) => (srcs.get(c.code).size > 1 ? { ...c, score: c.score + 6 } : c))
    .sort((a, b) => b.score - a.score || (a.source === 'subject' ? 0 : 1) - (b.source === 'subject' ? 0 : 1) || a.index - b.index);
  const w = ranked[0];
  const threshold = strong ? 45 : 55;
  trace?.push(`winner ${w.code} ${w.score.toFixed(1)} threshold ${threshold}`);
  if (w.score < threshold) return null;
  return { code: w.code, confidence: Math.round(Math.min(1, Math.max(0, w.score / 90)) * 100) / 100, source: w.source, phrase: w.phrase };
}
