import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ApiService } from './api.service';
import {
  ArchitectureMap,
  ComponentStat,
  DeployAnnotation,
  FlowTraceDetail,
  FlowTraceSummary,
  MetricsSummary,
  PagedResult,
  RouteStat,
  SpanDetail,
} from './models';

describe('ApiService', () => {
  let service: ApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('getTraces sends only the filters that were provided, as relative URL', () => {
    const expected: PagedResult<FlowTraceSummary> = { items: [], totalCount: 0 };

    service.getTraces({ status: 'Error', page: 2 }).subscribe((result) => {
      expect(result).toEqual(expected);
    });

    const req = httpMock.expectOne(
      (request) =>
        request.url === 'api/traces' &&
        request.params.get('status') === 'Error' &&
        request.params.get('page') === '2',
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.params.has('method')).toBe(false);
    req.flush(expected);
  });

  it('getLiveTraces hits /traces/live', () => {
    service.getLiveTraces().subscribe();
    const req = httpMock.expectOne('api/traces/live');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('getTrace encodes the traceId into the path', () => {
    const detail: FlowTraceDetail = {
      traceId: 'a b',
      method: 'GET',
      routeTemplate: '/api/orders/{id}',
      httpStatus: 200,
      status: 'Success',
      startedAt: '2026-01-01T00:00:00.000000Z',
      endedAt: '2026-01-01T00:00:00.000100Z',
      durationMicros: 100,
      spanCount: 0,
      truncated: false,
      errorMessage: null,
      tags: {},
      spans: [],
    };

    service.getTrace('a b').subscribe((result) => expect(result).toEqual(detail));

    const req = httpMock.expectOne('api/traces/a%20b');
    req.flush(detail);
  });

  it('getSpan hits /traces/{traceId}/spans/{spanId}', () => {
    const detail: SpanDetail = {
      span: {
        spanId: 's1',
        parentSpanId: null,
        sequence: 0,
        component: 'C',
        operation: 'Op',
        type: 'Service',
        startedAt: '2026-01-01T00:00:00.000000Z',
        endedAt: null,
        durationMicros: null,
        status: 'Running',
        errorType: null,
        errorMessage: null,
        isErrorOrigin: false,
        tags: {},
        logs: [],
      },
      ancestors: [],
      children: [],
      medianDurationMicros: null,
      deltaFromMedianMicros: null,
    };

    service.getSpan('t1', 's1').subscribe((result) => expect(result).toEqual(detail));

    const req = httpMock.expectOne('api/traces/t1/spans/s1');
    req.flush(detail);
  });

  it('getBlueprint sends method and route as query params and surfaces a 204 as null', () => {
    service.getBlueprint('GET', '/api/orders/{id}').subscribe((result) => {
      expect(result).toBeNull();
    });

    const req = httpMock.expectOne(
      (request) =>
        request.url === 'api/blueprint' &&
        request.params.get('method') === 'GET' &&
        request.params.get('route') === '/api/orders/{id}',
    );
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('getEndpoints hits /endpoints', () => {
    service.getEndpoints().subscribe();
    const req = httpMock.expectOne('api/endpoints');
    req.flush([]);
  });

  it('getMetrics omits from/to when not provided', () => {
    const summary: MetricsSummary = {
      totalTraces: 0,
      errorCount: 0,
      errorRate: 0,
      averageDurationMicros: 0,
      p95DurationMicros: 0,
      maxDurationMicros: 0,
    };

    service.getMetrics({}).subscribe((result) => expect(result).toEqual(summary));

    const req = httpMock.expectOne((request) => request.url === 'api/metrics');
    expect(req.request.params.has('from')).toBe(false);
    expect(req.request.params.has('to')).toBe(false);
    req.flush(summary);
  });

  it('getComponentMetrics hits /metrics/components', () => {
    const stats: ComponentStat[] = [];
    service
      .getComponentMetrics({ from: '2026-01-01T00:00:00Z' })
      .subscribe((result) => expect(result).toEqual(stats));

    const req = httpMock.expectOne(
      (request) =>
        request.url === 'api/metrics/components' &&
        request.params.get('from') === '2026-01-01T00:00:00Z',
    );
    req.flush(stats);
  });

  it('getRouteMetrics hits /metrics/routes', () => {
    const stats: RouteStat[] = [];
    service
      .getRouteMetrics({ from: '2026-01-01T00:00:00Z' })
      .subscribe((result) => expect(result).toEqual(stats));

    const req = httpMock.expectOne(
      (request) =>
        request.url === 'api/metrics/routes' &&
        request.params.get('from') === '2026-01-01T00:00:00Z',
    );
    req.flush(stats);
  });

  it('getArchitecture sends method, route and the stats query as params', () => {
    const map: ArchitectureMap = { totalExecutions: 0, nodes: [], edges: [] };
    service
      .getArchitecture('GET', '/api/orders/{id}', { from: '2026-01-01T00:00:00Z' })
      .subscribe((result) => expect(result).toEqual(map));

    const req = httpMock.expectOne(
      (request) =>
        request.url === 'api/architecture' &&
        request.params.get('method') === 'GET' &&
        request.params.get('route') === '/api/orders/{id}' &&
        request.params.get('from') === '2026-01-01T00:00:00Z',
    );
    req.flush(map);
  });

  it('deleteTraces issues a DELETE to /traces', () => {
    service.deleteTraces().subscribe();
    const req = httpMock.expectOne('api/traces');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('getConfig hits /config', () => {
    service
      .getConfig()
      .subscribe((result) =>
        expect(result).toEqual({ slowThresholdMicros: 250_000, version: '0.1.0' }),
      );

    const req = httpMock.expectOne('api/config');
    req.flush({ slowThresholdMicros: 250_000, version: '0.1.0' });
  });

  it('getDeploys sends from and to as query params', () => {
    const expected: DeployAnnotation[] = [
      { id: 'd1', timestamp: '2026-11-13T12:00:00.000000Z', label: 'v1.4.2' },
    ];

    service.getDeploys('2026-11-01T00:00:00Z', '2026-11-14T00:00:00Z').subscribe((result) => {
      expect(result).toEqual(expected);
    });

    const req = httpMock.expectOne(
      (request) =>
        request.url === 'api/deploys' &&
        request.params.get('from') === '2026-11-01T00:00:00Z' &&
        request.params.get('to') === '2026-11-14T00:00:00Z',
    );
    expect(req.request.method).toBe('GET');
    req.flush(expected);
  });

  it('createDeploy posts the label and returns the saved annotation', () => {
    const expected: DeployAnnotation = {
      id: 'd1',
      timestamp: '2026-11-13T12:00:00.000000Z',
      label: 'v1.4.2',
    };

    service.createDeploy('v1.4.2').subscribe((result) => {
      expect(result).toEqual(expected);
    });

    const req = httpMock.expectOne('api/deploys');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ label: 'v1.4.2' });
    req.flush(expected);
  });

  it('callEndpoint sends a root-relative request, not prefixed by the dashboard base path', () => {
    service
      .callEndpoint('POST', '/api/orders/5', { customerName: 'Acme' })
      .subscribe((response) => expect(response.headers.get('X-NetApiFlow-TraceId')).toBe('t1'));

    const req = httpMock.expectOne('/api/orders/5');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ customerName: 'Acme' });
    req.flush(null, { headers: { 'X-NetApiFlow-TraceId': 't1' } });
  });
});
