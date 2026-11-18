import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiService } from './api.service';
import {
  DEMO_MANIFEST_PATH,
  demoFixtureInterceptor,
  resetDemoManifestCache,
} from './demo-fixture.interceptor';

const MANIFEST = {
  routes: {
    'api/config': 'config.json',
    'api/traces': 'traces.json',
    'api/traces/live': 'traces_live.json',
    'api/metrics': 'metrics.json',
    'api/metrics/components': 'metrics_components.json',
    'api/metrics/routes': 'metrics_routes.json',
    'api/endpoints': 'endpoints.json',
    'api/traces/t1': 'traces_t1.json',
    'api/architecture?method=GET&route=/api/orders/{id}': 'architecture_get.json',
  },
  fallbacks: {
    'api/traces': 'traces_t1.json',
  },
  documents: {
    '/openapi/v1.json': 'openapi-document.json',
  },
};

describe('demoFixtureInterceptor', () => {
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

  function flushManifest(): void {
    httpMock.expectOne(DEMO_MANIFEST_PATH).flush(MANIFEST);
  }

  it('answers a dashboard API call from its fixture file, never from the network', async () => {
    const result = firstValue(api.getConfig());
    flushManifest();

    // The only request that leaves is the fixture read: the API URL itself is never requested.
    httpMock.expectNone('api/config');
    httpMock
      .expectOne('assets/demo-fixtures/config.json')
      .flush({ slowThresholdMicros: 250_000, version: '0.4.1' });

    expect((await result).version).toBe('0.4.1');
  });

  it('serves every route the dashboard calls on load', async () => {
    const calls: [string, Promise<unknown>][] = [
      ['traces.json', firstValue(api.getTraces({}))],
      ['traces_live.json', firstValue(api.getLiveTraces())],
      ['metrics.json', firstValue(api.getMetrics({}))],
      ['metrics_components.json', firstValue(api.getComponentMetrics({}))],
      ['metrics_routes.json', firstValue(api.getRouteMetrics({}))],
      ['endpoints.json', firstValue(api.getEndpoints())],
    ];

    flushManifest();

    for (const [file] of calls) {
      httpMock.expectOne(`assets/demo-fixtures/${file}`).flush([]);
    }

    await Promise.all(calls.map(([, pending]) => pending));
    httpMock.expectNone((request) => request.url.startsWith('api/'));
  });

  it('matches a fixture recorded with its query string', async () => {
    const result = firstValue(api.getArchitecture('GET', '/api/orders/{id}', {}));
    flushManifest();

    httpMock
      .expectOne('assets/demo-fixtures/architecture_get.json')
      .flush({ nodes: [], edges: [], totalExecutions: 0 });

    expect(await result).toBeTruthy();
  });

  it('falls back to a recorded trace for an id the recording never captured', async () => {
    const result = firstValue(api.getTrace('never-recorded'));
    flushManifest();

    httpMock.expectOne('assets/demo-fixtures/traces_t1.json').flush({ traceId: 't1', spans: [] });

    expect((await result).traceId).toBe('t1');
  });

  it("serves the host's OpenAPI document, which the Runner reads outside the dashboard API", async () => {
    const result = firstValue(TestBed.inject(HttpClient).get('/openapi/v1.json'));
    flushManifest();

    httpMock.expectNone('/openapi/v1.json');
    httpMock.expectOne('assets/demo-fixtures/openapi-document.json').flush({ paths: {} });

    expect(await result).toBeTruthy();
  });

  it('lets a request with no recorded answer reach the server, when it is not an API call', async () => {
    const result = firstValue(TestBed.inject(HttpClient).get('/icons/database.svg'));
    flushManifest();

    httpMock.expectOne('/icons/database.svg').flush('<svg />');

    expect(await result).toBeTruthy();
  });
});

function firstValue<T>(source: {
  subscribe: (observer: (value: T) => void) => unknown;
}): Promise<T> {
  return new Promise<T>((resolve) => source.subscribe(resolve));
}
