import assert from 'node:assert/strict';
import { test } from 'node:test';
import { deflateRawSync } from 'node:zlib';
import { findDependenciesOnOurPackagesThatAreNotExact, findProblems, lineDiff, normalizeNuspec, readNuspec } from './nuspec-snapshot.mjs';

const NUSPEC = `<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>NetApiFlow.EntityFrameworkCore</id>
    <version>0.6.0-ci.7</version>
    <dependencies>
      <group targetFramework=".NETCoreApp10.0">
        <dependency id="NetApiFlow" version="[0.6.0-ci.7]" exclude="Build,Analyzers" />
        <dependency id="Microsoft.EntityFrameworkCore" version="8.0.10" />
      </group>
      <group targetFramework="net8.0">
        <dependency id="Microsoft.EntityFrameworkCore" version="8.0.10" />
        <dependency id="NetApiFlow" version="[0.6.0-ci.7]" />
      </group>
    </dependencies>
    <frameworkReferences>
      <group targetFramework="net8.0">
        <frameworkReference name="Microsoft.AspNetCore.App" />
      </group>
    </frameworkReferences>
  </metadata>
</package>`;

function zipWith(name, content, method = 8) {
  const data = method === 8 ? deflateRawSync(Buffer.from(content)) : Buffer.from(content);
  const nameBytes = Buffer.from(name);

  const local = Buffer.alloc(30);
  local.writeUInt32LE(0x04034b50, 0);
  local.writeUInt16LE(method, 8);
  local.writeUInt32LE(data.length, 18);
  local.writeUInt32LE(content.length, 22);
  local.writeUInt16LE(nameBytes.length, 26);
  const localPart = Buffer.concat([local, nameBytes, data]);

  const central = Buffer.alloc(46);
  central.writeUInt32LE(0x02014b50, 0);
  central.writeUInt16LE(method, 10);
  central.writeUInt32LE(data.length, 20);
  central.writeUInt32LE(content.length, 24);
  central.writeUInt16LE(nameBytes.length, 28);
  central.writeUInt32LE(0, 42);
  const centralPart = Buffer.concat([central, nameBytes]);

  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(1, 8);
  end.writeUInt16LE(1, 10);
  end.writeUInt32LE(centralPart.length, 12);
  end.writeUInt32LE(localPart.length, 16);

  return Buffer.concat([localPart, centralPart, end]);
}

test('readNuspec_ReadsTheNuspecAtTheRootOfADeflatedZip', () => {
  assert.equal(readNuspec(zipWith('NetApiFlow.nuspec', NUSPEC)), NUSPEC);
});

test('readNuspec_ReadsAStoredEntryToo', () => {
  assert.equal(readNuspec(zipWith('NetApiFlow.nuspec', NUSPEC, 0)), NUSPEC);
});

test('readNuspec_WhenThereIsNoNuspecAtTheRoot_Throws', () => {
  assert.throws(() => readNuspec(zipWith('lib/net8.0/NetApiFlow.nuspec', NUSPEC)), /no \.nuspec at its root/);
});

test('readNuspec_WhenTheBufferIsNotAZip_Throws', () => {
  assert.throws(() => readNuspec(Buffer.from('not a zip file at all, just some text to read')), /not a zip file/);
});

test('normalizeNuspec_ReplacesTheOwnVersionAndOrdersGroupsAndDependencies', () => {
  assert.equal(
    normalizeNuspec(NUSPEC),
    [
      'id: NetApiFlow.EntityFrameworkCore',
      'dependencies:',
      '  net10.0:',
      '    Microsoft.EntityFrameworkCore 8.0.10',
      '    NetApiFlow [{version}] (exclude=Build,Analyzers)',
      '  net8.0:',
      '    Microsoft.EntityFrameworkCore 8.0.10',
      '    NetApiFlow [{version}]',
      'frameworkReferences:',
      '  net8.0:',
      '    Microsoft.AspNetCore.App',
      '',
    ].join('\n'),
  );
});

test('normalizeNuspec_WhenThePackageHasNoDependencies_SaysSo', () => {
  const text = normalizeNuspec('<package><metadata><id>NetApiFlow</id><version>1.0.0</version></metadata></package>');

  assert.equal(text, 'id: NetApiFlow\ndependencies: (none)\nframeworkReferences: (none)\n');
});

test('normalizeNuspec_ThePackageVersionNeverReachesTheOutput', () => {
  const first = normalizeNuspec(NUSPEC);
  const second = normalizeNuspec(NUSPEC.replaceAll('0.6.0-ci.7', '0.7.1-ci.12'));

  assert.equal(first, second);
});

test('lineDiff_MarksTheFloorThatChanged', () => {
  const diff = lineDiff('id: A\n  X 8.0.10\n  Y 1.0.0\n', 'id: A\n  X 9.0.22\n  Y 1.0.0\n');

  assert.deepEqual(
    diff.filter((line) => line.startsWith('-') || line.startsWith('+')),
    ['- ' + '  X 8.0.10', '+ ' + '  X 9.0.22'],
  );
});

