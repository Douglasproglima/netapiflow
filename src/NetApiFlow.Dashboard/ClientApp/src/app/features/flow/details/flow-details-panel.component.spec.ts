import { TestBed } from '@angular/core/testing';
import { FlowDetailsPanelComponent } from './flow-details-panel.component';
import { LanguageService } from '../../../core/i18n/language.service';
import { FlowSpan, FlowTraceDetail, SpanDetail } from '../../../core/models';

function makeTrace(): FlowTraceDetail {
  const spans: FlowSpan[] = [
    {
      spanId: 'root',
      parentSpanId: null,
      sequence: 0,
      component: 'OrdersController',
      operation: 'Get',
      type: 'Controller',
      startedAt: '2026-01-01T00:00:00.000000Z',
      endedAt: '2026-01-01T00:00:00.100000Z',
      durationMicros: 100_000,
      status: 'Success',
      errorType: null,
      errorMessage: null,
      isErrorOrigin: false,
      tags: {},
      logs: [],
    },
  ];

  return {
    traceId: 't1',
    method: 'GET',
    routeTemplate: '/api/orders/{id}',
    httpStatus: 200,
    status: 'Success',
    startedAt: '2026-01-01T00:00:00.000000Z',
    endedAt: '2026-01-01T00:00:00.100000Z',
    durationMicros: 100_000,
    spanCount: spans.length,
    truncated: false,
    errorMessage: null,
    tags: {},
    spans,
  };
}

function makeSpanDetail(): SpanDetail {
  return {
    span: {
      spanId: 'root',
      parentSpanId: null,
      sequence: 0,
      component: 'OrdersController',
      operation: 'Get',
      type: 'Controller',
      startedAt: '2026-01-01T00:00:00.000000Z',
      endedAt: '2026-01-01T00:00:00.100000Z',
      durationMicros: 100_000,
      status: 'Success',
      errorType: null,
      errorMessage: null,
      isErrorOrigin: false,
      tags: {},
      logs: [],
    },
    ancestors: [],
    children: [{ spanId: 'child', component: 'OrderService', operation: 'GetAsync' }],
    medianDurationMicros: 80_000,
    deltaFromMedianMicros: 20_000,
  };
}

function dbSpan(spanId: string, sequence: number): FlowSpan {
  return {
    spanId,
    parentSpanId: 'root',
    sequence,
    component: 'OrderRepository',
    operation: 'SELECT Orders',
    type: 'Database',
    startedAt: '2026-01-01T00:00:00.000000Z',
    endedAt: '2026-01-01T00:00:00.050000Z',
    durationMicros: 50_000,
    status: 'Success',
    errorType: null,
    errorMessage: null,
    isErrorOrigin: false,
    tags: {},
    logs: [],
  };
}

function makeDbSpanDetail(span: FlowSpan): SpanDetail {
  return {
    span,
    ancestors: [],
    children: [],
    medianDurationMicros: 50_000,
    deltaFromMedianMicros: 0,
  };
}

