'use strict';
const https = require('https');
const http = require('http');
const { URL, URLSearchParams } = require('url');

const UserAgent = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/142.0.0.0 Safari/537.36';
const base64 = s => Buffer.from(s, 'base64').toString('utf8');

function decodeEntities(value) {
  return String(value || '')
    .replace(/&#x([0-9a-f]+);/gi, (_, h) => String.fromCodePoint(parseInt(h, 16)))
    .replace(/&#(\d+);/g, (_, n) => String.fromCodePoint(parseInt(n, 10)))
    .replace(/&nbsp;/gi, ' ')
    .replace(/&amp;/gi, '&')
    .replace(/&lt;/gi, '<')
    .replace(/&gt;/gi, '>')
    .replace(/&quot;/gi, '"')
    .replace(/&apos;/gi, "'");
}
function attrs(raw) {
  const result = {};
  const re = /([a-z_:][a-z0-9_:.-]*)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+))/gi;
  let m;
  while ((m = re.exec(raw || ''))) result[m[1].toLowerCase()] = decodeEntities(m[2] ?? m[3] ?? m[4] ?? '');
  const flags = /(?:^|\s)(disabled|checked|selected|readonly)(?:\s|=|$)/gi;
  while ((m = flags.exec(raw || ''))) if (!(m[1].toLowerCase() in result)) result[m[1].toLowerCase()] = '';
  return result;
}
function normalize(value) {
  return decodeEntities(String(value || '').replace(/<[^>]*>/g, ' '))
    .replace(/\u00a0/g, ' ').replace(/\s+/g, ' ').trim();
}
function parseForm(html, documentUrl) {
  const forms = [...String(html || '').matchAll(/<form\b([^>]*)>([\s\S]*?)<\/form\s*>/gi)];
  let selected = null;
  for (const m of forms) {
    const a = attrs(m[1]);
    if ((a.id || '').toLowerCase() === 'form1' || (a.name || '').toLowerCase() === 'form1') {
      selected = m; break;
    }
  }
  if (!selected) throw new Error('学校页面结构已变化：找不到查询表单');
  const formAttrs = attrs(selected[1]);
  const action = new URL(formAttrs.action || documentUrl, documentUrl).toString();
  const fields = {};
  const options = {};
  const body = selected[2];
  for (const m of body.matchAll(/<input\b([^>]*)>/gi)) {
    const a = attrs(m[1]);
    if (!a.name || 'disabled' in a) continue;
    const type = (a.type || 'text').toLowerCase();
    if (['submit','image','button','reset','file'].includes(type)) continue;
    if ((type === 'checkbox' || type === 'radio') && !('checked' in a)) continue;
    fields[a.name] = a.value || '';
  }
  for (const m of body.matchAll(/<select\b([^>]*)>([\s\S]*?)<\/select\s*>/gi)) {
    const a = attrs(m[1]);
    if (!a.name || 'disabled' in a) continue;
    const values = new Set();
    let first = null, selectedValue = null;
    for (const o of m[2].matchAll(/<option\b([^>]*)>([\s\S]*?)<\/option\s*>/gi)) {
      const oa = attrs(o[1]);
      if ('disabled' in oa) continue;
      let value = oa.value;
      if (!value) value = normalize(o[2]);
      values.add(value);
      if (first === null) first = value;
      if ('selected' in oa) selectedValue = value;
    }
    fields[a.name] = selectedValue ?? first ?? '';
    options[a.name] = values;
  }
  for (const m of body.matchAll(/<textarea\b([^>]*)>([\s\S]*?)<\/textarea\s*>/gi)) {
    const a = attrs(m[1]);
    if (a.name && !('disabled' in a)) fields[a.name] = decodeEntities(normalize(m[2]));
  }
  return { action, fields, options };
}
function hasOption(form, name, value) {
  return form.options[name] && form.options[name].has(value || '');
}
function decodeBuffer(buffer, contentType) {
  const m = String(contentType || '').match(/charset\s*=\s*["']?([^;"'\s]+)/i);
  let charset = m ? m[1].toLowerCase() : 'utf-8';
  if (charset.includes('gb') || charset.includes('936')) charset = 'gb18030';
  try { return new TextDecoder(charset).decode(buffer); }
  catch { return buffer.toString('utf8'); }
}
class Session {
  constructor(base) { this.base = base; this.cookies = new Map(); }
  cookieHeader() { return [...this.cookies].map(([k,v]) => k + '=' + v).join('; '); }
  capture(headers) {
    const rows = headers['set-cookie'] || [];
    for (const row of rows) {
      const pair = String(row).split(';')[0];
      const i = pair.indexOf('=');
      if (i > 0) this.cookies.set(pair.slice(0, i).trim(), pair.slice(i + 1).trim());
    }
  }
  request(method, urlText, form, referer) {
    return new Promise((resolve, reject) => {
      const body = form ? new URLSearchParams(form).toString() : null;
      const u = new URL(urlText);
      const transport = u.protocol === 'https:' ? https : http;
      const headers = {
        'User-Agent': UserAgent,
        'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8',
        'Accept-Language': 'zh-CN,zh;q=0.9',
        'Cache-Control': 'no-cache'
      };
      const cookie = this.cookieHeader();
      if (cookie) headers.Cookie = cookie;
      if (method === 'POST') {
        headers['Content-Type'] = 'application/x-www-form-urlencoded';
        headers['Origin'] = u.origin;
        headers.Referer = referer || this.base;
        headers['Content-Length'] = Buffer.byteLength(body || '');
      }
      const req = transport.request(u, { method, headers, timeout: 20000 }, res => {
        this.capture(res.headers);
        if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
          res.resume();
          const next = new URL(res.headers.location, u).toString();
          const nextMethod = res.statusCode === 307 || res.statusCode === 308 ? method : 'GET';
          this.request(nextMethod, next, nextMethod === 'POST' ? form : null, this.base)
            .then(resolve, reject);
          return;
        }
        const chunks = [];
        res.on('data', c => chunks.push(c));
        res.on('end', () => {
          const buffer = Buffer.concat(chunks);
          if (res.statusCode < 200 || res.statusCode >= 300)
            reject(new Error('HTTP ' + res.statusCode));
          else resolve({ text: decodeBuffer(buffer, res.headers['content-type']), url: u.toString() });
        });
      });
      req.on('timeout', () => req.destroy(new Error('响应超时')));
      req.on('error', reject);
      if (body) req.write(body);
      req.end();
    });
  }
}
async function queryMeter(room, air) {
  const session = new Session(room.campus === 'south'
    ? base64('aHR0cDovLzE4My4xNjkuMjU1LjQ6ODA4MC9EZWZhdWx0LmFzcHg=')
    : base64('aHR0cHM6Ly9zZGN4LmhwdS5lZHUuY24v'));
  const meter = air ? room.air : room.lighting;
  const label = air ? '空调' : '照明';
  let r = await session.request('GET', session.base);
  let form = parseForm(r.text, r.url);
  if (!hasOption(form, 'drlouming', meter.building)) throw new Error('寝室目录与' + label + '楼栋选项不匹配');
  form.fields.drlouming = meter.building; form.fields.drceng = ''; form.fields.drfangjian = '';
  form.fields.__EVENTTARGET = 'drlouming'; form.fields.__EVENTARGUMENT = '';
  r = await session.request('POST', form.action, form.fields, session.base);
  form = parseForm(r.text, r.url);
  if (!hasOption(form, 'drceng', meter.floor)) throw new Error('寝室目录与' + label + '楼层选项不匹配');
  form.fields.drlouming = meter.building; form.fields.drceng = meter.floor; form.fields.drfangjian = '';
  form.fields.__EVENTTARGET = 'drceng'; form.fields.__EVENTARGUMENT = '';
  r = await session.request('POST', form.action, form.fields, session.base);
  form = parseForm(r.text, r.url);
  if (!hasOption(form, 'drfangjian', meter.room)) throw new Error('寝室目录与' + label + '寝室选项不匹配');
  form.fields.drlouming = meter.building; form.fields.drceng = meter.floor; form.fields.drfangjian = meter.room;
  form.fields.radio = 'usedR'; form.fields['ImageButton1.x'] = '8'; form.fields['ImageButton1.y'] = '8';
  form.fields.__EVENTTARGET = ''; form.fields.__EVENTARGUMENT = '';
  r = await session.request('POST', form.action, form.fields, session.base);
  form = parseForm(r.text, r.url);
  if (!('txtstart' in form.fields) || !('txtend' in form.fields)) throw new Error(label + '页面结构已变化：找不到日期查询框');
  const end = localDate(new Date());
  const start = localDate(new Date(Date.now() - 14 * 86400000));
  form.fields.txtstart = start; form.fields.txtend = end; form.fields.btnser = '查询';
  form.fields.__EVENTTARGET = ''; form.fields.__EVENTARGUMENT = '';
  r = await session.request('POST', form.action, form.fields, session.base);
  const plain = normalize(r.text.replace(/<(script|style|noscript)\b[^>]*>[\s\S]*?<\/\1\s*>/gi, ' '));
  const balanceMatch = plain.match(/剩余电量\s*[：:]\s*(-?\d+(?:\.\d+)?)\s*度/);
  if (!balanceMatch) throw new Error(label + '结果页找不到剩余电量');
  const rows = [];
  for (const tr of r.text.matchAll(/<tr\b[^>]*>([\s\S]*?)<\/tr\s*>/gi)) {
    const cells = [...tr[1].matchAll(/<td\b[^>]*>([\s\S]*?)<\/td\s*>/gi)].map(m => normalize(m[1]));
    if (cells.length < 4 || !/^\d{4}-\d{2}-\d{2}$/.test(cells[0])) continue;
    const k = (cells[2].replace(/,/g, '').match(/-?\d+(?:\.\d+)?/) || [])[0];
    const p = (cells[3].replace(/,/g, '').match(/-?\d+(?:\.\d+)?/) || [])[0];
    if (k === undefined || p === undefined) continue;
    rows.push({ date: cells[0], kwh: Number(k), cost: Number(k) * Number(p) });
  }
  return { balance: Number(balanceMatch[1]), rows };
}
function localDate(d) {
  const y=d.getFullYear(), m=String(d.getMonth()+1).padStart(2,'0'), day=String(d.getDate()).padStart(2,'0');
  return y+'-'+m+'-'+day;
}
(async () => {
  try {
    if (!process.argv[2]) throw new Error('缺少寝室参数');
    const room = JSON.parse(Buffer.from(process.argv[2], 'base64').toString('utf8'));
    const [light, air] = await Promise.all([queryMeter(room, false), queryMeter(room, true)]);
    const days = new Map();
    for (const row of light.rows.concat(air.rows)) {
      const target = days.get(row.date) || { date: row.date, kwh: 0, cost: 0 };
      target.kwh += row.kwh; target.cost += row.cost; days.set(row.date, target);
    }
    let latest = null;
    for (const row of days.values()) if (!latest || row.date > latest.date) latest = row;
    const now = new Date();
    const sevenStart = localDate(new Date(now.getTime() - 7 * 86400000));
    const sevenEnd = localDate(new Date(now.getTime() - 86400000));
    let sevenKwh = 0, sevenCost = 0;
    for (const row of days.values()) if (row.date >= sevenStart && row.date <= sevenEnd) { sevenKwh += row.kwh; sevenCost += row.cost; }
    const lightSeven = light.rows.filter(row => row.date >= sevenStart && row.date <= sevenEnd);
    const airSeven = air.rows.filter(row => row.date >= sevenStart && row.date <= sevenEnd);
    const lightAvg = lightSeven.reduce((sum, row) => sum + row.kwh, 0) / 7;
    const airAvg = airSeven.reduce((sum, row) => sum + row.kwh, 0) / 7;
    const lightDays = lightAvg >= 0.05 ? light.balance / lightAvg : -1;
    const airDays = airAvg >= 0.05 ? air.balance / airAvg : -1;
    const validDays = [lightDays, airDays].filter(value => value >= 0);
    const daysLeft = validDays.length ? Math.min(...validDays) : -1;
    const estimateDate = value => {
      if (!(value >= 0)) return '';
      return localDate(new Date(now.getTime() + value * 86400000));
    };
    const r2 = n => Math.round(n * 100) / 100;
    const r3 = n => Math.round(n * 1000) / 1000;
    console.log(JSON.stringify({
      lightingBalance: r2(light.balance), airBalance: r2(air.balance),
      lightingAvg7: r2(lightAvg), airAvg7: r2(airAvg), avgDaily7: r2(lightAvg + airAvg),
      lightingDaysLeft: r2(lightDays), airDaysLeft: r2(airDays), daysLeft: r2(daysLeft),
      lightingDepleteDate: estimateDate(lightDays), airDepleteDate: estimateDate(airDays),
      latestDate: latest ? latest.date : '', latestKwh: latest ? r2(latest.kwh) : 0,
      latestCost: latest ? r3(latest.cost) : 0,
      sevenDayKwh: r2(sevenKwh), sevenDayCost: r3(sevenCost),
      snapshotAt: now.toISOString(), sourceStatus: 'fresh'
    }));
  } catch (e) {
    console.log(JSON.stringify({ error: e && e.message ? e.message : String(e) }));
    process.exit(1);
  }
})();
