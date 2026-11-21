import { spawnSync } from 'node:child_process';
import { copyFile, readdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import {
  assembleProgram,
  extractBlocks,
  findDivergences,
  packageCommands,
  requireBlocks,
} from './readme-blocks.mjs';

const NUGET_ORG = 'https://api.nuget.org/v3/index.json';
export const SCENARIO_ID = 'a';
const ADVISORY_WARNINGS = 'NU1901%3BNU1902%3BNU1903%3BNU1904';
const AFTER_BUILD = ['UserPipeline.Configure(app);', ''];
const BEFORE_RUN = ['UserEndpoints.Map(app);'];

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const english = extractBlocks(await readFile(join(repoRoot, 'README.md'), 'utf8'));
  const portuguese = extractBlocks(await readFile(join(repoRoot, 'README.pt-BR.md'), 'utf8'));

  requireBlocks(english, 'README.md');
  requireBlocks(portuguese, 'README.pt-BR.md');

  const divergences = findDivergences(english, portuguese);
  if (divergences.length > 0) {
    throw new Error(`${divergences.join('\n')}\nThe two READMEs must show the same code, so neither can rot unseen.`);
  }

  await writeFile(join(options.app, 'nuget.config'), nugetConfig(options.feed));

  const packages = [...packageCommands(english.get('quickstart:packages').code), ...packageCommands(english.get('mcp:package').code)];
  for (const id of packages) {
    run('dotnet', ['add', 'package', id, '--version', `[${options.version}]`, '--no-restore'], options.app);
  }

  await writeFile(
    join(options.app, 'Program.cs'),
    `${assembleProgram({
      program: english.get('quickstart:program').code,
      mcpUsings: english.get('mcp:usings').code,
      registrations: english.get('quickstart:registrations').code,
      mcpRegistration: english.get('mcp:registration').code,
      afterBuild: AFTER_BUILD,
      beforeRun: BEFORE_RUN,
    })}\n`,
  );

  for (const name of await readdir(options.userCode)) {
    await copyFile(join(options.userCode, name), join(options.app, name));
  }

  console.log('\nProgram.cs, as the README blocks and the user code make it:\n');
  console.log(await readFile(join(options.app, 'Program.cs'), 'utf8'));

  const build = run(
    'dotnet',
    ['build', '-c', 'Release', '-p:TreatWarningsAsErrors=true', `-p:WarningsNotAsErrors=${ADVISORY_WARNINGS}`],
    options.app,
    false,
  );

  if (build.status !== 0) {
    console.error(`FAIL ${SCENARIO_ID} [build] the Quick start compiles with 0 warnings, with the README blocks pasted in the order the text says`);
    process.exit(1);
  }

  console.log(`PASS ${SCENARIO_ID} [build] the Quick start compiles with 0 warnings, with the README blocks pasted in the order the text says`);
}

function run(command, args, directory, failOnError = true) {
  const result = spawnSync(command, args, { cwd: directory, stdio: 'inherit' });
  if (result.error) {
    throw result.error;
  }

  if (failOnError && result.status !== 0) {
    throw new Error(`"${command} ${args.join(' ')}" exited with ${result.status}.`);
  }

  return result;
}

function nugetConfig(feed) {
  return `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="netapiflow-ci" value="${feed}" />
    <add key="nuget.org" value="${NUGET_ORG}" />
  </packageSources>
</configuration>
`;
}

function parseArguments(argumentList) {
  const options = { userCode: join(repoRoot, 'tests/real-package/user-code') };
  for (let index = 0; index < argumentList.length; index += 2) {
    const name = argumentList[index];
    const value = argumentList[index + 1];
    if (name === '--app' || name === '--feed' || name === '--user-code') {
      options[name === '--user-code' ? 'userCode' : name.slice(2)] = resolve(value);
    } else if (name === '--version') {
      options.version = value;
    } else {
      throw new Error(`Unknown argument "${name}". Use --app <folder> --feed <folder> --version <version> [--user-code <folder>].`);
    }
  }

  for (const required of ['app', 'feed', 'version']) {
    if (!options[required]) {
      throw new Error(`Pass --${required}.`);
    }
  }

  return options;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  await main();
}
