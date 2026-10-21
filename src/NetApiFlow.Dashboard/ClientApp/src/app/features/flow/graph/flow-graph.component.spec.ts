import { TestBed } from '@angular/core/testing';
import { FlowGraphComponent } from './flow-graph.component';
import { buildGraph } from './graph-builder';
import { ComponentType, FlowSpan, FlowStatus } from '../../../core/models';

function span(
  overrides: Partial<FlowSpan> &
    Pick<FlowSpan, 'spanId' | 'parentSpanId' | 'sequence' | 'component' | 'operation'>,
): FlowSpan {
  return {
    type: 'Service' as ComponentType,
    startedAt: '2026-01-01T00:00:00.000000Z',
    endedAt: '2026-01-01T00:00:00.100000Z',
    durationMicros: 100_000,
    status: 'Success' as FlowStatus,
    errorType: null,
    errorMessage: null,
    isErrorOrigin: false,
    tags: {},
    ...overrides,
  };
}

describe('FlowGraphComponent', () => {
  it('renders one SVG group per node and one path per edge, each with the right start order', async () => {
    const spans: FlowSpan[] = [
      span({
        spanId: 'root',
        parentSpanId: null,
        sequence: 0,
        component: 'OrdersController',
        operation: 'Get',
      }),
      span({
        spanId: 'mid',
        parentSpanId: 'root',
        sequence: 1,
        component: 'OrderService',
        operation: 'GetAsync',
      }),
      span({
        spanId: 'leaf',
        parentSpanId: 'mid',
        sequence: 2,
        component: 'OrderRepository',
        operation: 'FindAsync',
      }),
    ];
    const { nodes, edges } = buildGraph({
      spans,
      traceStatus: 'Success',
      slowThresholdMicros: 250_000,
    });

    const fixture = TestBed.createComponent(FlowGraphComponent);
    fixture.componentRef.setInput('nodes', nodes);
    fixture.componentRef.setInput('edges', edges);
    await fixture.whenStable();

    const nodeElements = fixture.nativeElement.querySelectorAll('.flow-graph__node');
    const edgeElements = fixture.nativeElement.querySelectorAll('.flow-graph__edge');

    expect(nodeElements.length).toBe(3);
    expect(edgeElements.length).toBe(2);

    // The sequence pill renders on the incoming edge, not inside the node (guide seção 6.4) — the
    // root has no incoming edge, so only the two non-root nodes get a pill.
    const edgeSequenceElements = fixture.nativeElement.querySelectorAll(
      '.flow-graph__edge-sequence text',
    );
    const sequenceTexts = Array.from(edgeSequenceElements).map((el) => (el as Element).textContent);

    expect(sequenceTexts).toEqual(['#1', '#2']);
  });

  it('emits nodeSelected with the clicked node id', async () => {
    const spans: FlowSpan[] = [
      span({ spanId: 'root', parentSpanId: null, sequence: 0, component: 'C', operation: 'Op' }),
    ];
    const { nodes, edges } = buildGraph({
      spans,
      traceStatus: 'Success',
      slowThresholdMicros: 250_000,
    });

    const fixture = TestBed.createComponent(FlowGraphComponent);
    fixture.componentRef.setInput('nodes', nodes);
    fixture.componentRef.setInput('edges', edges);
    await fixture.whenStable();

    let selectedId: string | null = null;
    fixture.componentInstance.nodeSelected.subscribe((id) => (selectedId = id));

    const nodeElement: HTMLElement = fixture.nativeElement.querySelector('.flow-graph__node');
    nodeElement.dispatchEvent(new MouseEvent('click', { bubbles: true }));

    expect(selectedId).toBe('root');
  });

  it('keeps the six visual states distinguishable using only text and aria-label, color removed', async () => {
    const realStatesSpans: FlowSpan[] = [
      span({
        spanId: 'completed',
        parentSpanId: null,
        sequence: 0,
        component: 'C',
        operation: 'Completed',
        status: 'Success',
        durationMicros: 1_000,
      }),
    ];
    const slowSpans: FlowSpan[] = [
      span({
        spanId: 'slow',
        parentSpanId: null,
        sequence: 0,
        component: 'C',
        operation: 'Slow',
        status: 'Success',
        durationMicros: 999_999,
      }),
    ];
    const errorSpans: FlowSpan[] = [
      span({
        spanId: 'error',
        parentSpanId: null,
        sequence: 0,
        component: 'C',
        operation: 'Error',
        status: 'Error',
      }),
    ];
    const runningSpans: FlowSpan[] = [
      span({
        spanId: 'running',
        parentSpanId: null,
        sequence: 0,
        component: 'C',
        operation: 'Running',
        status: 'Running',
        endedAt: null,
        durationMicros: null,
      }),
    ];
    const waitingReal: FlowSpan[] = [
      span({
        spanId: 'wait-root',
        parentSpanId: null,
        sequence: 0,
        component: 'C',
        operation: 'Root',
        status: 'Running',
        endedAt: null,
        durationMicros: null,
      }),
    ];
    const waitingBlueprint: FlowSpan[] = [
      span({
        spanId: 'bp-wait-root',
        parentSpanId: null,
        sequence: 0,
        component: 'C',
        operation: 'Root',
      }),
      span({
        spanId: 'bp-waiting',
        parentSpanId: 'bp-wait-root',
        sequence: 1,
        component: 'C',
        operation: 'Waiting',
      }),
    ];
    const notExecutedReal: FlowSpan[] = [
      span({
        spanId: 'ne-root',
        parentSpanId: null,
        sequence: 0,
        component: 'C',
        operation: 'Root',
      }),
    ];
    const notExecutedBlueprint: FlowSpan[] = [
      span({
        spanId: 'bp-ne-root',
        parentSpanId: null,
        sequence: 0,
        component: 'C',
        operation: 'Root',
      }),
      span({
        spanId: 'bp-ne',
        parentSpanId: 'bp-ne-root',
        sequence: 1,
        component: 'C',
        operation: 'NotExecuted',
      }),
    ];

    const graphs = [
      buildGraph({ spans: realStatesSpans, traceStatus: 'Success', slowThresholdMicros: 250_000 }),
      buildGraph({ spans: slowSpans, traceStatus: 'Success', slowThresholdMicros: 250_000 }),
      buildGraph({ spans: errorSpans, traceStatus: 'Error', slowThresholdMicros: 250_000 }),
      buildGraph({ spans: runningSpans, traceStatus: 'Running', slowThresholdMicros: 250_000 }),
      buildGraph({
        spans: waitingReal,
        traceStatus: 'Running',
        slowThresholdMicros: 250_000,
        blueprintSpans: waitingBlueprint,
      }),
      buildGraph({
        spans: notExecutedReal,
        traceStatus: 'Success',
        slowThresholdMicros: 250_000,
        blueprintSpans: notExecutedBlueprint,
      }),
    ];

    const observedSignatures = new Set<string>();

    for (const graph of graphs) {
      const fixture = TestBed.createComponent(FlowGraphComponent);
      fixture.componentRef.setInput('nodes', graph.nodes);
      fixture.componentRef.setInput('edges', graph.edges);
      await fixture.whenStable();

      // Only the state-under-test node matters here (the "Root"/ghost scaffolding spans share
      // the same graph); it is the one whose operation name names the state being tested.
      const relevantOperations = new Set([
        'Completed',
        'Slow',
        'Error',
        'Running',
        'Waiting',
        'NotExecuted',
      ]);
      const nodeElements: Element[] = Array.from(
        fixture.nativeElement.querySelectorAll('.flow-graph__node'),
      );

      for (const nodeElement of nodeElements) {
        const operation = nodeElement.querySelector('.flow-graph__node-operation')?.textContent;
        if (!operation || !relevantOperations.has(operation)) {
          continue;
        }

        const stateText = nodeElement.querySelector('.flow-graph__node-state')?.textContent ?? '';
        const ariaLabel = nodeElement.getAttribute('aria-label') ?? '';
        // Signature built only from non-color attributes: text content and aria-label.
        observedSignatures.add(`${stateText}|${ariaLabel}`);
      }
    }

    expect(observedSignatures.size).toBe(6);
  });
});
