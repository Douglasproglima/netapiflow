const OPENING_FENCE = /^```(.*)$/;
const CLOSING_FENCE = /^```\s*$/;
const MARKER = /^[a-z]+:[a-z-]+$/;
const TRAILING_LINE_COMMENT = /\s+\/\/.*$/;
const BUILD_LINE = 'var app = builder.Build();';
const RUN_LINE = 'app.Run();';

export const REQUIRED_MARKERS = [
  'quickstart:packages',
  'quickstart:program',
  'quickstart:registrations',
  'mcp:package',
  'mcp:usings',
  'mcp:registration',
];

export function extractBlocks(markdown) {
  const blocks = new Map();
  let open = null;

  markdown.split(/\r?\n/).forEach((line, index) => {
    if (open === null) {
      const opening = OPENING_FENCE.exec(line);
      if (opening === null) {
        return;
      }

      const [language = '', marker = ''] = opening[1].trim().split(/\s+/);
      open = { language, marker, line: index + 1, lines: [] };
      return;
    }

    if (CLOSING_FENCE.test(line)) {
      if (MARKER.test(open.marker)) {
        if (blocks.has(open.marker)) {
          throw new Error(`The block marker "${open.marker}" appears twice (line ${blocks.get(open.marker).line} and line ${open.line}).`);
        }

        blocks.set(open.marker, { language: open.language, line: open.line, code: open.lines.join('\n') });
      }

      open = null;
      return;
    }

    open.lines.push(line);
  });

  return blocks;
}

export function requireBlocks(blocks, label) {
  const missing = REQUIRED_MARKERS.filter((marker) => !blocks.has(marker));
  if (missing.length > 0) {
    throw new Error(
      `${label} has no block marked ${missing.map((marker) => `"${marker}"`).join(', ')}. `
        + 'Mark the opening fence of each block, for example ```csharp quickstart:program, so a README edit cannot switch this check off.',
    );
  }
}

export function withoutTrailingComments(code) {
  return code
    .split('\n')
    .map((line) => line.replace(TRAILING_LINE_COMMENT, '').trimEnd())
    .join('\n');
}

export function findDivergences(english, portuguese) {
  return REQUIRED_MARKERS.flatMap((marker) => {
    const englishLines = withoutTrailingComments(english.get(marker).code).split('\n');
    const portugueseLines = withoutTrailingComments(portuguese.get(marker).code).split('\n');
    const length = Math.max(englishLines.length, portugueseLines.length);

    for (let index = 0; index < length; index++) {
      if (englishLines[index] !== portugueseLines[index]) {
        return [
          `Block "${marker}" differs between README.md and README.pt-BR.md at its line ${index + 1}: `
            + `"${englishLines[index] ?? '(missing)'}" against "${portugueseLines[index] ?? '(missing)'}".`,
        ];
      }
    }

    return [];
  });
}

export function splitAtBuild(code, label) {
  const lines = code.split('\n');
  const position = lines.findIndex((line) => line.trim() === BUILD_LINE);
  if (position < 0 || lines.slice(position + 1).some((line) => line.trim() === BUILD_LINE)) {
    throw new Error(`${label} must contain "${BUILD_LINE}" exactly once, because the MCP block is split on that line.`);
  }

  return {
    before: lines.slice(0, position).join('\n').trim(),
    after: lines.slice(position + 1).join('\n').trim(),
  };
}

export function assembleProgram({ program, mcpUsings, registrations, mcpRegistration, afterBuild, beforeRun }) {
  const lines = withUsings(program, mcpUsings).split('\n');
  const buildPosition = onlyPosition(lines, BUILD_LINE, 'the Quick start program');
  const runPosition = onlyPosition(lines, RUN_LINE, 'the Quick start program');
  const mcp = splitAtBuild(mcpRegistration, 'The MCP registration block');

  return [
    ...lines.slice(0, buildPosition),
    registrations.trim(),
    '',
    mcp.before,
    '',
    lines[buildPosition],
    ...afterBuild,
    mcp.after,
    '',
    ...lines.slice(buildPosition + 1, runPosition),
    ...beforeRun,
    '',
    lines[runPosition],
    ...lines.slice(runPosition + 1),
  ].join('\n');
}

export function withUsings(program, usings) {
  const lines = program.split('\n');
  let afterLastUsing = 0;
  while (afterLastUsing < lines.length && lines[afterLastUsing].startsWith('using ')) {
    afterLastUsing++;
  }

  return [...lines.slice(0, afterLastUsing), ...usings.trim().split('\n'), ...lines.slice(afterLastUsing)].join('\n');
}

export function packageCommands(blockCode) {
  return blockCode
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map((line) => {
      const match = /^dotnet add package (\S+)$/.exec(line);
      if (match === null) {
        throw new Error(`Expected "dotnet add package <id>" in a package block, found "${line}".`);
      }

      return match[1];
    });
}

function onlyPosition(lines, wanted, label) {
  const positions = lines.flatMap((line, index) => (line.trim() === wanted ? [index] : []));
  if (positions.length !== 1) {
    throw new Error(`Expected "${wanted}" exactly once in ${label}, found it ${positions.length} times.`);
  }

  return positions[0];
}
