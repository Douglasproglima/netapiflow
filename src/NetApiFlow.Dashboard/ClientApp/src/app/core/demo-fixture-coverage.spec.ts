/// <reference types="vite/client" />
import manifestJson from '../../assets/demo-fixtures/manifest.json';
import documentJson from '../../assets/demo-fixtures/openapi-document.json';
import { DemoManifest, findSampleResponse } from './demo-fixture.interceptor';

const manifest = manifestJson as unknown as DemoManifest;

const fixtureFiles = new Set(
  Object.keys(import.meta.glob('../../assets/demo-fixtures/*')).map((path) =>
    path.split('/').pop(),
  ),
);

const fixtureContents = import.meta.glob('../../assets/demo-fixtures/*.json', {
  eager: true,
  import: 'default',
}) as Record<string, unknown>;

interface RecordedTrace {
  file: string;
  method: string;
  routeTemplate: string;
  status: string;
  startedAt: string;
}

const TRACE_ROUTE = /^api\/traces\/[0-9a-f]{32}$/;

const recordedTraces: RecordedTrace[] = Object.entries(manifest.routes)
  .filter(([key]) => TRACE_ROUTE.test(key))
  .map(([, file]) => ({
    ...(fixtureContents[`../../assets/demo-fixtures/${file}`] as Omit<RecordedTrace, 'file'>),
    file,
  }));

const blueprintKeys = Object.keys(manifest.routes).filter((key) => key.startsWith('api/blueprint'));

function routeKey(routeTemplate: string): string {
  const withLeadingSlash = routeTemplate.startsWith('/') ? routeTemplate : `/${routeTemplate}`;
  const withoutTrailingSlash =
    withLeadingSlash.length > 1 && withLeadingSlash.endsWith('/')
      ? withLeadingSlash.slice(0, -1)
      : withLeadingSlash;
  return withoutTrailingSlash.replace(/\{\*?([^{}:?]*)[^{}]*\}/g, '{$1}');
}

function startedAtMicros(startedAt: string): number {
  const [seconds, fraction = ''] = startedAt.replace('Z', '').split('.');
  return Date.parse(`${seconds}Z`) * 1000 + Number(fraction.padEnd(6, '0').slice(0, 6));
}

function lastSuccessBefore(
  method: string,
  route: string,
  before: string,
): RecordedTrace | undefined {
  return recordedTraces
    .filter(
      (candidate) =>
        candidate.status === 'Success' &&
        candidate.method.toUpperCase() === method.toUpperCase() &&
        routeKey(candidate.routeTemplate) === routeKey(route) &&
        startedAtMicros(candidate.startedAt) < startedAtMicros(before),
    )
    .sort((left, right) => startedAtMicros(right.startedAt) - startedAtMicros(left.startedAt))
    .at(0);
}

function blueprintKeyOf(trace: RecordedTrace): string {
  return `api/blueprint?before=${trace.startedAt}&method=${trace.method}&route=${trace.routeTemplate}`;
}

const operations = Object.entries(documentJson.paths).flatMap(([template, methods]) =>
  Object.keys(methods).map((method) => ({ method: method.toUpperCase(), template })),
);

describe('the recorded demo', () => {
  it('records an OpenAPI document that has operations to send', () => {
    expect(operations.length).toBeGreaterThan(0);
  });

  it('lists no route of the dashboard itself in the OpenAPI document', () => {
    expect(operations.filter((operation) => operation.template.startsWith('/flow'))).toEqual([]);
  });

  it.each(operations)(
    'answers $method $template with a recording that points at a trace the fixtures hold',
    ({ method, template }) => {
      const exampleUrl = template.replace(/\{[^}]+\}/g, '42');

      const recording = findSampleResponse(manifest, method, exampleUrl);

      expect(recording).toBeDefined();
      expect(recording?.traceId).toMatch(/^[0-9a-f]{32}$/);
      const traceFixture = manifest.routes[`api/traces/${recording?.traceId}`];
      expect(traceFixture).toBeDefined();
      expect(fixtureFiles.has(traceFixture)).toBe(true);
      if (recording?.body) {
        expect(fixtureFiles.has(recording.body)).toBe(true);
      }
    },
  );
});

describe('the manifest of the recorded demo', () => {
  it('lists the traces it answers', () => {
    expect(recordedTraces.length).toBeGreaterThan(0);
  });

  it('cites only files the fixtures hold', () => {
    const cited = [
      ...Object.values(manifest.routes),
      ...Object.values(manifest.fallbacks ?? {}),
      ...Object.values(manifest.documents ?? {}),
      ...(manifest.sampleResponses ?? []).flatMap((recording) =>
        recording.body ? [recording.body] : [],
      ),
    ];

    expect(cited.filter((file) => !fixtureFiles.has(file))).toEqual([]);
  });
});

describe('the blueprints of the recorded demo', () => {
  it.each(recordedTraces.map(blueprintKeyOf))(
    'answers %s, with a blueprint or with no content',
    (key) => {
      expect(key in manifest.routes || (manifest.noContent ?? []).includes(key)).toBe(true);
    },
  );

  it.each(blueprintKeys)(
    'points %s at the last success of the same route that started strictly before it',
    (key) => {
      const query = new URLSearchParams(key.slice(key.indexOf('?') + 1));
      const before = query.get('before');
      const method = query.get('method');
      const route = query.get('route');

      expect(before).not.toBeNull();
      expect(method).not.toBeNull();
      expect(route).not.toBeNull();
      expect(manifest.routes[key]).toBe(
        lastSuccessBefore(method as string, route as string, before as string)?.file,
      );
    },
  );

  it.each(manifest.noContent ?? [])(
    'has no earlier success of the same route for %s, which answers with no content',
    (key) => {
      const query = new URLSearchParams(key.slice(key.indexOf('?') + 1));

      expect(key in manifest.routes).toBe(false);
      expect(
        lastSuccessBefore(
          query.get('method') as string,
          query.get('route') as string,
          query.get('before') as string,
        ),
      ).toBeUndefined();
    },
  );
});