test('findProblems_WhenEverythingMatches_ReportsNothing', () => {
  const snapshots = new Map([['A', 'id: A\n']]);

  assert.deepEqual(findProblems(snapshots, new Map(snapshots)), []);
});

test('findProblems_WhenAPackageHasNoApprovedSnapshot_NamesIt', () => {
  const [problem] = findProblems(new Map([['NetApiFlow.New', 'id: NetApiFlow.New\n']]), new Map());

  assert.match(problem, /NetApiFlow\.New has no approved snapshot/);
});

test('findProblems_WhenASnapshotHasNoPackage_NamesIt', () => {
  const [problem] = findProblems(new Map(), new Map([['NetApiFlow.Old', 'id: NetApiFlow.Old\n']]));

  assert.match(problem, /NetApiFlow\.Old\.txt is approved, but the feed has no such package/);
});

test('findProblems_WhenAFloorChanged_ShowsTheDiff', () => {
  const approved = new Map([['A', 'id: A\n    X 8.0.10\n']]);
  const actual = new Map([['A', 'id: A\n    X 9.0.22\n']]);

  const [problem] = findProblems(actual, approved);

  assert.match(problem, /A declares something different/);
  assert.match(problem, /- {5}X 8\.0\.10/);
  assert.match(problem, /\+ {5}X 9\.0\.22/);
});

function nuspecDepending(...dependencies) {
  return `<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>NetApiFlow.Dashboard</id>
    <version>0.6.0</version>
    <dependencies>
      <group targetFramework="net8.0">
${dependencies.map(([id, range]) => `        <dependency id="${id}" version="${range}" exclude="Build,Analyzers" />`).join('\n')}
      </group>
      <group targetFramework="net10.0">
${dependencies.map(([id, range]) => `        <dependency id="${id}" version="${range}" exclude="Build,Analyzers" />`).join('\n')}
      </group>
    </dependencies>
  </metadata>
</package>`;
}

test('findDependenciesOnOurPackagesThatAreNotExact_WhenTheCoreIsAskedForExactly_ReportsNothing', () => {
  assert.deepEqual(findDependenciesOnOurPackagesThatAreNotExact(NUSPEC), []);
});

test('findDependenciesOnOurPackagesThatAreNotExact_WhenTheCoreIsAskedForAFloor_NamesThePackageAndTheDependencyOnceForAllTheFrameworks', () => {
  const problems = findDependenciesOnOurPackagesThatAreNotExact(nuspecDepending(['NetApiFlow', '0.6.0']));

  assert.equal(problems.length, 1);
  assert.match(problems[0], /NetApiFlow\.Dashboard depends on NetApiFlow 0\.6\.0, which accepts any later release/);
  assert.match(problems[0], /exactly \[0\.6\.0\]/);
});

test('findDependenciesOnOurPackagesThatAreNotExact_WhenAnotherOfOurPackagesIsAskedForAFloor_ReportsIt', () => {
  const problems = findDependenciesOnOurPackagesThatAreNotExact(nuspecDepending(['NetApiFlow.Dashboard.Extras', '[0.5.9]']));

  assert.equal(problems.length, 1);
  assert.match(problems[0], /depends on NetApiFlow\.Dashboard\.Extras \[0\.5\.9\]/);
});

test('findDependenciesOnOurPackagesThatAreNotExact_WhenAPackageOfAThirdPartyHasAFloor_ReportsNothing', () => {
  assert.deepEqual(
    findDependenciesOnOurPackagesThatAreNotExact(nuspecDepending(['Microsoft.Extensions.FileProviders.Embedded', '8.0.34'])),
    [],
  );
});

test('findDependenciesOnOurPackagesThatAreNotExact_WhenANameOnlyStartsLikeOursWithoutADot_IsNotOurs', () => {
  assert.deepEqual(findDependenciesOnOurPackagesThatAreNotExact(nuspecDepending(['NetApiFlowLike', '1.0.0'])), []);
});

test('findDependenciesOnOurPackagesThatAreNotExact_WhenThePackageHasNoDependencies_ReportsNothing', () => {
  assert.deepEqual(findDependenciesOnOurPackagesThatAreNotExact(nuspecDepending()), []);
});

test('findDependenciesOnOurPackagesThatAreNotExact_ForAPreRelease_ExpectsTheExactPreReleaseRange', () => {
  const prerelease = NUSPEC.replace('<id>NetApiFlow.EntityFrameworkCore</id>', '<id>NetApiFlow.Mcp</id>');

  assert.deepEqual(findDependenciesOnOurPackagesThatAreNotExact(prerelease), []);
  assert.equal(findDependenciesOnOurPackagesThatAreNotExact(prerelease.replaceAll('[0.6.0-ci.7]', '0.6.0-ci.7')).length, 1);
});
