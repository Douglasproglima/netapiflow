import * as dagre from '@dagrejs/dagre';
import { ComponentType, FlowSpan, FlowStatus } from '../../../core/models';

/**
 * The six visual states of seção 7.2 do prompt. Ghost nodes (inferred from the blueprint, never
 * actually executed) are "waiting" while the trace is still running, "not-executed" once it has
 * concluded — there is no way to know a missing branch will never run until the trace is done.
 */
export type NodeVisualState =
  'waiting' | 'running' | 'completed' | 'slow' | 'error' | 'not-executed';

export interface GraphNode {
  id: string;
  component: string;
  operation: string;
  type: ComponentType;
  /** Start order within the real trace, or `null` for a ghost (never-executed) node. */
  sequence: number | null;
  state: NodeVisualState;
  durationMicros: number | null;
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface GraphEdge {
  id: string;
  sourceId: string;
  targetId: string;
  points: { x: number; y: number }[];
}

export interface GraphResult {
  nodes: GraphNode[];
  edges: GraphEdge[];
}

export interface GraphBuilderInput {
  spans: FlowSpan[];
  traceStatus: FlowStatus;
  slowThresholdMicros: number;
  /** Spans of the last successful execution of the same endpoint, used to infer missing steps. */
  blueprintSpans?: FlowSpan[];
}

const NODE_WIDTH = 180;
const NODE_HEIGHT = 56;
const GHOST_ID_PREFIX = 'ghost-';

interface MergedNode {
  id: string;
  component: string;
  operation: string;
  type: ComponentType;
  sequence: number | null;
  /** `null` marks a ghost node: present in the blueprint, absent from the real spans. */
  status: FlowStatus | null;
  durationMicros: number | null;
  children: MergedNode[];
}

/**
 * Pure function: spans in, a laid-out graph out. No Angular, no HTTP, no DOM — `@dagrejs/dagre`
 * computes node positions only, never renders anything (seção 4.2 do prompt forbids a ready-made
 * graph-rendering library; the SVG component, tarefa 4.4, owns rendering).
 */
export function buildGraph(input: GraphBuilderInput): GraphResult {
  const tree = buildMergedTree(input);
  if (!tree) {
    return { nodes: [], edges: [] };
  }

  const layoutGraph = new dagre.graphlib.Graph();
  layoutGraph.setGraph({ rankdir: 'TB', nodesep: 40, ranksep: 60 });
  layoutGraph.setDefaultEdgeLabel(() => ({}));

  const flatNodes: MergedNode[] = [];
  const flatEdges: { parentId: string; childId: string }[] = [];

  const visit = (node: MergedNode, parentId: string | null): void => {
    flatNodes.push(node);
    layoutGraph.setNode(node.id, { width: NODE_WIDTH, height: NODE_HEIGHT });
    if (parentId !== null) {
      layoutGraph.setEdge(parentId, node.id);
      flatEdges.push({ parentId, childId: node.id });
    }

    for (const child of node.children) {
      visit(child, node.id);
    }
  };

  visit(tree, null);
  dagre.layout(layoutGraph);

  const nodes: GraphNode[] = flatNodes.map((node) => {
    const position = layoutGraph.node(node.id);
    return {
      id: node.id,
      component: node.component,
      operation: node.operation,
      type: node.type,
      sequence: node.sequence,
      state: resolveState(node, input),
      durationMicros: node.durationMicros,
      x: position.x,
      y: position.y,
      width: position.width,
      height: position.height,
    };
  });

  const edges: GraphEdge[] = flatEdges.map(({ parentId, childId }) => ({
    id: `${parentId}->${childId}`,
    sourceId: parentId,
    targetId: childId,
    points: layoutGraph.edge(parentId, childId)?.points ?? [],
  }));

  return { nodes, edges };
}

function resolveState(node: MergedNode, input: GraphBuilderInput): NodeVisualState {
  if (node.status === null) {
    return input.traceStatus === 'Running' ? 'waiting' : 'not-executed';
  }

  if (node.status === 'Error') {
    return 'error';
  }

  if (node.status === 'Running') {
    return 'running';
  }

  if (node.durationMicros !== null && node.durationMicros >= input.slowThresholdMicros) {
    return 'slow';
  }

  return 'completed';
}

function buildChildrenByParent(spans: FlowSpan[]): Map<string | null, FlowSpan[]> {
  const byParent = new Map<string | null, FlowSpan[]>();
  for (const span of spans) {
    const children = byParent.get(span.parentSpanId) ?? [];
    children.push(span);
    byParent.set(span.parentSpanId, children);
  }

  for (const children of byParent.values()) {
    children.sort((a, b) => a.sequence - b.sequence);
  }

  return byParent;
}

function buildMergedTree(input: GraphBuilderInput): MergedNode | null {
  const realChildrenByParent = buildChildrenByParent(input.spans);
  const blueprintChildrenByParent = buildChildrenByParent(input.blueprintSpans ?? []);

  const realRoot = input.spans.find((span) => span.parentSpanId === null);
  const blueprintRoot = (input.blueprintSpans ?? []).find((span) => span.parentSpanId === null);

  if (realRoot) {
    return mergeNode(realRoot, realChildrenByParent, blueprintRoot, blueprintChildrenByParent);
  }

  if (blueprintRoot) {
    return buildGhostSubtree(blueprintRoot, blueprintChildrenByParent);
  }

  return null;
}

function mergeNode(
  realSpan: FlowSpan,
  realChildrenByParent: Map<string | null, FlowSpan[]>,
  blueprintSpan: FlowSpan | undefined,
  blueprintChildrenByParent: Map<string | null, FlowSpan[]>,
): MergedNode {
  const realChildren = realChildrenByParent.get(realSpan.spanId) ?? [];
  const blueprintChildren = blueprintSpan
    ? (blueprintChildrenByParent.get(blueprintSpan.spanId) ?? [])
    : [];

  const matchedBlueprintIds = new Set<string>();
  const children: MergedNode[] = [];

  for (const realChild of realChildren) {
    const match = blueprintChildren.find(
      (candidate) =>
        !matchedBlueprintIds.has(candidate.spanId) &&
        candidate.component === realChild.component &&
        candidate.operation === realChild.operation,
    );

    if (match) {
      matchedBlueprintIds.add(match.spanId);
    }

    children.push(mergeNode(realChild, realChildrenByParent, match, blueprintChildrenByParent));
  }

  for (const blueprintChild of blueprintChildren) {
    if (!matchedBlueprintIds.has(blueprintChild.spanId)) {
      children.push(buildGhostSubtree(blueprintChild, blueprintChildrenByParent));
    }
  }

  return {
    id: realSpan.spanId,
    component: realSpan.component,
    operation: realSpan.operation,
    type: realSpan.type,
    sequence: realSpan.sequence,
    status: realSpan.status,
    durationMicros: realSpan.durationMicros,
    children,
  };
}

function buildGhostSubtree(
  blueprintSpan: FlowSpan,
  blueprintChildrenByParent: Map<string | null, FlowSpan[]>,
): MergedNode {
  const children = (blueprintChildrenByParent.get(blueprintSpan.spanId) ?? []).map((child) =>
    buildGhostSubtree(child, blueprintChildrenByParent),
  );

  return {
    id: `${GHOST_ID_PREFIX}${blueprintSpan.spanId}`,
    component: blueprintSpan.component,
    operation: blueprintSpan.operation,
    type: blueprintSpan.type,
    sequence: null,
    status: null,
    durationMicros: null,
    children,
  };
}
