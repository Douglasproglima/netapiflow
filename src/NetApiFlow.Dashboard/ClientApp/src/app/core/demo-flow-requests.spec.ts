/// <reference types="vite/client" />
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import manifestJson from '../../assets/demo-fixtures/manifest.json';
import { ApiService } from './api.service';
import {
  DEMO_FIXTURES_PATH,
  DEMO_MANIFEST_PATH,
  DemoManifest,
  demoFixtureInterceptor,
  resetDemoManifestCache,
} from './demo-fixture.interceptor';
import { FlowTraceDetail } from './models';

const manifest = manifestJson as unknown as DemoManifest;

const fixtureBodies = import.meta.glob('../../assets/demo-fixtures/*.json', {
  eager: true,
  import: 'default',
}) as Record<string, object | null>;

const recordedTraces: FlowTraceDetail[] = Object.entries(manifest.routes)
  .filter(([key]) => /^api\/traces\/[0-9a-f]{32}$/.test(key))
  .map(([, file]) => fixtureBodies[`../../assets/demo-fixtures/${file}`] as FlowTraceDetail);

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

function lastSuccessBefore(trace: FlowTraceDetail): FlowTraceDetail | undefined {
  return recordedTraces
    .filter(
      (candidate) =>
        candidate.status === 'Success' &&
        candidate.method.toUpperCase() === trace.method.toUpperCase() &&
        routeKey(candidate.routeTemplate) === routeKey(trace.routeTemplate) &&
        startedAtMicros(candidate.startedAt) < startedAtMicros(trace.startedAt),
    )
    .sort((left, right) => startedAtMicros(right.startedAt) - startedAtMicros(left.startedAt))
    .at(0);
}

describe('the recorded demo, as the flow page uses it', () => {
  let httpMock: HttpTestingController;
  let api: ApiService;

  beforeEach(() => {
    resetDemoManifestCache();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([demoFixtureInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    api = TestBed.inject(ApiService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function serveRecordedFixtures(): void {
    for (const request of httpMock.match((candidate) => candidate.url === DEMO_MANIFEST_PATH)) {
      request.flush(manifest);
    }

    for (const request of httpMock.match((candidate) =>
      candidate.url.startsWith(DEMO_FIXTURES_PATH),
    )) {
      const file = request.request.url.slice(DEMO_FIXTURES_PATH.length + 1);
      request.flush(fixtureBodies[`../../assets/demo-fixtures/${file}`]);
    }
  }

  it('records traces for the flow page to open', () => {
    expect(recordedTraces.length).toBeGreaterThan(0);
  });

  it.each(recordedTraces.map((trace) => ({ traceId: trace.traceId, trace })))(
    'answers every request the flow page makes to draw the trace $traceId',
    async ({ trace }) => {
      const opened = firstValueFrom(api.getTrace(trace.traceId));
      serveRecordedFixtures();
      const blueprint = firstValueFrom(
        api.getBlueprint(trace.method, trace.routeTemplate, trace.startedAt),
      );
      serveRecordedFixtures();

      expect((await opened).traceId).toBe(trace.traceId);
      expect((await blueprint)?.traceId ?? null).toBe(lastSuccessBefore(trace)?.traceId ?? null);
    },
  );

  it('answers a blueprint with the last success before the trace, and with nothing for the first one', async () => {
    const withEarlierExecution = recordedTraces.find((trace) => lastSuccessBefore(trace));
    const firstOfItsRoute = recordedTraces.find((trace) => !lastSuccessBefore(trace));
    expect(withEarlierExecution).toBeDefined();
    expect(firstOfItsRoute).toBeDefined();

    const earlier = firstValueFrom(
      api.getBlueprint(
        withEarlierExecution!.method,
        withEarlierExecution!.routeTemplate,
        withEarlierExecution!.startedAt,
      ),
    );
    serveRecordedFixtures();
    const none = firstValueFrom(
      api.getBlueprint(
        firstOfItsRoute!.method,
        firstOfItsRoute!.routeTemplate,
        firstOfItsRoute!.startedAt,
      ),
    );
    serveRecordedFixtures();

    expect(startedAtMicros((await earlier)!.startedAt)).toBeLessThan(
      startedAtMicros(withEarlierExecution!.startedAt),
    );
    expect(await none).toBeNull();
  });
});
