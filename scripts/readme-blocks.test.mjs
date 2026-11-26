import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  assembleProgram,
  extractBlocks,
  findDivergences,
  packageCommands,
  REQUIRED_MARKERS,
  requireBlocks,
  splitAtBuild,
  withoutTrailingComments,
  withUsings,
} from './readme-blocks.mjs';

const FENCE = '```';

function readme(blocks) {
  return Object.entries(blocks)
    .map(([marker, code]) => `Some text.\n\n${FENCE}csharp ${marker}\n${code}\n${FENCE}\n`)
    .join('\n');
}

function allBlocks(overrides = {}) {
  return {
    'quickstart:packages': 'dotnet add package NetApiFlow',
    'quickstart:program': 'using NetApiFlow;\n\nvar builder = WebApplication.CreateBuilder(args);\n\nvar app = builder.Build();\napp.Run();',
    'quickstart:registrations': 'builder.Services.AddNetApiFlow();',
    'mcp:package': 'dotnet add package NetApiFlow.Mcp',
    'mcp:usings': 'using NetApiFlow.Mcp;\nusing NetApiFlow.Mcp.Endpoints;',
    'mcp:registration': 'builder.Services.AddNetApiFlowMcp();\n\nvar app = builder.Build();\napp.UseNetApiFlowMcp(); // mounted at /flow/mcp by default',
    ...overrides,
  };
}

test('extractBlocks_ReadsTheMarkerFromTheOpeningFenceAndSkipsUnmarkedFences', () => {
  const text = `${FENCE}bash\nnpm ci\n${FENCE}\n\n${FENCE}csharp quickstart:program\nvar a = 1;\n${FENCE}\n`;

  const blocks = extractBlocks(text);

  assert.deepEqual([...blocks.keys()], ['quickstart:program']);
  assert.equal(blocks.get('quickstart:program').code, 'var a = 1;');
  assert.equal(blocks.get('quickstart:program').language, 'csharp');
});

test('extractBlocks_IgnoresAFenceInsideABlockquote', () => {
  const text = `> ${FENCE}csharp quickstart:program\n> var a = 1;\n> ${FENCE}\n`;

  assert.equal(extractBlocks(text).size, 0);
});

test('extractBlocks_WhenAMarkerAppearsTwice_Throws', () => {
  const text = `${FENCE}csharp mcp:registration\na\n${FENCE}\n${FENCE}csharp mcp:registration\nb\n${FENCE}\n`;

  assert.throws(() => extractBlocks(text), /"mcp:registration" appears twice/);
});

test('requireBlocks_WhenAMarkerIsGone_NamesItAndFails', () => {
  const blocks = extractBlocks(readme(allBlocks({})));
  blocks.delete('quickstart:registrations');

  assert.throws(() => requireBlocks(blocks, 'README.md'), /README\.md has no block marked "quickstart:registrations"/);
});

test('requireBlocks_WhenEveryMarkerIsThere_DoesNotThrow', () => {
  const blocks = extractBlocks(readme(allBlocks()));

  assert.doesNotThrow(() => requireBlocks(blocks, 'README.md'));
  assert.deepEqual([...blocks.keys()].sort(), [...REQUIRED_MARKERS].sort());
});

test('withoutTrailingComments_RemovesOnlyTheEndOfLineComment', () => {
  assert.equal(withoutTrailingComments('app.Use(); // mounted at /flow\nvar url = "a";'), 'app.Use();\nvar url = "a";');
});

test('findDivergences_WhenOnlyTheTrailingCommentDiffers_ReportsNothing', () => {
  const english = extractBlocks(readme(allBlocks()));
  const portuguese = extractBlocks(readme(allBlocks({ 'mcp:registration': allBlocks()['mcp:registration'].replace('mounted at', 'montado em') })));

  assert.deepEqual(findDivergences(english, portuguese), []);
});

