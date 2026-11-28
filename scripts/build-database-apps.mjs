import { spawnSync } from 'node:child_process';
import { cp, mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const NUGET_ORG = 'https://api.nuget.org/v3/index.json';
const ADVISORY_WARNINGS = 'NU1901%3BNU1902%3BNU1903%3BNU1904';
const PACKAGES = ['NetApiFlow', 'NetApiFlow.Dashboard', 'NetApiFlow.Storage.PostgreSql', 'NetApiFlow.Storage.SqlServer'];

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const appTemplate = join(repoRoot, 'tests/real-package/database-app');
const previousVersionFile = join(repoRoot, 'tests/real-package/previous-published-version.txt');

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const previousVersion = (await readFile(previousVersionFile, 'utf8')).trim();

  await buildApp(join(options.out, 'new'), options.version, [options.feed, NUGET_ORG]);
  await buildApp(join(options.out, 'old'), previousVersion, [NUGET_ORG]);

  console.log(`\nBuilt the database app against ${options.version} (new, from the feed) and ${previousVersion} (old, from nuget.org).`);
}

async function buildApp(directory, version, sources) {
  await mkdir(directory, { recursive: true });
  await cp(appTemplate, directory, { recursive: true });
  await writeFile(join(directory, 'nuget.config'), nugetConfig(sources));

  for (const id of PACKAGES) {
    run('dotnet', ['add', 'package', id, '--version', `[${version}]`, '--no-restore'], directory);
  }

  run('dotnet', ['build', '-c', 'Release', '-p:TreatWarningsAsErrors=true', `-p:WarningsNotAsErrors=${ADVISORY_WARNINGS}`], directory);
}

function run(command, args, directory) {
  const result = spawnSync(command, args, { cwd: directory, stdio: 'inherit' });
  if (result.error) {
    throw result.error;
  }

  if (result.status !== 0) {
    throw new Error(`"${command} ${args.join(' ')}" exited with ${result.status} in ${directory}.`);
  }
}

function nugetConfig(sources) {
  const lines = sources.map((source, index) => `    <add key="source-${index + 1}" value="${source}" />`);
  return `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
${lines.join('\n')}
  </packageSources>
</configuration>
`;
}

function parseArguments(argumentList) {
  const options = {};
  for (let index = 0; index < argumentList.length; index += 2) {
    const name = argumentList[index];
    const value = argumentList[index + 1];
    if (name === '--out' || name === '--feed') {
      options[name.slice(2)] = resolve(value);
    } else if (name === '--version') {
      options.version = value;
    } else {
      throw new Error(`Unknown argument "${name}". Use --out <folder> --feed <folder> --version <version>.`);
    }
  }

  for (const required of ['out', 'feed', 'version']) {
    if (!options[required]) {
      throw new Error(`Pass --${required}.`);
    }
  }

  return options;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  await main();
}
