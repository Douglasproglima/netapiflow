import * as dagre from '@dagrejs/dagre';
import { ArchitectureMap } from '../../core/models';
import { GraphEdge, GraphNode, GraphResult } from '../flow/graph/graph-builder';

const NODE_WIDTH = 184;
const NODE_MAX_WIDTH = 320;
const NODE_HEIGHT = 60;
const NODE_LEFT_INSET_PX = 48;
const NODE_RIGHT_MARGIN_PX = 10;
// Same per-character approximation as graph-builder.ts: the component name is proportional
// sans-serif, the operation label is monospace, both rendered by the unmodified flow-graph
// component (D-83: the SVG renderer is reused as-is).
const COMPONENT_CHAR_WIDTH_PX = 6.8;
const MONO_CHAR_WIDTH_PX = 6.3;

function nodeWidthFor(component: string, operationLabel: string): number {
  const operationWidth =
    NODE_LEFT_INSET_PX + operationLabel.length * MONO_CHAR_WIDTH_PX + NODE_RIGHT_MARGIN_PX;
  const componentWidth =
    NODE_LEFT_INSET_PX + component.length * COMPONENT_CHAR_WIDTH_PX + NODE_RIGHT_MARGIN_PX;

  return Math.min(NODE_MAX_WIDTH, Math.max(NODE_WIDTH, operationWidth, componentWidth));
}

function nodeId(component: string, operation: string): string {
  return `${component}::${operation}`;
}

/** `node.operation` carries its own occurrence percentage, embedded in the label text (D-83): no new field on `GraphNode`, no change to flow-graph.component. */
function operationLabelFor(
  executionCount: number,
  totalExecutions: number,
  operation: string,
): string {
  if (executionCount >= totalExecutions) {
    return operation;
  }

  const percentage = Math.round((executionCount / totalExecutions) * 100);
  return `${operation} (${percentage}%)`;
}

/**
 * Pure function: an `ArchitectureMap` in, a laid-out graph out, in the same `GraphResult` shape
 * `flow-graph.component` already renders (D-83: the renderer is reused unmodified). Unlike
 * `buildGraph`, there is no single trace: `sequence`, `durationMicros` and `nPlusOneCount` have no
 * equivalent in an aggregated map, so every node carries `null` for them and a uniform
 * `'completed'` state.
 */
export function buildArchitectureGraph(map: ArchitectureMap): GraphResult {
  if (map.nodes.length === 0) {
    return { nodes: [], edges: [] };
  }

  const layoutGraph = new dagre.graphlib.Graph();
  layoutGraph.setGraph({ rankdir: 'LR', nodesep: 16, ranksep: 44, marginx: 24, marginy: 24 });
  layoutGraph.setDefaultEdgeLabel(() => ({}));

  const operationLabelById = new Map<string, string>();

  for (const node of map.nodes) {
    const id = nodeId(node.component, node.operation);
    const operationLabel = operationLabelFor(
      node.executionCount,
      map.totalExecutions,
      node.operation,
    );
    operationLabelById.set(id, operationLabel);
    layoutGraph.setNode(id, {
      width: nodeWidthFor(node.component, operationLabel),
      height: NODE_HEIGHT,
    });
  }

  for (const edge of map.edges) {
    layoutGraph.setEdge(
      nodeId(edge.fromComponent, edge.fromOperation),
      nodeId(edge.toComponent, edge.toOperation),
    );
  }

  dagre.layout(layoutGraph);

  const nodes: GraphNode[] = map.nodes.map((node) => {
    const id = nodeId(node.component, node.operation);
    const position = layoutGraph.node(id);
    return {
      id,
      component: node.component,
      operation: operationLabelById.get(id)!,
      type: node.type,
      sequence: null,
      state: 'completed',
      durationMicros: null,
      nPlusOneCount: null,
      x: position.x,
      y: position.y,
      width: position.width,
      height: position.height,
    };
  });

  const edges: GraphEdge[] = map.edges.map((edge) => {
    const sourceId = nodeId(edge.fromComponent, edge.fromOperation);
    const targetId = nodeId(edge.toComponent, edge.toOperation);
    return {
      id: `${sourceId}->${targetId}`,
      sourceId,
      targetId,
      points: layoutGraph.edge(sourceId, targetId)?.points ?? [],
    };
  });

  return { nodes, edges };
}
