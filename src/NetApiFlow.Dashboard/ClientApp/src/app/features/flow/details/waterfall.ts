import { FlowStatus, FlowTraceDetail, NodeVisualState } from '../../../core/models';
import { stateFromSpanStatus } from '../graph/graph-builder';

export interface WaterfallRow {
  spanId: string;
  component: string;
  operation: string;
  status: FlowStatus;
  state: NodeVisualState;
  durationMicros: number | null;
  /** Percentage of the trace's total width, from its start. */
  offsetPercent: number;
  /** Percentage of the trace's total width this span's bar occupies. */
  widthPercent: number;
}

/**
 * Pure function: spans ordered by start, each with a bar offset/width proportional to the
 * trace's total duration. Positions come from parsing ISO timestamps (millisecond precision,
 * fine for a visual proportion); the displayed duration text always comes straight from
 * `durationMicros`, never derived from these parsed timestamps (seção 7.3 do prompt). Each row's
 * `state` drives the bar's color (guide seção 6.6), same six-state vocabulary as the graph.
 */
export function buildWaterfall(
  trace: FlowTraceDetail,
  slowThresholdMicros: number,
): WaterfallRow[] {
  if (trace.spans.length === 0) {
    return [];
  }

  const traceStartMs = Date.parse(trace.startedAt);
  const sorted = [...trace.spans].sort((a, b) => Date.parse(a.startedAt) - Date.parse(b.startedAt));

  const fallbackEndMs = Math.max(
    ...sorted.map((span) => Date.parse(span.endedAt ?? span.startedAt) - traceStartMs),
    1,
  );
  const totalDurationMs =
    trace.durationMicros !== null ? trace.durationMicros / 1_000 : fallbackEndMs;

  return sorted.map((span) => {
    const startOffsetMs = Date.parse(span.startedAt) - traceStartMs;
    const spanDurationMs =
      span.durationMicros !== null
        ? span.durationMicros / 1_000
        : Math.max(Date.parse(span.endedAt ?? span.startedAt) - Date.parse(span.startedAt), 0);

    return {
      spanId: span.spanId,
      component: span.component,
      operation: span.operation,
      status: span.status,
      state: stateFromSpanStatus(span.status, span.durationMicros, slowThresholdMicros),
      durationMicros: span.durationMicros,
      offsetPercent: totalDurationMs > 0 ? (startOffsetMs / totalDurationMs) * 100 : 0,
      widthPercent:
        totalDurationMs > 0 ? Math.max((spanDurationMs / totalDurationMs) * 100, 0.5) : 0,
    };
  });
}

export interface UnexplainedTime {
  unexplainedMicros: number;
  unexplainedPercent: number;
}

interface Interval {
  startMs: number;
  endMs: number;
}

/**
 * Merges overlapping or touching intervals (sorted by start) so their combined length can be
 * summed without double-counting a nested span or two parallel `Task.WhenAll` branches (D-78).
 */
function mergeIntervals(intervals: Interval[]): Interval[] {
  const sorted = [...intervals].sort((a, b) => a.startMs - b.startMs);
  const merged: Interval[] = [];

  for (const current of sorted) {
    const last = merged[merged.length - 1];
    if (last && current.startMs <= last.endMs) {
      last.endMs = Math.max(last.endMs, current.endMs);
    } else {
      merged.push({ ...current });
    }
  }

  return merged;
}

/**
 * Pure function: how much of the trace's total duration isn't covered by any recorded span,
 * as the union of `[startedAt, endedAt)` intervals, not a naive sum of `durationMicros` (D-78).
 * A naive sum double-counts a nested span (its time is already inside its parent's) and two
 * sibling branches of a `Task.WhenAll` that overlap on the wall clock.
 */
export function computeUnexplainedTime(trace: FlowTraceDetail): UnexplainedTime | null {
  if (trace.status === 'Running' || trace.spans.length === 0 || trace.durationMicros === null) {
    return null;
  }

  const intervals = trace.spans
    .filter((span) => span.status !== 'Running' && span.endedAt !== null)
    .map((span) => ({ startMs: Date.parse(span.startedAt), endMs: Date.parse(span.endedAt!) }));

  const coveredMs = mergeIntervals(intervals).reduce(
    (sum, interval) => sum + (interval.endMs - interval.startMs),
    0,
  );

  const totalMicros = trace.durationMicros;
  const unexplainedMicros = Math.max(0, totalMicros - coveredMs * 1_000);

  return {
    unexplainedMicros,
    unexplainedPercent: totalMicros > 0 ? (unexplainedMicros / totalMicros) * 100 : 0,
  };
}