test('findDivergences_WhenACodeLineDiffers_NamesTheBlockAndTheLine', () => {
  const english = extractBlocks(readme(allBlocks()));
  const portuguese = extractBlocks(readme(allBlocks({ 'quickstart:registrations': 'builder.Services.AddNetApiFlowDashboard();' })));

  const [message, ...rest] = findDivergences(english, portuguese);

  assert.equal(rest.length, 0);
  assert.match(message, /Block "quickstart:registrations" differs/);
  assert.match(message, /line 1/);
});

test('splitAtBuild_SplitsTheBlockOnTheBuildLine', () => {
  const { before, after } = splitAtBuild(allBlocks()['mcp:registration'], 'The MCP block');

  assert.equal(before, 'builder.Services.AddNetApiFlowMcp();');
  assert.equal(after, 'app.UseNetApiFlowMcp(); // mounted at /flow/mcp by default');
});

test('splitAtBuild_WhenTheBuildLineIsMissing_Throws', () => {
  assert.throws(() => splitAtBuild('builder.Services.AddNetApiFlowMcp();', 'The MCP block'), /exactly once/);
});

test('assembleProgram_PutsEachBlockWhereTheTextSaysAndInThatOrder', () => {
  const blocks = allBlocks();

  const program = assembleProgram({
    program: blocks['quickstart:program'],
    mcpUsings: blocks['mcp:usings'],
    registrations: blocks['quickstart:registrations'],
    mcpRegistration: blocks['mcp:registration'],
    beforeBuild: ['UserServices.Add(builder.Services);'],
    afterBuild: ['UserPipeline.Configure(app);'],
    beforeRun: ['UserEndpoints.Map(app);'],
  });

  const order = [
    'using NetApiFlow;',
    'using NetApiFlow.Mcp;',
    'using NetApiFlow.Mcp.Endpoints;',
    'WebApplication.CreateBuilder',
    'AddNetApiFlow()',
    'AddNetApiFlowMcp()',
    'UserServices.Add(builder.Services);',
    'var app = builder.Build();',
    'UserPipeline.Configure(app);',
    'app.UseNetApiFlowMcp();',
    'UserEndpoints.Map(app);',
    'app.Run();',
  ].map((fragment) => program.indexOf(fragment));

  assert.ok(order.every((position) => position >= 0), `a block is missing from:\n${program}`);
  assert.deepEqual(order, [...order].sort((left, right) => left - right));
  assert.equal(program.split('var app = builder.Build();').length, 2);
});

test('assembleProgram_WhenTheProgramHasNoRunLine_Throws', () => {
  const blocks = allBlocks();

  assert.throws(
    () =>
      assembleProgram({
        program: 'var builder = WebApplication.CreateBuilder(args);\nvar app = builder.Build();',
        mcpUsings: blocks['mcp:usings'],
        registrations: blocks['quickstart:registrations'],
        mcpRegistration: blocks['mcp:registration'],
        afterBuild: [],
        beforeRun: [],
      }),
    /"app.Run\(\);" exactly once/,
  );
});

test('withUsings_PutsTheNewLinesRightAfterTheUsingsThatOpenTheFile', () => {
  const program = withUsings('using A;\nusing B;\n\nvar builder = 1;\nusing NotAtTheTop;', 'using C;');

  assert.equal(program, 'using A;\nusing B;\nusing C;\n\nvar builder = 1;\nusing NotAtTheTop;');
});

test('packageCommands_ReturnsTheIdsOfTheAddPackageLines', () => {
  assert.deepEqual(packageCommands('dotnet add package NetApiFlow\ndotnet add package NetApiFlow.Dashboard\n'), ['NetApiFlow', 'NetApiFlow.Dashboard']);
});

test('packageCommands_WhenALineIsAnythingElse_Throws', () => {
  assert.throws(() => packageCommands('dotnet add package NetApiFlow --prerelease'), /Expected "dotnet add package <id>"/);
});
