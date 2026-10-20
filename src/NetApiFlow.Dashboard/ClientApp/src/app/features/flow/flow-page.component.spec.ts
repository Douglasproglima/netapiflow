import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { FlowPageComponent } from './flow-page.component';
import { SseService, SseConnectionState } from '../../core/sse.service';
import { FlowTraceDetail, FlowTraceSummary } from '../../core/models';

const fakeSse = {
  events$: new Subject().asObservable(),
  reconnected$: new Subject<void>().asObservable(),
  state: () => 'open' as SseConnectionState,
  connect: vi.fn(),
  disconnect: vi.fn(),
};

function makeTrace(traceId: string): FlowTraceDetail {
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
      },
    ],
  };
}

describe('FlowPageComponent', () => {
  let httpMock: HttpTestingController;

  function configure(traceId: string | null): void {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: SseService, useValue: fakeSse },
        {
          provide: ActivatedRoute,
          useValue: {
            queryParamMap: of(convertToParamMap(traceId ? { trace: traceId } : {})),
            snapshot: { queryParamMap: convertToParamMap(traceId ? { trace: traceId } : {}) },
          },
        },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => {
    httpMock.verify();
  });

  it('loads the trace from the ?trace= query param on mount', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    httpMock.expectOne('api/traces/t1').flush(makeTrace('t1'));
    await fixture.whenStable();

    expect(fixture.componentInstance.trace()?.traceId).toBe('t1');
    expect(fixture.componentInstance.graph().nodes).toHaveLength(1);
  });

  it('lists in-progress traces when no ?trace= param is present', async () => {
    configure(null);
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    const summaries: FlowTraceSummary[] = [
      {
        traceId: 't2',
        method: 'GET',
        routeTemplate: '/api/orders',
        httpStatus: null,
        status: 'Running',
        startedAt: '2026-01-01T00:00:00.000000Z',
        durationMicros: null,
        truncated: false,
      },
    ];
    httpMock.expectOne('api/traces/live').flush(summaries);
    await fixture.whenStable();

    expect(fixture.componentInstance.liveTraces()).toEqual(summaries);
    expect(fixture.nativeElement.textContent).toContain('/api/orders');
  });
});
