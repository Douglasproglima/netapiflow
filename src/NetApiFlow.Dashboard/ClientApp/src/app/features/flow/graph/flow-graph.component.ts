import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { select, Selection } from 'd3-selection';
import { zoom, zoomIdentity, D3ZoomEvent, ZoomBehavior } from 'd3-zoom';
import { GraphEdge, GraphNode } from './graph-builder';
import { ContainerSize, computeGraphViewBox } from './flow-graph-viewbox';
import { NODE_STATE_PRESENTATION } from '../../../shared/node-state-presentation';
import { ComponentTypeIcon, componentTypeIcon } from '../../../shared/component-type-icon';
import { formatDurationMicros } from '../../../shared/duration-format';
import { LanguageService } from '../../../core/i18n/language.service';
import { NodeVisualState } from '../../../core/models';

const ZOOM_STEP_FACTOR = 1.3;

/**
 * Renders the output of graph-builder as plain SVG (seção 4.2 do prompt: no ready-made
 * graph-rendering library, dagre is layout-only). Zoom and pan via d3-zoom.
 */
@Component({
  selector: 'app-flow-graph',
  templateUrl: './flow-graph.component.html',
  styleUrl: './flow-graph.component.scss',
})
export class FlowGraphComponent implements AfterViewInit, OnDestroy {
  private readonly language = inject(LanguageService);
  private readonly hostElementRef = inject(ElementRef<HTMLElement>);

  readonly nodes = input.required<GraphNode[]>();
  readonly edges = input.required<GraphEdge[]>();
  readonly selectedNodeId = input<string | null>(null);

  readonly nodeSelected = output<string>();

  private readonly svgRef = viewChild.required<ElementRef<SVGSVGElement>>('svg');
  private readonly zoomLayerRef = viewChild.required<ElementRef<SVGGElement>>('zoomLayer');
  private readonly legendRef = viewChild.required<ElementRef<HTMLDivElement>>('legend');
  private readonly zoomControlsRef = viewChild.required<ElementRef<HTMLDivElement>>('zoomControls');

  private readonly containerSize = signal<ContainerSize>({ width: 0, height: 0 });
  private readonly overlayPanelHeights = signal<readonly number[]>([]);
  private overlayResizeObserver: ResizeObserver | null = null;

  readonly viewBox = computed(() => {
    const allNodes = this.nodes();
    if (allNodes.length === 0) {
      return '0 0 100 100';
    }

    const contentWidth = Math.max(...allNodes.map((node) => node.x + node.width / 2));
    const contentHeight = Math.max(...allNodes.map((node) => node.y + node.height / 2));
    return computeGraphViewBox(
      contentWidth,
      contentHeight,
      this.containerSize(),
      this.overlayPanelHeights(),
    );
  });

  private zoomInitialized = false;
  private svgSelection: Selection<SVGSVGElement, unknown, null, undefined> | null = null;
  private zoomBehavior: ZoomBehavior<SVGSVGElement, unknown> | null = null;

  private readonly zoomLevel = signal(1);
  readonly zoomPercentage = computed(() => Math.round(this.zoomLevel() * 100));

  readonly legendStates: readonly NodeVisualState[] = Object.keys(
    NODE_STATE_PRESENTATION,
  ) as NodeVisualState[];

  constructor() {
    effect(() => {
      this.viewBox();
      this.zoomToFit();
    });
  }

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
        this.zoomLevel.set(event.transform.k);
      });

    this.svgSelection = select(svgElement);
    this.zoomBehavior = behavior;
    this.svgSelection.call(behavior).call(behavior.transform, zoomIdentity);

    this.observeOverlaySizes();
  }

  ngOnDestroy(): void {
    this.overlayResizeObserver?.disconnect();
  }

  private observeOverlaySizes(): void {
    if (typeof ResizeObserver === 'undefined') {
      return;
    }

    const hostElement = this.hostElementRef.nativeElement;
    const legendElement = this.legendRef().nativeElement;
    const zoomControlsElement = this.zoomControlsRef().nativeElement;

    const measure = (): void => {
      this.containerSize.set({ width: hostElement.clientWidth, height: hostElement.clientHeight });
      this.overlayPanelHeights.set([legendElement.clientHeight, zoomControlsElement.clientHeight]);
    };

    this.overlayResizeObserver = new ResizeObserver(measure);
    this.overlayResizeObserver.observe(hostElement);
    this.overlayResizeObserver.observe(legendElement);
    this.overlayResizeObserver.observe(zoomControlsElement);
  }

  zoomToFit(): void {
    if (this.svgSelection && this.zoomBehavior) {
      this.svgSelection.call(this.zoomBehavior.transform, zoomIdentity);
    }
  }

  zoomIn(): void {
    if (this.svgSelection && this.zoomBehavior) {
      this.zoomBehavior.scaleBy(this.svgSelection, ZOOM_STEP_FACTOR);
    }
  }

  zoomOut(): void {
    if (this.svgSelection && this.zoomBehavior) {
      this.zoomBehavior.scaleBy(this.svgSelection, 1 / ZOOM_STEP_FACTOR);
    }
  }

  legendIconSrc(state: NodeVisualState): string {
    return NODE_STATE_PRESENTATION[state].iconSrc;
  }

  legendLabel(state: NodeVisualState): string {
    return this.language.translate(NODE_STATE_PRESENTATION[state].labelKey);
  }

  zoomInAriaLabel(): string {
    return this.language.translate('graph.zoomIn');
  }

  zoomOutAriaLabel(): string {
    return this.language.translate('graph.zoomOut');
  }

  zoomToFitAriaLabel(): string {
    return this.language.translate('graph.zoomToFit');
  }

  legendAriaLabel(): string {
    return this.language.translate('graph.legend.aria');
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
    return this.language.translate(NODE_STATE_PRESENTATION[node.state].labelKey);
  }

  nodeAriaLabel(node: GraphNode): string {
    const duration =
      node.durationMicros === null ? '' : `, ${formatDurationMicros(node.durationMicros)}`;
    const nPlusOne =
      node.nPlusOneCount === null ? '' : `, ${this.nPlusOneAriaLabel(node.nPlusOneCount)}`;
    return `${node.component}, ${node.operation}, ${this.stateLabel(node)}${duration}${nPlusOne}`;
  }

  nPlusOneBadgeLabel(count: number): string {
    return this.language.translate('node.nPlusOneBadge', { count });
  }

  nPlusOneAriaLabel(count: number): string {
    return this.language.translate('node.nPlusOneAria', { count });
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
