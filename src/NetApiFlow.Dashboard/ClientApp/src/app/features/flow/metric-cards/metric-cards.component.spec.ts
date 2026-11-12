import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { Subject } from 'rxjs';
import { MetricCardsComponent } from './metric-cards.component';
import {
  ComponentStat,
  FlowEvent,
  FlowTraceSummary,
  MetricsSummary,
  PagedResult,
} from '../../../core/models';
import { SseService } from '../../../core/sse.service';

const SUMMARY: MetricsSummary = {
  totalTraces: 10,
  errorCount: 3,
  errorRate: 0.3,
  averageDurationMicros: 21_500,
  p95DurationMicros: 88_200,
  maxDurationMicros: 412_000,
};

const COMPONENT_STATS: ComponentStat[] = [
  {
    component: 'OrderRepository',
    operation: 'FindAsync',
    executionCount: 40,
    errorCount: 1,
    medianDurationMicros: 1_000,
    averageDurationMicros: 1_200,
    p95DurationMicros: 2_000,
    maxDurationMicros: 3_000,
  },
  {
    component: 'OrderService',
    operation: 'GetAsync',
    executionCount: 10,
    errorCount: 0,
    medianDurationMicros: 1_500,
    averageDurationMicros: 1_600,
    p95DurationMicros: 2_500,
    maxDurationMicros: 3_500,
  },
];

function trace(
  overrides: Partial<FlowTraceSummary> & Pick<FlowTraceSummary, 'traceId'>,
): FlowTraceSummary {
  return {
    method: 'GET',
    routeTemplate: '/api/orders/{id}',
    httpStatus: 200,
    status: 'Success',
    startedAt: '2026-01-01T00:00:00.000000Z',
    durationMicros: 1_000,
    truncated: false,
    ...overrides,
  };
}

const TRACES_PAGE: PagedResult<FlowTraceSummary> = {
  totalCount: 3,
  items: [
    trace({ traceId: 't3', durationMicros: 3_000 }),
    trace({ traceId: 't2', durationMicros: 2_000 }),
    trace({ traceId: 't1', durationMicros: 1_000 }),
  ],
};

function fakeSseService() {
  const events = new Subject<FlowEvent>();
  return { events$: events.asObservable(), emit: (event: FlowEvent) => events.next(event) };
}

