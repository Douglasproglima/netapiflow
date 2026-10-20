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
}
