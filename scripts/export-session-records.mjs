// Read-only extraction of visible conversation text from explicitly selected project records.
// Does not export system/developer instructions, reasoning, tool payloads or binary attachments.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';

const [kind, source, destination] = process.argv.slice(2);
if (!['codex', 'claude'].includes(kind) || !source || !destination) {
  throw new Error('Usage: node scripts/export-session-records.mjs codex|claude <source.jsonl> <output-directory>');
}

const raw = await readFile(source, 'utf8');
const lines = raw.split(/\r?\n/);
const messages = [];
const seen = new Set();
const activity = [];
let firstSourceTimestamp;
let lastSourceTimestamp;
let omittedReasoning = 0;
let redactions = 0;
let malformed = 0;

function sanitize(value) {
  return value
    .replace(/\b(?:sk|rk)_(?:live|test)_[A-Za-z0-9]{20,}\b|\bwhsec_[A-Za-z0-9]{20,}\b|\bsk-proj-[A-Za-z0-9_-]{20,}\b|\bgh[pousr]_[A-Za-z0-9]{20,}\b/g, () => {
      redactions++;
      return '[REDACTED_CREDENTIAL_PATTERN]';
    })
    .replace(/data:image\/[a-zA-Z0-9.+-]+;base64,[A-Za-z0-9+/=]+/g, '[BINARY_IMAGE_OMITTED; see project assets]');
}

function textFrom(content) {
  if (typeof content === 'string') return content;
  if (!Array.isArray(content)) return '';
  return content.map(block => {
    if (['text', 'input_text', 'output_text'].includes(block.type)) return block.text ?? '';
    if (['image', 'input_image', 'image_url'].includes(block.type)) return '[Image attachment omitted from text; see original requirement images or project assets.]';
    return '';
  }).filter(Boolean).join('\n\n');
}

for (let index = 0; index < lines.length; index++) {
  if (!lines[index].trim()) continue;
  let row;
  try { row = JSON.parse(lines[index]); } catch { malformed++; continue; }
  if (row.timestamp) {
    firstSourceTimestamp ??= row.timestamp;
    lastSourceTimestamp = row.timestamp;
  }
  let role;
  let text;
  let phase;
  let id;
  if (kind === 'codex' && row.type === 'response_item') {
    const item = row.payload;
    if (item?.type === 'reasoning') { omittedReasoning++; continue; }
    if (['function_call', 'custom_tool_call', 'function_call_output', 'custom_tool_call_output'].includes(item?.type)) {
      activity.push({ timestamp: row.timestamp, type: item.type, name: item.name ?? null, id: item.call_id ?? item.id ?? null });
    }
    if (item?.type !== 'message' || !['user', 'assistant'].includes(item.role)) continue;
    if (item.channel === 'analysis' || item.phase === 'analysis') { omittedReasoning++; continue; }
    role = item.role;
    text = textFrom(item.content);
    phase = item.phase ?? item.channel ?? null;
    id = item.id;
  } else if (kind === 'claude' && ['user', 'assistant'].includes(row.type)) {
    role = row.type;
    text = textFrom(row.message?.content);
    id = row.uuid;
    omittedReasoning += Array.isArray(row.message?.content) ? row.message.content.filter(block => block.type === 'thinking').length : 0;
    for (const block of Array.isArray(row.message?.content) ? row.message.content : []) {
      if (block.type === 'tool_use') activity.push({ timestamp: row.timestamp, type: 'tool_use', name: block.name, id: block.id });
    }
  } else continue;
  if (!text?.trim()) continue;
  const key = `${id ?? ''}:${role}:${text}`;
  if (seen.has(key)) continue;
  seen.add(key);
  messages.push({ timestamp: row.timestamp ?? null, role, phase, sourceLine: index + 1, text: sanitize(text) });
}

await mkdir(destination, { recursive: true });
const generatedAt = new Date().toISOString();
const metadata = {
  kind, generatedAt, sourceFile: path.basename(source),
  sourceSha256: createHash('sha256').update(raw).digest('hex'),
  firstSourceTimestamp, lastSourceTimestamp,
  sourceBytes: Buffer.byteLength(raw), messages: messages.length,
  credentialPatternRedactions: redactions, omittedReasoningRecords: omittedReasoning,
  malformedOrIncompleteLines: malformed,
  format: 'Derived visible-message text snapshot, not a native app export or unmodified raw file',
  exclusions: ['private reasoning', 'system/developer messages', 'tool arguments/results', 'binary images'],
};
const formatter = new Intl.DateTimeFormat('en-US', { timeZone: 'America/New_York', dateStyle: 'medium', timeStyle: 'medium' });
const markdown = [
  `# ${kind === 'codex' ? 'Codex' : 'Claude'} — visible conversation snapshot`,
  '', `Generated: ${generatedAt}. Source: ${path.basename(source)}.`, '',
  '**Derived text snapshot, not a native app export.** Text is extracted from the source records in order, not reconstructed from memory. Private reasoning, system/developer messages, tool arguments/results and binary payloads are excluded. Images and implementation artifacts remain in the project. Credential-pattern redactions, if any, are marked.', '',
  `Source timestamp range (UTC): ${firstSourceTimestamp} through ${lastSourceTimestamp}.`,
  `Messages: ${messages.length}; redactions: ${redactions}; malformed/incomplete source lines skipped: ${malformed}.`, '',
  'The source was captured while sessions may still be active. Later messages, including a final response generated after this extraction, are not included. See export-metadata.json for provenance and SHA-256.', '',
  ...messages.flatMap((message, index) => [
    `## ${index + 1}. ${message.role === 'assistant' ? 'Assistant' : 'User'}${message.phase ? ` (${message.phase})` : ''} — ${message.timestamp ? formatter.format(new Date(message.timestamp)) + ' Eastern' : 'timestamp unavailable'}`,
    '', `Source line: ${message.sourceLine}.`, '', message.text, '',
  ]),
].join('\n');
await writeFile(path.join(destination, 'conversation.md'), markdown);
await writeFile(path.join(destination, 'visible-messages.jsonl'), messages.map(message => JSON.stringify(message)).join('\n') + '\n');
await writeFile(path.join(destination, 'tool-activity-index.json'), JSON.stringify(activity, null, 2));
await writeFile(path.join(destination, 'export-metadata.json'), JSON.stringify(metadata, null, 2));
console.log(JSON.stringify(metadata));
