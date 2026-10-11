import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
const messages = await Promise.all(['fa','en'].map(locale => readFile(new URL(`../messages/${locale}.json`, import.meta.url), 'utf8').then(JSON.parse)));
function keys(value, prefix = '') {return Object.entries(value).flatMap(([key,item]) => typeof item === 'object' ? keys(item, `${prefix}${key}.`) : [`${prefix}${key}`]).sort();}
assert.deepEqual(keys(messages[0]), keys(messages[1]));
for (const instant of ['2026-01-01T12:00:00Z', '2026-07-01T12:00:00Z']) {
  assert.equal(new Intl.DateTimeFormat('en-GB-u-nu-latn', {timeZone: 'Asia/Tehran', hour: '2-digit', minute: '2-digit', hourCycle: 'h23'}).format(new Date(instant)), '15:30');
}
console.log('fa/en message parity and winter/summer Asia/Tehran formatting passed.');