describe('MetricCardsComponent', () => {
  let httpMock: HttpTestingController;
  let sse: ReturnType<typeof fakeSseService>;

  beforeEach(() => {
    sse = fakeSseService();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SseService, useValue: sse },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function flushConfig(slowThresholdMicros = 250_000) {
    httpMock.expectOne('api/config').flush({ slowThresholdMicros, version: '0.1.0' });
  }

  function flushSparkline(page: PagedResult<FlowTraceSummary> = TRACES_PAGE) {
    httpMock
      .expectOne(
        (request) =>
          request.url === 'api/traces' &&
          request.params.get('page') === '1' &&
          request.params.get('pageSize') === '50',
      )
      .flush(page);
  }

  it('renders success/errors derived from errorCount and steps executed summed from component metrics', async () => {
    const fixture = TestBed.createComponent(MetricCardsComponent);
    await fixture.whenStable();
    flushConfig();
    httpMock.expectOne('api/metrics').flush(SUMMARY);
    httpMock.expectOne('api/metrics/components').flush(COMPONENT_STATS);
    flushSparkline();
    await fixture.whenStable();

    const cards = fixture.componentInstance.cards();
    expect(cards).toEqual([
      {
        labelKey: 'metrics.traces',
        value: '10',
        valueUnit: null,
        icon: 'traces',
        tint: 'flow',
        highlight: false,
      },
      {
        labelKey: 'metrics.successful',
        value: '7',
        valueUnit: null,
        icon: 'success',
        tint: 'success',
        highlight: false,
      },
      {
        labelKey: 'metrics.errors',
        value: '3',
        valueUnit: null,
        icon: 'errors',
        tint: 'danger',
        highlight: true,
      },
      {
        labelKey: 'metrics.avgLatency',
        value: '21.5',
        valueUnit: 'ms',
        icon: 'avg',
        tint: 'flow',
        highlight: false,
      },
      {
        labelKey: 'metrics.maxLatency',
        value: '412',
        valueUnit: 'ms',
        icon: 'max',
        tint: 'warning',
        highlight: false,
      },
      {
        labelKey: 'metrics.stepsExecuted',
        value: '50',
        valueUnit: null,
        icon: 'steps',
        tint: 'flow',
        highlight: false,
      },
    ]);
  });

  it('does not highlight the errors card when there are no errors', async () => {
    const fixture = TestBed.createComponent(MetricCardsComponent);
    await fixture.whenStable();
    flushConfig();
    httpMock.expectOne('api/metrics').flush({ ...SUMMARY, errorCount: 0, errorRate: 0 });
    httpMock.expectOne('api/metrics/components').flush(COMPONENT_STATS);
    flushSparkline();
    await fixture.whenStable();

    const errorsCard = fixture.componentInstance.cards().find((card) => card.icon === 'errors');
    expect(errorsCard?.highlight).toBe(false);
    expect(errorsCard?.tint).toBe('idle');
  });

  it('tints the latency tiles warning only when they are at or above the slow threshold', async () => {
    const fixture = TestBed.createComponent(MetricCardsComponent);
    await fixture.whenStable();
    flushConfig(500_000);
    httpMock.expectOne('api/metrics').flush(SUMMARY);
    httpMock.expectOne('api/metrics/components').flush(COMPONENT_STATS);
    flushSparkline();
    await fixture.whenStable();

    const cards = fixture.componentInstance.cards();
    expect(cards.find((card) => card.icon === 'avg')?.tint).toBe('flow');
    expect(cards.find((card) => card.icon === 'max')?.tint).toBe('flow');
  });

  it('shows an explicit error state when GET /metrics fails', async () => {
    const fixture = TestBed.createComponent(MetricCardsComponent);
    await fixture.whenStable();
    flushConfig();
    httpMock.expectOne('api/metrics').flush('boom', { status: 500, statusText: 'Server Error' });
    httpMock.expectOne('api/metrics/components').flush(COMPONENT_STATS);
    flushSparkline();
    await fixture.whenStable();

    // pt-BR is the default language (D-35).
    expect(fixture.nativeElement.textContent).toContain('Falha ao carregar as métricas.');
  });

  it('re-fetches the metrics when a TraceCompleted event arrives (D-39)', async () => {
    const fixture = TestBed.createComponent(MetricCardsComponent);
    await fixture.whenStable();
    flushConfig();
    httpMock.expectOne('api/metrics').flush(SUMMARY);
    httpMock.expectOne('api/metrics/components').flush(COMPONENT_STATS);
    flushSparkline();
    await fixture.whenStable();

    expect(fixture.componentInstance.cards().find((card) => card.icon === 'traces')?.value).toBe(
      '10',
    );

    sse.emit({
      eventId: 1,
      type: 'TraceCompleted',
      traceId: 't1',
      timestamp: '2026-01-01T00:00:00Z',
    });
    httpMock.expectOne('api/metrics').flush({ ...SUMMARY, totalTraces: 11 });
    httpMock.expectOne('api/metrics/components').flush(COMPONENT_STATS);
    flushSparkline();
    await fixture.whenStable();

    expect(fixture.componentInstance.cards().find((card) => card.icon === 'traces')?.value).toBe(
      '11',
    );
  });

  it('renders the p95 figure from GET /metrics and a sparkline point per execution from GET /traces', async () => {
    const fixture = TestBed.createComponent(MetricCardsComponent);
    await fixture.whenStable();
    flushConfig();
    httpMock.expectOne('api/metrics').flush(SUMMARY);
    httpMock.expectOne('api/metrics/components').flush(COMPONENT_STATS);
    flushSparkline();
    await fixture.whenStable();

    expect(fixture.componentInstance.p95Value()).toEqual({ value: '88.2', unit: 'ms' });

    const points = fixture.componentInstance
      .sparklinePoints()
      .split(' ')
      .map((pair) => pair.split(',').map(Number));
    expect(points).toHaveLength(TRACES_PAGE.items.length);
    // flushSparkline's page arrives newest-first (t3, t2, t1); the sparkline reverses it so the
    // oldest execution (t1, the shortest) plots first/lowest and the newest (t3) plots last/highest.
    expect(points[0][0]).toBeLessThan(points[2][0]);
    expect(points[0][1]).toBeGreaterThan(points[2][1]);
  });

  it('ignores traces with a null duration (still running) when building the sparkline', async () => {
    const fixture = TestBed.createComponent(MetricCardsComponent);
    await fixture.whenStable();
    flushConfig();
    httpMock.expectOne('api/metrics').flush(SUMMARY);
    httpMock.expectOne('api/metrics/components').flush(COMPONENT_STATS);
    flushSparkline({
      totalCount: 2,
      items: [trace({ traceId: 'running', durationMicros: null }), trace({ traceId: 't1' })],
    });
    await fixture.whenStable();

    expect(fixture.componentInstance.sparklinePoints().split(' ')).toHaveLength(1);
  });
});
