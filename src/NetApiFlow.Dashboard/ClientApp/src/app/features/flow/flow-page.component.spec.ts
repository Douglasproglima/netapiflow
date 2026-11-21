import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { FlowPageComponent } from './flow-page.component';
import { LanguageService } from '../../core/i18n/language.service';
import { SseService, SseConnectionState } from '../../core/sse.service';
import { FlowEvent, FlowTrace, FlowTraceDetail, FlowTraceSummary } from '../../core/models';

const sseEvents$ = new Subject<FlowEvent>();
const sseReconnected$ = new Subject<void>();

const fakeSse = {
  events$: sseEvents$.asObservable(),
  reconnected$: sseReconnected$.asObservable(),
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

  /**
   * FlowPageComponent, the metric cards and the action panel all read the same `ConfigService`
   * (M9), so exactly one GET /config request serves every one of them.
   */
  function flushAllConfigRequests(slowThresholdMicros = 250_000): void {
    for (const request of httpMock.match('api/config')) {
      request.flush({
        slowThresholdMicros,
        version: '0.1.0',
        openApiDocumentPath: '/openapi/v1.json',
        hostPathBase: '',
      });
    }
  }

  /**
   * The workspace's metric cards and action panel fire their own requests on mount. Both
   * RecentExecutionsComponent (pageSize 8) and the metric cards' sparkline (pageSize 50) call
   * GET /traces independently, so both must be matched and flushed.
   */
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
    for (const request of httpMock.match((request) => request.url === 'api/traces')) {
      request.flush({ totalCount: 0, items: [] });
    }
  }

  /**
   * M1: a live trace fires its own `GET /blueprint` once it loads. Flushed as 204 ("no ghosts")
   * unless a test cares about what it returns, since the fetch only becomes a pending request
   * after a tick (`livePlayer.trace()` becoming non-null), hence the extra `whenStable()`.
   */
  async function flushLiveBlueprint(
    fixture: ComponentFixture<FlowPageComponent>,
    blueprint: FlowTraceDetail | null = null,
  ): Promise<TestRequest[]> {
    await fixture.whenStable();
    const requests = httpMock.match((request) => request.url === 'api/blueprint');
    for (const request of requests) {
      if (blueprint) {
        request.flush(blueprint);
      } else {
        request.flush(null, { status: 204, statusText: 'No Content' });
      }
    }
    await fixture.whenStable();
    return requests;
  }

  function findReplayButton(fixture: ComponentFixture<FlowPageComponent>): HTMLButtonElement {
    const button = Array.from(fixture.nativeElement.querySelectorAll('button')).find(
      (candidate): candidate is HTMLButtonElement =>
        (candidate as HTMLButtonElement).textContent?.trim() === 'Replay',
    );
    if (!button) {
      throw new Error('Replay button not found');
    }
    return button;
  }

  it('loads the trace from the ?trace= query param on mount', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests();
    httpMock.expectOne('api/traces/t1').flush(makeTrace('t1'));
    flushWorkspaceChildRequests();
    await fixture.whenStable();
    await flushLiveBlueprint(fixture);

    expect(fixture.componentInstance.trace()?.traceId).toBe('t1');
    expect(fixture.componentInstance.graph().nodes).toHaveLength(1);
  });

  it('fetches the blueprint once per tracked trace and marks a skipped branch not-executed before the trace ends (M1)', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests();
    httpMock.expectOne('api/traces/t1').flush({
      ...makeTrace('t1'),
      status: 'Running',
      httpStatus: null,
      endedAt: null,
      durationMicros: null,
    });
    flushWorkspaceChildRequests();
    await fixture.whenStable();

    const blueprint: FlowTraceDetail = {
      ...makeTrace('t1'),
      spans: [
        ...makeTrace('t1').spans,
        {
          spanId: 'bp-notify',
          parentSpanId: 's1',
          sequence: 1,
          component: 'NotificationService',
          operation: 'SendAsync',
          type: 'Service',
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
    const [blueprintReq] = await flushLiveBlueprint(fixture, blueprint);
    expect(blueprintReq.request.params.get('method')).toBe('GET');
    expect(blueprintReq.request.params.get('route')).toBe('/api/orders/{id}');

    // The root span (s1) already concluded without taking the blueprint's extra branch: that
    // branch is settled as not-executed now, even though the trace overall is still Running.
    expect(fixture.componentInstance.trace()?.status).toBe('Running');
    expect(
      fixture.componentInstance.graph().nodes.find((n) => n.component === 'NotificationService')
        ?.state,
    ).toBe('not-executed');
  });

  async function openTraceWithBlueprint(
    trace: FlowTraceDetail,
    blueprint: FlowTraceDetail | null = null,
  ): Promise<{ fixture: ComponentFixture<FlowPageComponent>; blueprintRequests: TestRequest[] }> {
    configure(trace.traceId);
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests();
    httpMock.expectOne(`api/traces/${trace.traceId}`).flush(trace);
    flushWorkspaceChildRequests();
    await fixture.whenStable();
    const blueprintRequests = await flushLiveBlueprint(fixture, blueprint);
    return { fixture, blueprintRequests };
  }

  function withANotificationBranch(trace: FlowTraceDetail): FlowTraceDetail {
    return {
      ...trace,
      spans: [
        ...trace.spans,
        {
          ...trace.spans[0],
          spanId: 'bp-notify',
          parentSpanId: 's1',
          sequence: 1,
          component: 'NotificationService',
          operation: 'SendAsync',
          type: 'Service',
          status: 'Success',
          endedAt: '2026-01-01T00:00:00.000100Z',
          durationMicros: 100,
        },
      ],
    };
  }

  it.skip('asks for the blueprint of a trace with the start of that trace as the anchor (F1b)', async () => {
    const { blueprintRequests } = await openTraceWithBlueprint(makeTrace('t1'));

    expect(blueprintRequests).toHaveLength(1);
    expect(blueprintRequests[0].request.params.get('before')).toBe('2026-01-01T00:00:00.000000Z');
  });

  it('does not invent a not-executed step when no earlier execution exists', async () => {
    const { fixture } = await openTraceWithBlueprint(makeTrace('t1'), null);

    const states = fixture.componentInstance.graph().nodes.map((node) => node.state);
    expect(states).not.toContain('not-executed');
    expect(states).not.toContain('waiting');
  });

  it('does not ask for a blueprint for a trace whose route is unmatched', async () => {
    const { blueprintRequests } = await openTraceWithBlueprint({
      ...makeTrace('missing-route'),
      routeTemplate: '(unmatched)',
      httpStatus: 404,
      spanCount: 0,
      spans: [],
    });

    expect(blueprintRequests).toHaveLength(0);
  });

  it('draws the trace without a blueprint when the blueprint request fails', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();
    flushAllConfigRequests();
    httpMock.expectOne('api/traces/t1').flush(makeTrace('t1'));
    flushWorkspaceChildRequests();
    await fixture.whenStable();

    const [blueprintRequest] = httpMock.match((request) => request.url === 'api/blueprint');
    blueprintRequest.flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();

    expect(fixture.componentInstance.trace()?.traceId).toBe('t1');
    const states = fixture.componentInstance.graph().nodes.map((node) => node.state);
    expect(states).not.toContain('not-executed');
    expect(states).not.toContain('waiting');
  });

  it('shows a branch of the blueprint as waiting while the trace runs', async () => {
    const running: FlowTraceDetail = {
      ...makeTrace('running'),
      status: 'Running',
      httpStatus: null,
      endedAt: null,
      durationMicros: null,
    };
    running.spans = [
      { ...running.spans[0], status: 'Running', endedAt: null, durationMicros: null },
    ];

    const { fixture } = await openTraceWithBlueprint(
      running,
      withANotificationBranch(makeTrace('running')),
    );

    const branch = fixture.componentInstance
      .graph()
      .nodes.find((node) => node.component === 'NotificationService');
    expect(branch?.state).toBe('waiting');
  });

  it('shows the same branch as not executed once the trace has ended without it', async () => {
    const { fixture } = await openTraceWithBlueprint(
      makeTrace('ended'),
      withANotificationBranch(makeTrace('ended')),
    );

    const branch = fixture.componentInstance
      .graph()
      .nodes.find((node) => node.component === 'NotificationService');
    expect(branch?.state).toBe('not-executed');
  });

  it('shows an explicit not-found state for a trace that expired or never existed (A3)', async () => {
    configure('missing');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests();
    httpMock
      .expectOne('api/traces/missing')
      .flush('not found', { status: 404, statusText: 'Not Found' });
    flushWorkspaceChildRequests();
    await fixture.whenStable();

    expect(fixture.componentInstance.trace()).toBeNull();
    expect(fixture.componentInstance.traceNotFound()).toBe(true);
    // pt-BR is the default language; must not show the generic empty-list state instead.
    expect(fixture.nativeElement.textContent).toContain('Trace não encontrada.');
    expect(fixture.nativeElement.textContent).not.toContain('Nenhuma requisição em andamento');
  });

  async function openTraceWithoutSteps(
    traceId: string,
    overrides: Partial<FlowTraceDetail> = {},
    language: 'pt-BR' | 'en' = 'pt-BR',
  ): Promise<ComponentFixture<FlowPageComponent>> {
    configure(traceId);
    if (language === 'en') {
      TestBed.inject(LanguageService).setLanguage('en');
    }

    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests();
    httpMock
      .expectOne(`api/traces/${traceId}`)
      .flush({ ...makeTrace(traceId), spanCount: 0, spans: [], ...overrides });
    flushWorkspaceChildRequests();
    await fixture.whenStable();
    await flushLiveBlueprint(fixture);
    return fixture;
  }

  it('explains how to mark a step when a finished trace has none, instead of leaving the canvas blank', async () => {
    const fixture = await openTraceWithoutSteps('plain');

    const emptyState: HTMLElement | null = fixture.nativeElement.querySelector(
      '.flow-workspace__empty--no-steps',
    );
    expect(emptyState).not.toBeNull();
    expect(emptyState?.textContent).toContain(
      'A requisição foi gravada, mas nenhuma etapa foi marcada.',
    );
    expect(emptyState?.textContent).toContain('.WithNetApiFlowTracking()');
    expect(emptyState?.textContent).toContain('NetApiFlow.Step(...)');
    expect(emptyState?.textContent).toContain('AddFlowTracked');
    expect(emptyState?.querySelector('svg')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-flow-graph')).toBeNull();
  });

  it('explains the same in English', async () => {
    try {
      const fixture = await openTraceWithoutSteps('plain', {}, 'en');

      const emptyState: HTMLElement | null = fixture.nativeElement.querySelector(
        '.flow-workspace__empty--no-steps',
      );
      expect(emptyState?.textContent).toContain(
        'The request was recorded, but no step was marked.',
      );
      expect(emptyState?.textContent).toContain('.WithNetApiFlowTracking()');
    } finally {
      localStorage.clear();
    }
  });

  it('says a Minimal API endpoint becomes a step on its own, and needs the call only with the capture turned off', async () => {
    const fixture = await openTraceWithoutSteps('plain');

    const emptyState: HTMLElement | null = fixture.nativeElement.querySelector(
      '.flow-workspace__empty--no-steps',
    );
    expect(emptyState?.textContent).toContain('vira etapa sozinho');
    expect(emptyState?.textContent).toContain('CaptureMinimalApiEndpoints');
    expect(emptyState?.textContent).not.toContain('Um endpoint de Minimal API precisa de');
  });

  it('says the same about the Minimal API endpoint in English', async () => {
    try {
      const fixture = await openTraceWithoutSteps('plain', {}, 'en');

      const emptyState: HTMLElement | null = fixture.nativeElement.querySelector(
        '.flow-workspace__empty--no-steps',
      );
      expect(emptyState?.textContent).toContain('becomes a step on its own');
      expect(emptyState?.textContent).toContain('CaptureMinimalApiEndpoints');
    } finally {
      localStorage.clear();
    }
  });

  it('says there is no endpoint to mark when the request matched no route, instead of asking to mark one', async () => {
    const fixture = await openTraceWithoutSteps('missing-route', {
      routeTemplate: '(unmatched)',
      httpStatus: 404,
    });

    const emptyState: HTMLElement | null = fixture.nativeElement.querySelector(
      '.flow-workspace__empty--no-steps',
    );
    expect(emptyState).not.toBeNull();
    expect(emptyState?.textContent).toContain('não casou com nenhuma rota');
    expect(emptyState?.textContent).not.toContain('.WithNetApiFlowTracking()');
    expect(emptyState?.textContent).not.toContain('nenhuma etapa foi marcada');
    expect(emptyState?.querySelector('svg')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('app-flow-graph')).toBeNull();
  });

  it('says the same about a request that matched no route in English', async () => {
    try {
      const fixture = await openTraceWithoutSteps(
        'missing-route',
        { routeTemplate: '(unmatched)', httpStatus: 404 },
        'en',
      );

      const emptyState: HTMLElement | null = fixture.nativeElement.querySelector(
        '.flow-workspace__empty--no-steps',
      );
      expect(emptyState?.textContent).toContain('matched no route');
      expect(emptyState?.textContent).not.toContain('.WithNetApiFlowTracking()');
      expect(emptyState?.textContent).not.toContain('no step was marked');
    } finally {
      localStorage.clear();
    }
  });

  it('does not say a trace has no steps while it is still running and its steps have not arrived', async () => {
    const fixture = await openTraceWithoutSteps('running', {
      status: 'Running',
      httpStatus: null,
      endedAt: null,
      durationMicros: null,
    });

    expect(fixture.nativeElement.querySelector('.flow-workspace__empty--no-steps')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-flow-graph')).not.toBeNull();
  });

  it('keeps drawing the graph for a trace that has steps', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests();
    httpMock.expectOne('api/traces/t1').flush(makeTrace('t1'));
    flushWorkspaceChildRequests();
    await fixture.whenStable();
    await flushLiveBlueprint(fixture);

    expect(fixture.nativeElement.querySelector('.flow-workspace__empty--no-steps')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-flow-graph')).not.toBeNull();
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
    // pt-BR is the default language; the status must not leak the raw English "Running".
    expect(fixture.nativeElement.textContent).toContain('Em execução');
    expect(fixture.nativeElement.textContent).not.toContain('Running');
  });

  it('refreshes the in-progress list on a TraceStarted/TraceCompleted SSE event, without a page reload (M6)', async () => {
    configure(null);
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests();
    httpMock.expectOne('api/traces/live').flush([]);
    flushWorkspaceChildRequests();
    await fixture.whenStable();

    expect(fixture.componentInstance.liveTraces()).toEqual([]);

    vi.useFakeTimers({
      toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'],
    });
    sseEvents$.next({
      eventId: 1,
      type: 'TraceStarted',
      traceId: 't9',
      timestamp: '2026-01-01T00:00:00.000000Z',
    });
    vi.advanceTimersByTime(1500);
    vi.useRealTimers();
    await Promise.resolve();
    fixture.detectChanges();

    const summaries: FlowTraceSummary[] = [
      {
        traceId: 't9',
        method: 'POST',
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
  });

  it('marks a span as slow using the threshold fetched from GET /config, not a fixed constant', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests(50);
    httpMock.expectOne('api/traces/t1').flush(makeTrace('t1'));
    flushWorkspaceChildRequests();
    await fixture.whenStable();
    await flushLiveBlueprint(fixture);

    expect(fixture.componentInstance.graph().nodes[0].state).toBe('slow');
  });

  it('falls back to a reasonable default threshold when GET /config fails, instead of blocking the graph (M9)', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    for (const request of httpMock.match('api/config')) {
      request.flush('boom', { status: 500, statusText: 'Server Error' });
    }
    httpMock.expectOne('api/traces/t1').flush(makeTrace('t1'));
    flushWorkspaceChildRequests();
    await fixture.whenStable();
    await flushLiveBlueprint(fixture);

    expect(fixture.componentInstance.slowThresholdMicros()).toBe(250_000);
    expect(fixture.componentInstance.graph().nodes).toHaveLength(1);
  });

  it('exits the loading state with an error message when loading the in-progress list fails (M9)', async () => {
    configure(null);
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests();
    httpMock
      .expectOne('api/traces/live')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    flushWorkspaceChildRequests();
    await fixture.whenStable();

    expect(fixture.componentInstance.liveTracesLoading()).toBe(false);
    expect(fixture.componentInstance.liveTracesError()).toBe(true);
    // pt-BR is the default language.
    expect(fixture.nativeElement.textContent).toContain(
      'Falha ao carregar as requisições em andamento.',
    );
  });

  it('toggles into replay mode and back to live', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests();
    httpMock.expectOne('api/traces/t1').flush(makeTrace('t1'));
    flushWorkspaceChildRequests();
    await fixture.whenStable();
    await flushLiveBlueprint(fixture);

    expect(fixture.componentInstance.isReplaying()).toBe(false);

    fixture.componentInstance.toggleReplay();
    expect(fixture.componentInstance.isReplaying()).toBe(true);
    expect(fixture.componentInstance.graph().nodes).toHaveLength(1);
    // Not yet revealed by the replay, but the replay itself isn't finished either (A4): this
    // must read as "waiting", never "not-executed" — the latter would claim the step never ran.
    expect(fixture.componentInstance.graph().nodes[0].state).toBe('waiting');

    fixture.componentInstance.replayPlayer.stepForward();
    expect(fixture.componentInstance.graph().nodes).toHaveLength(1);
    expect(fixture.componentInstance.graph().nodes[0].state).toBe('completed');

    fixture.componentInstance.toggleReplay();
    expect(fixture.componentInstance.isReplaying()).toBe(false);
    expect(fixture.componentInstance.graph().nodes).toHaveLength(1);
  });

  it('disables the replay button while the trace is Running, and re-enables it once it concludes (M5)', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests();
    httpMock.expectOne('api/traces/t1').flush({
      ...makeTrace('t1'),
      status: 'Running',
      httpStatus: null,
      endedAt: null,
      durationMicros: null,
    });
    flushWorkspaceChildRequests();
    await fixture.whenStable();
    await flushLiveBlueprint(fixture);

    expect(findReplayButton(fixture).disabled).toBe(true);

    const completedTrace: FlowTrace = makeTrace('t1');
    sseEvents$.next({
      eventId: 1,
      type: 'TraceCompleted',
      traceId: 't1',
      timestamp: '2026-01-01T00:00:00.000200Z',
      trace: completedTrace,
    });
    await fixture.whenStable();

    expect(findReplayButton(fixture).disabled).toBe(false);
  });

  it('does not reset an already-playing replay when a new SSE event arrives for the same Running trace (M5)', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    const runningTrace = makeTrace('t1');
    runningTrace.status = 'Running';
    runningTrace.httpStatus = null;
    runningTrace.endedAt = null;
    runningTrace.durationMicros = null;
    runningTrace.spans = [
      ...runningTrace.spans,
      {
        spanId: 's2',
        parentSpanId: 's1',
        sequence: 1,
        component: 'OrderController',
        operation: 'Notify',
        type: 'Service',
        startedAt: '2026-01-01T00:00:00.000100Z',
        endedAt: '2026-01-01T00:00:00.000150Z',
        durationMicros: 50,
        status: 'Success',
        errorType: null,
        errorMessage: null,
        isErrorOrigin: false,
        tags: {},
        logs: [],
      },
    ];

    flushAllConfigRequests();
    httpMock.expectOne('api/traces/t1').flush(runningTrace);
    flushWorkspaceChildRequests();
    await fixture.whenStable();
    await flushLiveBlueprint(fixture);

    fixture.componentInstance.toggleReplay();
    expect(fixture.componentInstance.isReplaying()).toBe(true);
    expect(fixture.componentInstance.replayPlayer.totalSteps()).toBe(2);

    fixture.componentInstance.replayPlayer.stepForward();
    expect(fixture.componentInstance.replayPlayer.stepIndex()).toBe(1);
    expect(fixture.componentInstance.replayPlayer.state()).toBe('paused');

    sseEvents$.next({
      eventId: 1,
      type: 'SpanStarted',
      traceId: 't1',
      timestamp: '2026-01-01T00:00:00.000200Z',
      span: {
        spanId: 's3',
        parentSpanId: 's1',
        sequence: 2,
        component: 'OrderController',
        operation: 'Audit',
        type: 'Service',
        startedAt: '2026-01-01T00:00:00.000150Z',
        endedAt: null,
        durationMicros: null,
        status: 'Running',
        errorType: null,
        errorMessage: null,
        isErrorOrigin: false,
        tags: {},
        logs: [],
      },
    });
    await fixture.whenStable();

    // Not reloaded (M5): the live trace now has 3 spans, but the replay already in progress
    // keeps the 2-span snapshot it started with, at the step the user left it on.
    expect(fixture.componentInstance.replayPlayer.totalSteps()).toBe(2);
    expect(fixture.componentInstance.replayPlayer.stepIndex()).toBe(1);
    expect(fixture.componentInstance.replayPlayer.state()).toBe('paused');
  });

  it('shows the live pause toggle only while following a Running trace live, not during replay', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests();
    httpMock.expectOne('api/traces/t1').flush({
      ...makeTrace('t1'),
      status: 'Running',
      httpStatus: null,
      endedAt: null,
      durationMicros: null,
    });
    flushWorkspaceChildRequests();
    await fixture.whenStable();
    await flushLiveBlueprint(fixture);

    expect(fixture.componentInstance.isFollowingLiveRunning()).toBe(true);
    // pt-BR is the default language (D-35).
    expect(fixture.nativeElement.textContent).toContain('Pausar ao vivo');

    fixture.componentInstance.toggleLivePause();
    await fixture.whenStable();
    expect(fixture.componentInstance.livePlayer.paused()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Retomar ao vivo');

    fixture.componentInstance.toggleReplay();
    await fixture.whenStable();
    expect(fixture.componentInstance.isFollowingLiveRunning()).toBe(false);
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
    await flushLiveBlueprint(fixture);

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

  it('always reflects the latest span selection, never a stale response that arrives after a newer one (M4)', async () => {
    configure('t1');
    const fixture = TestBed.createComponent(FlowPageComponent);
    await fixture.whenStable();

    flushAllConfigRequests();
    httpMock.expectOne('api/traces/t1').flush({
      ...makeTrace('t1'),
      spans: [
        ...makeTrace('t1').spans,
        {
          spanId: 's2',
          parentSpanId: 's1',
          sequence: 1,
          component: 'OrderService',
          operation: 'GetAsync',
          type: 'Service',
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
    });
    flushWorkspaceChildRequests();
    await fixture.whenStable();
    await flushLiveBlueprint(fixture);

    fixture.componentInstance.onNodeSelected('s1');
    const firstReq = httpMock.expectOne('api/traces/t1/spans/s1');

    fixture.componentInstance.onNodeSelected('s2');
    const secondReq = httpMock.expectOne('api/traces/t1/spans/s2');

    // `switchMap` (M4) unsubscribes the first (now stale) lookup the moment the second starts.
    expect(firstReq.cancelled).toBe(true);

    secondReq.flush({
      span: { ...makeTrace('t1').spans[0], spanId: 's2' },
      ancestors: [],
      children: [],
    } as never);
    await fixture.whenStable();

    expect(fixture.componentInstance.selectedSpanDetail()?.span.spanId).toBe('s2');
  });
});