describe('FlowDetailsPanelComponent', () => {
  it('shows the request summary and waterfall when nothing is selected', async () => {
    const fixture = TestBed.createComponent(FlowDetailsPanelComponent);
    fixture.componentRef.setInput('trace', makeTrace());
    fixture.componentRef.setInput('slowThresholdMicros', 250_000);
    await fixture.whenStable();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('GET');
    expect(text).toContain('/api/orders/{id}');
    // pt-BR is the default language (D-35).
    expect(text).toContain('Etapas');
    expect(fixture.nativeElement.querySelectorAll('.flow-waterfall__row')).toHaveLength(1);
  });

  it('gives each waterfall label the whole component and operation as its tooltip', async () => {
    const trace = makeTrace();
    trace.spans[0].component = 'Minimal API';
    trace.spans[0].operation = 'GET /api/orders/{id:int}/items/{itemId:int}/history';

    const fixture = TestBed.createComponent(FlowDetailsPanelComponent);
    fixture.componentRef.setInput('trace', trace);
    fixture.componentRef.setInput('slowThresholdMicros', 250_000);
    await fixture.whenStable();

    const label: HTMLElement = fixture.nativeElement.querySelector('.flow-waterfall__label');
    expect(label.getAttribute('title')).toBe(
      'Minimal API.GET /api/orders/{id:int}/items/{itemId:int}/history',
    );
  });

  describe('unexplained time badge (D-78)', () => {
    it('shows the badge with the percent and both absolute durations for a finished trace', async () => {
      const trace = makeTrace();
      trace.durationMicros = 100_000;
      trace.spans[0].durationMicros = 60_000;
      trace.spans[0].endedAt = '2026-01-01T00:00:00.060000Z';

      const fixture = TestBed.createComponent(FlowDetailsPanelComponent);
      fixture.componentRef.setInput('trace', trace);
      fixture.componentRef.setInput('slowThresholdMicros', 250_000);
      await fixture.whenStable();

      const badge: HTMLElement = fixture.nativeElement.querySelector('.flow-details__unexplained');
      expect(badge).not.toBeNull();
      expect(badge.textContent).toContain('40%');
      expect(badge.textContent).toContain('40 ms');
      expect(badge.textContent).toContain('100 ms');
    });

    it('does not show the badge for a trace still running', async () => {
      const trace = makeTrace();
      trace.status = 'Running';
      trace.endedAt = null;
      trace.durationMicros = null;

      const fixture = TestBed.createComponent(FlowDetailsPanelComponent);
      fixture.componentRef.setInput('trace', trace);
      fixture.componentRef.setInput('slowThresholdMicros', 250_000);
      await fixture.whenStable();

      expect(fixture.nativeElement.querySelector('.flow-details__unexplained')).toBeNull();
    });
  });

  it('shows the full span detail, including children and the delta from the median, when a span is selected', async () => {
    const fixture = TestBed.createComponent(FlowDetailsPanelComponent);
    fixture.componentRef.setInput('trace', makeTrace());
    fixture.componentRef.setInput('slowThresholdMicros', 250_000);
    fixture.componentRef.setInput('selectedSpan', makeSpanDetail());
    await fixture.whenStable();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('OrdersController.Get');
    expect(text).toContain('OrderService.GetAsync');
    expect(text).toContain('+20 ms');
    expect(fixture.nativeElement.querySelectorAll('.flow-waterfall__row')).toHaveLength(0);
  });

  describe('span logs (ADR-0017)', () => {
    it('shows no logs section when the span has no correlated logs', async () => {
      const fixture = TestBed.createComponent(FlowDetailsPanelComponent);
      fixture.componentRef.setInput('trace', makeTrace());
      fixture.componentRef.setInput('slowThresholdMicros', 250_000);
      fixture.componentRef.setInput('selectedSpan', makeSpanDetail());
      await fixture.whenStable();

      expect(fixture.nativeElement.querySelector('.flow-details__logs')).toBeNull();
    });

    it('shows level, message and exception for each correlated log', async () => {
      const spanDetail = makeSpanDetail();
      spanDetail.span.logs = [
        {
          timestamp: '2026-01-01T00:00:00.050000Z',
          level: 'Warning',
          message: 'stock running low',
          exceptionType: null,
          exceptionMessage: null,
        },
        {
          timestamp: '2026-01-01T00:00:00.060000Z',
          level: 'Error',
          message: 'order confirmation failed',
          exceptionType: 'System.InvalidOperationException',
          exceptionMessage: 'boom',
        },
      ];

      const fixture = TestBed.createComponent(FlowDetailsPanelComponent);
      fixture.componentRef.setInput('trace', makeTrace());
      fixture.componentRef.setInput('slowThresholdMicros', 250_000);
      fixture.componentRef.setInput('selectedSpan', spanDetail);
      await fixture.whenStable();

      const entries = fixture.nativeElement.querySelectorAll('.flow-details__log');
      expect(entries).toHaveLength(2);

      const text: string = fixture.nativeElement.textContent;
      expect(text).toContain('stock running low');
      expect(text).toContain('order confirmation failed');
      expect(text).toContain('System.InvalidOperationException');
      expect(text).toContain('boom');
      expect(entries[0].classList.contains('flow-details__log--warning')).toBe(true);
      expect(entries[1].classList.contains('flow-details__log--warning')).toBe(false);
    });
  });

  describe('N+1 group (D-71)', () => {
    it('shows the N+1 message and jumps to the first sibling when a flagged span is selected', async () => {
      const trace = makeTrace();
      trace.spans.push(dbSpan('q1', 1), dbSpan('q2', 2), dbSpan('q3', 3));

      const fixture = TestBed.createComponent(FlowDetailsPanelComponent);
      fixture.componentRef.setInput('trace', trace);
      fixture.componentRef.setInput('slowThresholdMicros', 250_000);
      fixture.componentRef.setInput('selectedSpan', makeDbSpanDetail(dbSpan('q2', 2)));
      await fixture.whenStable();

      const text: string = fixture.nativeElement.textContent;
      expect(text).toContain('3');

      let jumpedTo: string | null = null;
      fixture.componentInstance.jumpToSpan.subscribe((spanId) => (jumpedTo = spanId));

      const jumpButton: HTMLButtonElement = fixture.nativeElement.querySelector(
        '.flow-details__warnbox-action',
      );
      jumpButton.click();

      expect(jumpedTo).toBe('q1');
    });

    it('shows no N+1 message when the selected span is not part of a flagged group', async () => {
      const fixture = TestBed.createComponent(FlowDetailsPanelComponent);
      fixture.componentRef.setInput('trace', makeTrace());
      fixture.componentRef.setInput('slowThresholdMicros', 250_000);
      fixture.componentRef.setInput('selectedSpan', makeSpanDetail());
      await fixture.whenStable();

      expect(fixture.nativeElement.querySelector('.flow-details__warnbox')).toBeNull();
    });
  });

  describe('trace identity (S3)', () => {
    const OWN_ID = 'a'.repeat(32);
    const W3C_ID = 'b'.repeat(32);

    afterEach(() => {
      localStorage.clear();
    });

    async function renderSummary(tags: Record<string, string>, language: 'pt-BR' | 'en') {
      TestBed.inject(LanguageService).setLanguage(language);
      const trace = makeTrace();
      trace.traceId = OWN_ID;
      trace.tags = tags;
      const fixture = TestBed.createComponent(FlowDetailsPanelComponent);
      fixture.componentRef.setInput('trace', trace);
      fixture.componentRef.setInput('slowThresholdMicros', 250_000);
      await fixture.whenStable();

      const read = (selector: string): string[] =>
        Array.from(fixture.nativeElement.querySelectorAll(selector)).map(
          (element) => (element as HTMLElement).textContent?.trim() ?? '',
        );
      return {
        labels: read('.flow-details__meta-dl dt'),
        values: read('.flow-details__meta-dl dd'),
      };
    }

    it.each(['pt-BR', 'en'] as const)(
      'shows the id of the trace and the W3C trace id, each with its own label, in %s',
      async (language) => {
        const { labels, values } = await renderSummary({ 'w3c.trace_id': W3C_ID }, language);

        expect(labels.slice(0, 2)).toEqual(['Trace', 'W3C trace']);
        expect(values.slice(0, 2)).toEqual([OWN_ID, W3C_ID]);
      },
    );

    it('shows only the id of the trace when the trace has no W3C trace id', async () => {
      const { labels, values } = await renderSummary({}, 'en');

      expect(labels).not.toContain('W3C trace');
      expect(values).not.toContain(W3C_ID);
      expect(values[0]).toBe(OWN_ID);
    });
  });
});
