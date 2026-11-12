import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { DiffPageComponent } from './diff-page.component';
import { FlowTraceDetail } from '../../../core/models';

function makeTrace(traceId: string, overrides: Partial<FlowTraceDetail> = {}): FlowTraceDetail {
  return {
    traceId,
    method: 'GET',
    routeTemplate: '/api/orders/{id}',
    httpStatus: 200,
    status: 'Success',
    startedAt: '2026-01-01T00:00:00.000000Z',
    endedAt: '2026-01-01T00:00:00.000100Z',
    durationMicros: 100,
    spanCount: 1,
    truncated: false,
    errorMessage: null,
    tags: {},
    spans: [
      {
        spanId: 's1',
        parentSpanId: null,
        sequence: 0,
        component: 'OrderController',
        operation: 'Get',
        type: 'Controller',
        startedAt: '2026-01-01T00:00:00.000000Z',
        endedAt: '2026-01-01T00:00:00.000100Z',
        durationMicros: 100,
        status: 'Success',
        errorType: null,
        errorMessage: null,
        isErrorOrigin: false,
        tags: {},
        logs: [],
      },
    ],
    ...overrides,
  };
}

describe('DiffPageComponent', () => {
  let httpMock: HttpTestingController;

  function configure(a: string | null, b: string | null): void {
    const params = { ...(a ? { a } : {}), ...(b ? { b } : {}) };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            queryParamMap: of(convertToParamMap(params)),
            snapshot: { queryParamMap: convertToParamMap(params) },
          },
        },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => {
    httpMock.verify();
  });

  function flushConfig(slowThresholdMicros = 250_000): void {
    httpMock.expectOne('api/config').flush({ slowThresholdMicros, version: '0.1.0' });
  }

  it('shows missing-params without calling the API when a or b is absent', async () => {
    configure('t1', null);
    const fixture = TestBed.createComponent(DiffPageComponent);
    await fixture.whenStable();
    flushConfig();
    await fixture.whenStable();

    expect(fixture.componentInstance.viewState()).toBe('missing-params');
  });

  it('fetches both traces independently and builds the diff rows', async () => {
    configure('t1', 't2');
    const fixture = TestBed.createComponent(DiffPageComponent);
    await fixture.whenStable();

    flushConfig();
    httpMock.expectOne('api/traces/t1').flush(makeTrace('t1'));
    httpMock.expectOne('api/traces/t2').flush(makeTrace('t2'));
    await fixture.whenStable();

    expect(fixture.componentInstance.viewState()).toBe('data');
    expect(fixture.componentInstance.diffRows()).toHaveLength(1);
    expect(fixture.componentInstance.diffRows()[0].kind).toBe('matched');
    expect(fixture.componentInstance.routeMismatch()).toBe(false);
  });

  it('flags a route mismatch between the two executions without blocking the comparison', async () => {
    configure('t1', 't2');
    const fixture = TestBed.createComponent(DiffPageComponent);
    await fixture.whenStable();

    flushConfig();
    httpMock.expectOne('api/traces/t1').flush(makeTrace('t1'));
    httpMock
      .expectOne('api/traces/t2')
      .flush(makeTrace('t2', { method: 'POST', routeTemplate: '/api/orders' }));
    await fixture.whenStable();

    expect(fixture.componentInstance.viewState()).toBe('data');
    expect(fixture.componentInstance.routeMismatch()).toBe(true);
  });

  it('shows an error when one of the two traces fails to load', async () => {
    configure('t1', 'missing');
    const fixture = TestBed.createComponent(DiffPageComponent);
    await fixture.whenStable();

    flushConfig();
    httpMock.expectOne('api/traces/t1').flush(makeTrace('t1'));
    httpMock
      .expectOne('api/traces/missing')
      .flush('not found', { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(fixture.componentInstance.viewState()).toBe('error');
  });
});
