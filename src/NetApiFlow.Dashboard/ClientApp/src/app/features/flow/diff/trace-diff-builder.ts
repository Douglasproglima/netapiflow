import { ComponentType, FlowSpan, FlowTraceDetail, NodeVisualState } from '../../../core/models';
import { spanGroupKey, stateFromSpanStatus } from '../graph/graph-builder';

export type DiffRowKind = 'matched' | 'onlyInA' | 'onlyInB';

export interface DiffRow {
  key: string;
  component: string;
  operation: string;
  type: ComponentType;
  kind: DiffRowKind;
  /** How many spans of this key execution A has (0 when `onlyInB`); N+1 repeats are summed, not listed. */
  countA: number;
  countB: number;
  durationMicrosA: number | null;
  durationMicrosB: number | null;
  /** `durationMicrosB - durationMicrosA`, only when `matched` and both sides have a duration. */
  deltaMicros: number | null;
  /** `not-executed` on the side that's missing this key (seção 7.2 do prompt: cor + ícone + texto). */
  stateA: NodeVisualState;
  stateB: NodeVisualState;
}

export interface TraceDiffInput {
  traceA: FlowTraceDetail;
  traceB: FlowTraceDetail;
  slowThresholdMicros: number;
}

interface SpanGroup {
  component: string;
  operation: string;
  type: ComponentType;
  spans: FlowSpan[];
  minSequence: number;
}

function groupByKey(spans: FlowSpan[]): Map<string, SpanGroup> {
  const groups = new Map<string, SpanGroup>();

  for (const span of spans) {
    const key = spanGroupKey(span.component, span.operation);
    const existing = groups.get(key);
    if (existing) {
      existing.spans.push(span);
      existing.minSequence = Math.min(existing.minSequence, span.sequence);
      continue;
    }

    groups.set(key, {
      component: span.component,
      operation: span.operation,
      type: span.type,
      spans: [span],
      minSequence: span.sequence,
    });
  }

  return groups;
}

function aggregatedDuration(spans: FlowSpan[]): number | null {
  if (spans.every((span) => span.durationMicros === null)) {
    return null;
  }

  return spans.reduce((sum, span) => sum + (span.durationMicros ?? 0), 0);
}

function aggregatedState(spans: FlowSpan[], slowThresholdMicros: number): NodeVisualState {
  if (spans.some((span) => span.status === 'Error')) {
    return 'error';
  }

  if (spans.some((span) => span.status === 'Running')) {
    return 'running';
  }

  return stateFromSpanStatus('Success', aggregatedDuration(spans), slowThresholdMicros);
}

/**
 * Pure function: matches the spans of two executions by `spanGroupKey` (graph-builder.ts) — the
 * same key `findNPlusOneGroup` and the blueprint's "not-executed" node already use, never a
 * `spanId` (unique per execution, never repeats across two different runs). A key repeated within
 * one execution (N+1) is summed into a single row, never listed as separate rows.
 */
export function buildTraceDiff(input: TraceDiffInput): DiffRow[] {
  const groupsA = groupByKey(input.traceA.spans);
  const groupsB = groupByKey(input.traceB.spans);
  const keys = new Set([...groupsA.keys(), ...groupsB.keys()]);

  const rowsWithSortKey = Array.from(keys, (key) => {
    const groupA = groupsA.get(key);
    const groupB = groupsB.get(key);
    const reference = (groupA ?? groupB) as SpanGroup;

    const durationMicrosA = groupA ? aggregatedDuration(groupA.spans) : null;
    const durationMicrosB = groupB ? aggregatedDuration(groupB.spans) : null;
    const kind: DiffRowKind = groupA && groupB ? 'matched' : groupA ? 'onlyInA' : 'onlyInB';

    const row: DiffRow = {
      key,
      component: reference.component,
      operation: reference.operation,
      type: reference.type,
      kind,
      countA: groupA?.spans.length ?? 0,
      countB: groupB?.spans.length ?? 0,
      durationMicrosA,
      durationMicrosB,
      deltaMicros:
        kind === 'matched' && durationMicrosA !== null && durationMicrosB !== null
          ? durationMicrosB - durationMicrosA
          : null,
      stateA: groupA ? aggregatedState(groupA.spans, input.slowThresholdMicros) : 'not-executed',
      stateB: groupB ? aggregatedState(groupB.spans, input.slowThresholdMicros) : 'not-executed',
    };

    return {
      row,
      sortKey: Math.min(groupA?.minSequence ?? Infinity, groupB?.minSequence ?? Infinity),
    };
  });

  rowsWithSortKey.sort((a, b) => a.sortKey - b.sortKey);
  return rowsWithSortKey.map(({ row }) => row);
}
