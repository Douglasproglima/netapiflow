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
