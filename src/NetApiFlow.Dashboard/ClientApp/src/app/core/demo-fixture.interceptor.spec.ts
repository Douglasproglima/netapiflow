import {
  HttpClient,
  HttpErrorResponse,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
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
    'api/blueprint?before=2026-01-01T00:00:00.000000Z&method=GET&route=/api/orders/{id}':
      'traces_t1.json',
  },
  noContent: ['api/blueprint?before=2025-01-01T00:00:00.000000Z&method=GET&route=/api/orders/{id}'],
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

    httpMock.expectNone('api/config');
    httpMock
      .expectOne('assets/demo-fixtures/config.json')
      .flush({ slowThresholdMicros: 250_000, version: '0.4.1' });

    expect((await result).version).toBe('0.4.1');
  });

  it('answers a blueprint request with the recording made for the start of that trace', async () => {
    const result = firstValue(
      api.getBlueprint('GET', '/api/orders/{id}', '2026-01-01T00:00:00.000000Z'),
    );
    flushManifest();

    httpMock.expectNone('api/blueprint');
    httpMock.expectOne('assets/demo-fixtures/traces_t1.json').flush({ traceId: 't1', spans: [] });

    expect((await result)?.traceId).toBe('t1');
  });

  it('answers 204 with no body for a trace that has no earlier execution', async () => {
    const http = TestBed.inject(HttpClient);
    const result = firstValue(
      http.get('api/blueprint', {
        observe: 'response',
        params: {
          method: 'GET',
          route: '/api/orders/{id}',
          before: '2025-01-01T00:00:00.000000Z',
        },
      }),
    );
    flushManifest();

    httpMock.expectNone((request) => request.url.startsWith('assets/demo-fixtures/traces'));
    const response = await result;
    expect(response.status).toBe(204);
    expect(response.body).toBeNull();
  });

  it('still answers 404 for a blueprint of a trace the manifest does not know', async () => {
    const result = firstValueFrom(
      api.getBlueprint('GET', '/api/orders/{id}', '2030-01-01T00:00:00.000000Z'),
    ).catch((error: HttpErrorResponse) => error.status);
    flushManifest();

    expect(await result).toBe(404);
  });

  it('says on the console which request it has no recording for when it answers 404', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const result = firstValueFrom(
      api.getBlueprint('GET', '/api/orders/{id}', '2030-01-01T00:00:00.000000Z'),
    ).catch((error: HttpErrorResponse) => error.status);
    flushManifest();

    await result;

    expect(consoleError).toHaveBeenCalledTimes(1);
    expect(consoleError.mock.calls[0][0]).toContain('api/blueprint');
    expect(consoleError.mock.calls[0][0]).toContain('before=2030-01-01T00:00:00.000000Z');
    consoleError.mockRestore();
  });

  it('does not touch the console when it has a recording', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const result = firstValueFrom(
      api.getBlueprint('GET', '/api/orders/{id}', '2026-01-01T00:00:00.000000Z'),
    );
    flushManifest();
    httpMock.expectOne('assets/demo-fixtures/traces_t1.json').flush({});

    await result;

    expect(consoleError).not.toHaveBeenCalled();
    consoleError.mockRestore();
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

  describe('a route of the sample application, sent by the Runner', () => {
    const MANIFEST_WITH_RECORDINGS = {
      routes: {},
      sampleResponses: [
        {
          method: 'GET',
          template: '/api/orders/{id}',
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: 'sample_order.json',
          traceId: 'trace-order',
        },
        {
          method: 'GET',
          template: '/api/orders/parallel',
          status: 200,
          contentType: 'application/json; charset=utf-8',
          body: 'sample_parallel.json',
          traceId: 'trace-parallel',
        },
        {
          method: 'GET',
          template: '/api/orders/{id}/shipping-status',
          status: 200,
          contentType: 'text/plain; charset=utf-8',
          body: 'sample_shipping.txt',
          traceId: 'trace-shipping',
        },
      ],
    };

    function sendFromRunner(method: string, url: string) {
      return firstValue(TestBed.inject(HttpClient).request(method, url, { observe: 'response' }));
    }

    it('is answered by its recording, with the trace id header the Runner follows', async () => {
      const result = sendFromRunner('GET', '/api/orders/1');
      httpMock.expectOne(DEMO_MANIFEST_PATH).flush(MANIFEST_WITH_RECORDINGS);

      httpMock.expectNone('/api/orders/1');
      httpMock.expectOne('assets/demo-fixtures/sample_order.json').flush({ id: 1 });

      const response = await result;
      expect(response.status).toBe(200);
      expect(response.headers.get('X-NetApiFlow-TraceId')).toBe('trace-order');
      expect(response.body).toEqual({ id: 1 });
    });

    it.each(['/api/orders/1', '/api/orders/999', '/api/orders/not-a-number', '/api/orders/7?x=1'])(
      'is found by method and template, not by the value sent: %s',
      async (url) => {
        const result = sendFromRunner('GET', url);
        httpMock.expectOne(DEMO_MANIFEST_PATH).flush(MANIFEST_WITH_RECORDINGS);
        httpMock.expectOne('assets/demo-fixtures/sample_order.json').flush({ id: 1 });

        expect((await result).headers.get('X-NetApiFlow-TraceId')).toBe('trace-order');
      },
    );

    it('prefers the template with more literal segments over one with a parameter', async () => {
      const result = sendFromRunner('GET', '/api/orders/parallel');
      httpMock.expectOne(DEMO_MANIFEST_PATH).flush(MANIFEST_WITH_RECORDINGS);
      httpMock.expectOne('assets/demo-fixtures/sample_parallel.json').flush([]);

      expect((await result).headers.get('X-NetApiFlow-TraceId')).toBe('trace-parallel');
    });

    it('hands back a plain text recording as text', async () => {
      const result = sendFromRunner('GET', '/api/orders/1/shipping-status');
      httpMock.expectOne(DEMO_MANIFEST_PATH).flush(MANIFEST_WITH_RECORDINGS);
      const bodyRead = httpMock.expectOne('assets/demo-fixtures/sample_shipping.txt');
      expect(bodyRead.request.responseType).toBe('text');
      bodyRead.flush('Keep it logically awesome.');

      const response = await result;
      expect(response.body).toBe('Keep it logically awesome.');
      expect(response.headers.get('Content-Type')).toContain('text/plain');
    });

    it('goes to the network when no recording matches, instead of being taken for a dashboard call', () => {
      let failure: unknown;
      TestBed.inject(HttpClient)
        .request('POST', '/api/orders', { observe: 'response' })
        .subscribe({ error: (error: unknown) => (failure = error) });
      httpMock.expectOne(DEMO_MANIFEST_PATH).flush(MANIFEST_WITH_RECORDINGS);

      httpMock.expectOne('/api/orders').flush({}, { status: 404, statusText: 'Not Found' });

      expect(failure).toBeDefined();
    });
  });
});

function firstValue<T>(source: {
  subscribe: (observer: (value: T) => void) => unknown;
}): Promise<T> {
  return new Promise<T>((resolve) => source.subscribe(resolve));
}
