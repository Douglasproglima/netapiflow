import { ArchitectureMap } from '../../core/models';

function escapeLabel(label: string): string {
  return label.replace(/"/g, '#quot;');
}

/**
 * Pure function: an `ArchitectureMap` in, Mermaid `flowchart TD` text out (D-83, client-side
 * export, no new HTTP call). Node ids are assigned in encounter order, so edges always resolve to
 * an id already declared above them.
 */
export function architectureMapToMermaid(map: ArchitectureMap): string {
  const idByKey = new Map<string, string>();

  const idFor = (component: string, operation: string): string => {
    const key = `${component}::${operation}`;
    let id = idByKey.get(key);
    if (id === undefined) {
      id = `n${idByKey.size}`;
      idByKey.set(key, id);
    }

    return id;
  };

  const lines = ['flowchart TD'];

  for (const node of map.nodes) {
    const id = idFor(node.component, node.operation);
    const label =
      node.executionCount >= map.totalExecutions
        ? `${node.component}: ${node.operation}`
        : `${node.component}: ${node.operation} (${Math.round((node.executionCount / map.totalExecutions) * 100)}%)`;
    lines.push(`  ${id}["${escapeLabel(label)}"]`);
  }

  for (const edge of map.edges) {
    const sourceId = idFor(edge.fromComponent, edge.fromOperation);
    const targetId = idFor(edge.toComponent, edge.toOperation);
    lines.push(`  ${sourceId} --> ${targetId}`);
  }

  return lines.join('\n');
}
