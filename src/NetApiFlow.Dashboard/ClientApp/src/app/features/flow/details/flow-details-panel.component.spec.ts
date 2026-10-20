import { TestBed } from '@angular/core/testing';
import { FlowDetailsPanelComponent } from './flow-details-panel.component';
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
    },
    ancestors: [],
    children: [{ spanId: 'child', component: 'OrderService', operation: 'GetAsync' }],
    medianDurationMicros: 80_000,
    deltaFromMedianMicros: 20_000,
  };
}

describe('FlowDetailsPanelComponent', () => {
  it('shows the request summary and waterfall when nothing is selected', async () => {
    const fixture = TestBed.createComponent(FlowDetailsPanelComponent);
    fixture.componentRef.setInput('trace', makeTrace());
    await fixture.whenStable();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('GET /api/orders/{id}');
    expect(text).toContain('Waterfall');
    expect(fixture.nativeElement.querySelectorAll('.flow-waterfall__row')).toHaveLength(1);
  });

  it('shows the full span detail, including children and the delta from the median, when a span is selected', async () => {
    const fixture = TestBed.createComponent(FlowDetailsPanelComponent);
    fixture.componentRef.setInput('trace', makeTrace());
    fixture.componentRef.setInput('selectedSpan', makeSpanDetail());
    await fixture.whenStable();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('OrdersController.Get');
    expect(text).toContain('OrderService.GetAsync');
    expect(text).toContain('+20.0 ms');
    expect(fixture.nativeElement.querySelectorAll('.flow-waterfall__row')).toHaveLength(0);
  });
});
