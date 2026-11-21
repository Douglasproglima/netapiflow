import { mkdir, readdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { inflateRawSync } from 'node:zlib';

const END_OF_CENTRAL_DIRECTORY = 0x06054b50;
const CENTRAL_DIRECTORY_ENTRY = 0x02014b50;
const LOCAL_FILE_HEADER = 0x04034b50;
const STORED = 0;
const DEFLATED = 8;
const VERSION_PLACEHOLDER = '{version}';
const NO_TARGET_FRAMEWORK = '(any framework)';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const defaultSnapshotDirectory = join(repoRoot, 'tests/nuspec-snapshot');

export function readNuspec(nupkg) {
  const entry = centralDirectoryEntries(nupkg).find((candidate) => candidate.name.endsWith('.nuspec') && !candidate.name.includes('/'));
  if (entry === undefined) {
    throw new Error('The package has no .nuspec at its root.');
  }

  const nameLength = nupkg.readUInt16LE(entry.localHeaderOffset + 26);
  const extraLength = nupkg.readUInt16LE(entry.localHeaderOffset + 28);
  const start = entry.localHeaderOffset + 30 + nameLength + extraLength;
  const data = nupkg.subarray(start, start + entry.compressedSize);

  if (entry.method === STORED) {
    return data.toString('utf8');
  }

  if (entry.method === DEFLATED) {
    return inflateRawSync(data).toString('utf8');
  }

  throw new Error(`The .nuspec uses compression method ${entry.method}, which this script does not read.`);
}

function centralDirectoryEntries(buffer) {
  let end = buffer.length - 22;
  while (end >= 0 && buffer.readUInt32LE(end) !== END_OF_CENTRAL_DIRECTORY) {
    end--;
  }

  if (end < 0) {
    throw new Error('This is not a zip file: the end of the central directory is missing.');
  }

  const count = buffer.readUInt16LE(end + 10);
  let offset = buffer.readUInt32LE(end + 16);
  const entries = [];

  for (let index = 0; index < count; index++) {
    if (buffer.readUInt32LE(offset) !== CENTRAL_DIRECTORY_ENTRY) {
      throw new Error('The central directory of the zip file is damaged.');
    }

    const nameLength = buffer.readUInt16LE(offset + 28);
    const extraLength = buffer.readUInt16LE(offset + 30);
    const commentLength = buffer.readUInt16LE(offset + 32);
    const localHeaderOffset = buffer.readUInt32LE(offset + 42);

    if (buffer.readUInt32LE(localHeaderOffset) !== LOCAL_FILE_HEADER) {
      throw new Error('A local file header of the zip file is damaged.');
    }

    entries.push({
      name: buffer.toString('utf8', offset + 46, offset + 46 + nameLength),
      method: buffer.readUInt16LE(offset + 10),
      compressedSize: buffer.readUInt32LE(offset + 20),
      localHeaderOffset,
    });

    offset += 46 + nameLength + extraLength + commentLength;
  }

  return entries;
}

export function normalizeNuspec(xml) {
  const id = elementText(xml, 'id');
  const version = elementText(xml, 'version');
  const dependencies = groupsOf(xml, 'dependencies').map(({ framework, body }) => ({
    framework: normalizeFramework(framework),
    lines: [...body.matchAll(/<dependency\s+([^>]*?)\/?>/g)]
      .map(([, attributes]) => dependencyLine(attributes, version))
      .sort(compareOrdinal),
  }));
  const frameworkReferences = groupsOf(xml, 'frameworkReferences').map(({ framework, body }) => ({
    framework: normalizeFramework(framework),
    lines: [...body.matchAll(/<frameworkReference\s+([^>]*?)\/?>/g)]
      .map(([, attributes]) => attribute(attributes, 'name'))
      .sort(compareOrdinal),
  }));

  return [
    `id: ${id}`,
    ...section('dependencies', dependencies),
    ...section('frameworkReferences', frameworkReferences),
    '',
  ].join('\n');
}

function section(title, groups) {
  if (groups.length === 0) {
    return [`${title}: (none)`];
  }

  return [
    `${title}:`,
    ...[...groups]
      .sort((left, right) => compareOrdinal(left.framework, right.framework))
      .flatMap(({ framework, lines }) => [`  ${framework}:`, ...(lines.length === 0 ? ['    (none)'] : lines.map((line) => `    ${line}`))]),
  ];
}

function groupsOf(xml, containerName) {
  const container = new RegExp(`<${containerName}>([\\s\\S]*?)</${containerName}>`).exec(xml);
  if (container === null) {
    return [];
  }

  const groups = [...container[1].matchAll(/<group\s+targetFramework="([^"]*)"\s*(?:\/>|>([\s\S]*?)<\/group>)/g)].map(
    ([, framework, body = '']) => ({ framework, body }),
  );

  return groups.length > 0 ? groups : [{ framework: '', body: container[1] }];
}

function dependencyLine(attributes, ownVersion) {
  const id = attribute(attributes, 'id');
  const range = (attribute(attributes, 'version') ?? '').split(ownVersion).join(VERSION_PLACEHOLDER);
  const include = attribute(attributes, 'include');
  const exclude = attribute(attributes, 'exclude');
  const qualifiers = [include ? `include=${include}` : '', exclude ? `exclude=${exclude}` : ''].filter(Boolean);

  return [`${id} ${range}`, ...(qualifiers.length > 0 ? [`(${qualifiers.join(' ')})`] : [])].join(' ');
}

