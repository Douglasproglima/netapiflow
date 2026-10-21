import {
  AfterViewInit,
  Component,
  ElementRef,
  computed,
  input,
  output,
  viewChild,
} from '@angular/core';
import { select } from 'd3-selection';
import { zoom, zoomIdentity, D3ZoomEvent } from 'd3-zoom';
import { GraphEdge, GraphNode } from './graph-builder';
import { NODE_STATE_PRESENTATION } from '../../../shared/node-state-presentation';
import { ComponentTypeIcon, componentTypeIcon } from '../../../shared/component-type-icon';
import { formatDurationMicros } from '../../../shared/duration-format';

/**
 * Renders the output of graph-builder as plain SVG (seção 4.2 do prompt: no ready-made
 * graph-rendering library, dagre is layout-only). Zoom and pan via d3-zoom.
 */
@Component({
  selector: 'app-flow-graph',
  templateUrl: './flow-graph.component.html',
  styleUrl: './flow-graph.component.scss',
})
export class FlowGraphComponent implements AfterViewInit {
  readonly nodes = input.required<GraphNode[]>();
  readonly edges = input.required<GraphEdge[]>();
  readonly selectedNodeId = input<string | null>(null);

  readonly nodeSelected = output<string>();

  private readonly svgRef = viewChild.required<ElementRef<SVGSVGElement>>('svg');
  private readonly zoomLayerRef = viewChild.required<ElementRef<SVGGElement>>('zoomLayer');

  readonly viewBox = computed(() => {
    const allNodes = this.nodes();
    if (allNodes.length === 0) {
      return '0 0 100 100';
    }

    const maxX = Math.max(...allNodes.map((node) => node.x + node.width / 2)) + 40;
    const maxY = Math.max(...allNodes.map((node) => node.y + node.height / 2)) + 40;
    return `0 0 ${maxX} ${maxY}`;
  });

  private zoomInitialized = false;

  ngAfterViewInit(): void {
    if (this.zoomInitialized) {
      return;
    }

    this.zoomInitialized = true;
    const svgElement = this.svgRef().nativeElement;
    const zoomLayerElement = this.zoomLayerRef().nativeElement;

    const behavior = zoom<SVGSVGElement, unknown>()
      .scaleExtent([0.25, 4])
      .on('zoom', (event: D3ZoomEvent<SVGSVGElement, unknown>) => {
        select(zoomLayerElement).attr('transform', event.transform.toString());
      });

    select(svgElement).call(behavior).call(behavior.transform, zoomIdentity);
  }

  nodeTransform(node: GraphNode): string {
    return `translate(${node.x - node.width / 2}, ${node.y - node.height / 2})`;
  }

  edgePath(edge: GraphEdge): string {
    if (edge.points.length === 0) {
      return '';
    }

    return edge.points
      .map((point, index) => `${index === 0 ? 'M' : 'L'}${point.x},${point.y}`)
      .join(' ');
  }

  onNodeClick(node: GraphNode): void {
    this.nodeSelected.emit(node.id);
  }

  stateIconSrc(node: GraphNode): string {
    return NODE_STATE_PRESENTATION[node.state].iconSrc;
  }

  typeIcon(node: GraphNode): ComponentTypeIcon {
    return componentTypeIcon(node.type);
  }

  stateLabel(node: GraphNode): string {
    return NODE_STATE_PRESENTATION[node.state].label;
  }

  nodeAriaLabel(node: GraphNode): string {
    const duration =
      node.durationMicros === null ? '' : `, ${formatDurationMicros(node.durationMicros)}`;
    return `${node.component}, ${node.operation}, ${this.stateLabel(node)}${duration}`;
  }

  nodeDuration(node: GraphNode): string {
    return node.durationMicros === null ? '' : formatDurationMicros(node.durationMicros);
  }

  // `.flow-graph__node-operation` is 10.5px monospace, so every character has the same advance
  // width — no need for canvas-based text measurement to decide where it would overlap the
  // duration label that shares its baseline.
  private static readonly MONO_CHAR_WIDTH_PX = 6.3;
  private static readonly NODE_LEFT_INSET_PX = 48;
  private static readonly NODE_RIGHT_MARGIN_PX = 10;
  private static readonly DURATION_GAP_PX = 8;

  nodeOperationLabel(node: GraphNode): string {
    const duration = this.nodeDuration(node);
    const durationWidth = duration
      ? duration.length * FlowGraphComponent.MONO_CHAR_WIDTH_PX + FlowGraphComponent.DURATION_GAP_PX
      : 0;
    const availablePx =
      node.width -
      FlowGraphComponent.NODE_LEFT_INSET_PX -
      FlowGraphComponent.NODE_RIGHT_MARGIN_PX -
      durationWidth;
    const maxChars = Math.max(4, Math.floor(availablePx / FlowGraphComponent.MONO_CHAR_WIDTH_PX));

    return node.operation.length <= maxChars
      ? node.operation
      : `${node.operation.slice(0, maxChars - 1)}…`;
  }

  /** Edge color follows the target node's state (guide seção 6.4: "cor do estado do nó de destino"). */
  nodeStateOf(nodeId: string): string {
    return this.nodes().find((node) => node.id === nodeId)?.state ?? 'waiting';
  }

  /** The sequence pill renders on the incoming edge (guide seção 6.4), not inside the node itself. */
  edgeSequence(edge: GraphEdge): number | null {
    return this.nodes().find((node) => node.id === edge.targetId)?.sequence ?? null;
  }

  edgeMidpoint(edge: GraphEdge): { x: number; y: number } {
    const points = edge.points;
    if (points.length === 0) {
      return { x: 0, y: 0 };
    }

    const mid = points[Math.floor(points.length / 2)];
    return mid;
  }
}
