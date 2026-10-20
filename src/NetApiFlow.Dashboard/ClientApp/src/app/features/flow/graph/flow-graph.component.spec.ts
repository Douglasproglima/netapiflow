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

    const sequenceByComponent = new Map<string, string>();
    for (const nodeElement of Array.from(nodeElements)) {
      const component = (nodeElement as Element).querySelector(
        '.flow-graph__node-component',
      )?.textContent;
      const sequence = (nodeElement as Element).querySelector(
        '.flow-graph__node-sequence',
      )?.textContent;
      if (component && sequence !== undefined) {
        sequenceByComponent.set(component, sequence ?? '');
      }
    }

    expect(sequenceByComponent.get('OrdersController')).toBe('0');
    expect(sequenceByComponent.get('OrderService')).toBe('1');
    expect(sequenceByComponent.get('OrderRepository')).toBe('2');
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
});