function normalizeFramework(framework) {
  if (framework === '') {
    return NO_TARGET_FRAMEWORK;
  }

  const netCoreApp = /^\.NETCoreApp(\d+\.\d+)$/.exec(framework);
  return netCoreApp === null ? framework : `net${netCoreApp[1]}`;
}

function elementText(xml, name) {
  const match = new RegExp(`<${name}>([^<]*)</${name}>`).exec(xml);
  if (match === null) {
    throw new Error(`The .nuspec has no <${name}> element.`);
  }

  return match[1].trim();
}

function attribute(attributes, name) {
  const match = new RegExp(`(?:^|\\s)${name}="([^"]*)"`).exec(attributes);
  return match === null ? undefined : decodeEntities(match[1]);
}

function decodeEntities(value) {
  return value.replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&quot;', '"').replaceAll('&amp;', '&');
}

function compareOrdinal(left, right) {
  if (left < right) {
    return -1;
  }

  return left > right ? 1 : 0;
}

export function lineDiff(approved, actual) {
  const before = approved.split('\n');
  const after = actual.split('\n');
  const lengths = Array.from({ length: before.length + 1 }, () => new Array(after.length + 1).fill(0));

  for (let row = before.length - 1; row >= 0; row--) {
    for (let column = after.length - 1; column >= 0; column--) {
      lengths[row][column] =
        before[row] === after[column] ? lengths[row + 1][column + 1] + 1 : Math.max(lengths[row + 1][column], lengths[row][column + 1]);
    }
  }

  const result = [];
  let row = 0;
  let column = 0;
  while (row < before.length || column < after.length) {
    if (row < before.length && column < after.length && before[row] === after[column]) {
      result.push(`  ${before[row]}`);
      row++;
      column++;
    } else if (row < before.length && (column === after.length || lengths[row + 1][column] >= lengths[row][column + 1])) {
      result.push(`- ${before[row]}`);
      row++;
    } else {
      result.push(`+ ${after[column]}`);
      column++;
    }
  }

  return result;
}

export async function snapshotsOfFeed(feedDirectory) {
  const names = (await readdir(feedDirectory)).filter((name) => name.endsWith('.nupkg')).sort();
  const snapshots = new Map();

  for (const name of names) {
    const text = normalizeNuspec(readNuspec(await readFile(join(feedDirectory, name))));
    snapshots.set(/^id: (.+)$/m.exec(text)[1], text);
  }

  return snapshots;
}

export function findProblems(actual, approved) {
  const problems = [];

  for (const [id, text] of actual) {
    const approvedText = approved.get(id);
    if (approvedText === undefined) {
      problems.push(`${id} has no approved snapshot (expected ${id}.txt).`);
    } else if (approvedText !== text) {
      problems.push([`${id} declares something different from its approved snapshot:`, ...lineDiff(approvedText, text)].join('\n'));
    }
  }

  for (const id of approved.keys()) {
    if (!actual.has(id)) {
      problems.push(`${id}.txt is approved, but the feed has no such package. Was it renamed or removed?`);
    }
  }

  return problems;
}

async function approvedSnapshots(snapshotDirectory) {
  const names = (await readdir(snapshotDirectory)).filter((name) => name.endsWith('.txt')).sort();
  const approved = new Map();

  for (const name of names) {
    approved.set(name.slice(0, -'.txt'.length), (await readFile(join(snapshotDirectory, name), 'utf8')).replaceAll('\r\n', '\n'));
  }

  return approved;
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const snapshotDirectory = options.snapshots ?? defaultSnapshotDirectory;

  if (options.update) {
    await mkdir(snapshotDirectory, { recursive: true });
    const actual = await snapshotsOfFeed(options.feed);
    for (const [id, text] of actual) {
      await writeFile(join(snapshotDirectory, `${id}.txt`), text);
    }

    console.log(`Wrote ${actual.size} snapshots to ${snapshotDirectory}. Review the diff and commit it.`);
    return;
  }

  const actual = await snapshotsOfFeed(options.feed);
  const problems = findProblems(actual, await approvedSnapshots(snapshotDirectory));
  if (problems.length > 0) {
    console.error(problems.join('\n\n'));
    console.error(
      `\nWhat a package declares as a dependency is the floor everyone who installs it must meet. `
        + `If this change is intended, run: node scripts/nuspec-snapshot.mjs --feed ${options.feed} --update, and commit the files it writes.`,
    );
    process.exit(1);
  }

  console.log(`All ${actual.size} packages declare what their snapshot approves.`);
}

function parseArguments(argumentList) {
  const options = { update: false };
  for (let index = 0; index < argumentList.length; index++) {
    const name = argumentList[index];
    if (name === '--update') {
      options.update = true;
    } else if (name === '--feed' || name === '--snapshots') {
      options[name.slice(2)] = argumentList[++index];
    } else {
      throw new Error(`Unknown argument "${name}". Use --feed <folder> [--snapshots <folder>] [--update].`);
    }
  }

  if (!options.feed) {
    throw new Error('Pass the folder with the .nupkg files: --feed <folder>.');
  }

  return options;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  await main();
}
