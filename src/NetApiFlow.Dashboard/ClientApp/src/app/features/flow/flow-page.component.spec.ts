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
        logs: [],
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

  /** Both FlowPageComponent and the metric cards read GET /config independently. */
  function flushAllConfigRequests(slowThresholdMicros = 250_000): void {
    for (const request of httpMock.match('api/config')) {
      request.flush({ slowThresholdMicros, version: '0.1.0' });
    }
  }

  /** The workspace's metric cards and action panel fire their own requests on mount. */
  function flushWorkspaceChildRequests(): void {
    httpMock.expectOne('api/metrics').flush({
      totalTraces: 0,
      errorCount: 0,
      errorRate: 0,
      averageDurationMicros: 0,
      p95DurationMicros: 0,
      maxDurationMicros: 0,
    });
    httpMock.expectOne('api/metrics/components').flush([]);
    httpMock.expectOne('/openapi/v1.json').flush({ paths: {} });
    httpMock
      .expectOne((request) => request.url === 'api/traces')
      .flush({ totalCount: 0, items: [] });
  }

  it('loads the trace from the ?trace= query param on mount', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests();
    httpMock.expectOne('api/traces/t1').flush(makeTrace('t1'));
    flushWorkspaceChildRequests();
    await fixture.whenStable();

    expect(fixture.componentInstance.trace()?.traceId).toBe('t1');
    expect(fixture.componentInstance.graph().nodes).toHaveLength(1);
  });

  it('lists in-progress traces when no ?trace= param is present', async () => {
    configure(null);
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests();

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
    flushWorkspaceChildRequests();
    await fixture.whenStable();

    expect(fixture.componentInstance.liveTraces()).toEqual(summaries);
    expect(fixture.nativeElement.textContent).toContain('/api/orders');
  });

  it('marks a span as slow using the threshold fetched from GET /config, not a fixed constant', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests(50);
    httpMock.expectOne('api/traces/t1').flush(makeTrace('t1'));
    flushWorkspaceChildRequests();
    await fixture.whenStable();

    expect(fixture.componentInstance.graph().nodes[0].state).toBe('slow');
  });

  it('toggles into replay mode and back to live', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests();
    httpMock.expectOne('api/traces/t1').flush(makeTrace('t1'));
    flushWorkspaceChildRequests();
    await fixture.whenStable();

    expect(fixture.componentInstance.isReplaying()).toBe(false);

    fixture.componentInstance.toggleReplay();
    expect(fixture.componentInstance.isReplaying()).toBe(true);
    expect(fixture.componentInstance.graph().nodes).toHaveLength(1);
    expect(fixture.componentInstance.graph().nodes[0].state).toBe('not-executed');

    fixture.componentInstance.replayPlayer.stepForward();
    expect(fixture.componentInstance.graph().nodes).toHaveLength(1);
    expect(fixture.componentInstance.graph().nodes[0].state).toBe('completed');

    fixture.componentInstance.toggleReplay();
    expect(fixture.componentInstance.isReplaying()).toBe(false);
    expect(fixture.componentInstance.graph().nodes).toHaveLength(1);
  });

  it('toggles and closes the action drawer', async () => {
    configure(null);
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();
    flushAllConfigRequests();
    httpMock.expectOne('api/traces/live').flush([]);
    flushWorkspaceChildRequests();
    await fixture.whenStable();

    expect(fixture.componentInstance.actionDrawer.open()).toBe(false);
    fixture.componentInstance.actionDrawer.toggle();
    expect(fixture.componentInstance.actionDrawer.open()).toBe(true);
    fixture.componentInstance.actionDrawer.close();
    expect(fixture.componentInstance.actionDrawer.open()).toBe(false);
  });

  it('exportTrace downloads the loaded trace as JSON', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();
    flushAllConfigRequests();
    httpMock.expectOne('api/traces/t1').flush(makeTrace('t1'));
    flushWorkspaceChildRequests();
    await fixture.whenStable();

    const createObjectURL = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url');
    const revokeObjectURL = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(() => undefined);

    fixture.componentInstance.exportTrace();

    expect(createObjectURL).toHaveBeenCalledTimes(1);
    const blob = createObjectURL.mock.calls[0][0] as Blob;
    expect(blob.type).toBe('application/json');
    expect(JSON.parse(await blob.text())).toEqual(makeTrace('t1'));
    expect(click).toHaveBeenCalledTimes(1);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:mock-url');

    createObjectURL.mockRestore();
    revokeObjectURL.mockRestore();
    click.mockRestore();
  });

  function fileSelectEvent(content: string, name = 'trace.json'): Event {
    const file = new File([content], name, { type: 'application/json' });
    return { target: { files: [file], value: '' } } as unknown as Event;
  }

  it('imports a trace from a file and renders it without calling the API', async () => {
    configure(null);
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();
    flushAllConfigRequests();
    httpMock.expectOne('api/traces/live').flush([]);
    flushWorkspaceChildRequests();
    await fixture.whenStable();

    const imported = makeTrace('imported-1');
    await fixture.componentInstance.onImportFileSelected(fileSelectEvent(JSON.stringify(imported)));
    await fixture.whenStable();

    expect(fixture.componentInstance.importedTrace()?.traceId).toBe('imported-1');
    expect(fixture.componentInstance.trace()?.traceId).toBe('imported-1');
    expect(fixture.componentInstance.importErrorKey()).toBeNull();

    fixture.componentInstance.onNodeSelected('s1');
    expect(fixture.componentInstance.selectedSpanDetail()?.span.spanId).toBe('s1');
    expect(fixture.componentInstance.selectedSpanDetail()?.medianDurationMicros).toBeNull();
  });

  it('shows an error and keeps the current view when the imported file is not valid JSON', async () => {
    configure(null);
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();
    flushAllConfigRequests();
    httpMock.expectOne('api/traces/live').flush([]);
    flushWorkspaceChildRequests();
    await fixture.whenStable();

    await fixture.componentInstance.onImportFileSelected(fileSelectEvent('{ not json'));
    await fixture.whenStable();

    expect(fixture.componentInstance.importErrorKey()).toBe('import.error.invalidJson');
    expect(fixture.componentInstance.trace()).toBeNull();
  });

  it('shows an error when the imported file is valid JSON but not a trace export', async () => {
    configure(null);
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();
    flushAllConfigRequests();
    httpMock.expectOne('api/traces/live').flush([]);
    flushWorkspaceChildRequests();
    await fixture.whenStable();

    await fixture.componentInstance.onImportFileSelected(fileSelectEvent('{}'));
    await fixture.whenStable();

    expect(fixture.componentInstance.importErrorKey()).toBe('import.error.invalidShape');
    expect(fixture.componentInstance.trace()).toBeNull();
  });

  it('exits imported mode back to the live list', async () => {
    configure(null);
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();
    flushAllConfigRequests();
    httpMock.expectOne('api/traces/live').flush([]);
    flushWorkspaceChildRequests();
    await fixture.whenStable();

    await fixture.componentInstance.onImportFileSelected(
      fileSelectEvent(JSON.stringify(makeTrace('imported-1'))),
    );
    await fixture.whenStable();
    expect(fixture.componentInstance.trace()).not.toBeNull();

    fixture.componentInstance.exitImportedTrace();
    await fixture.whenStable();

    expect(fixture.componentInstance.importedTrace()).toBeNull();
    expect(fixture.componentInstance.trace()).toBeNull();
  });
});
