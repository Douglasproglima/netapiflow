import { FlowStatus, FlowTraceDetail, NodeVisualState } from '../../../core/models';
import { parseTimestampMicros } from '../../../shared/timestamp-micros';
import { stateFromSpanStatus } from '../graph/graph-builder';

const MINIMUM_BAR_WIDTH_PERCENT = 0.5;

export interface WaterfallRow {
  spanId: string;
  component: string;
  operation: string;
  status: FlowStatus;
  state: NodeVisualState;
  durationMicros: number | null;
  offsetPercent: number;
  widthPercent: number;
}

export function buildWaterfall(
  trace: FlowTraceDetail,
  slowThresholdMicros: number,
): WaterfallRow[] {
  if (trace.spans.length === 0) {
    return [];
  }

  const traceStartMicros = parseTimestampMicros(trace.startedAt);
  const sorted = [...trace.spans].sort(
    (a, b) => parseTimestampMicros(a.startedAt) - parseTimestampMicros(b.startedAt),
  );

  const latestSpanEndMicros = Math.max(
    ...sorted.map(
      (span) => parseTimestampMicros(span.endedAt ?? span.startedAt) - traceStartMicros,
    ),
    1,
  );
  const totalDurationMicros = trace.durationMicros ?? latestSpanEndMicros;

  return sorted.map((span) => {
    const startOffsetMicros = parseTimestampMicros(span.startedAt) - traceStartMicros;
    const spanDurationMicros =
      span.durationMicros ??
      Math.max(
        parseTimestampMicros(span.endedAt ?? span.startedAt) - parseTimestampMicros(span.startedAt),
        0,
      );

    return {
      spanId: span.spanId,
      component: span.component,
      operation: span.operation,
      status: span.status,
      state: stateFromSpanStatus(span.status, span.durationMicros, slowThresholdMicros),
      durationMicros: span.durationMicros,
      offsetPercent: totalDurationMicros > 0 ? (startOffsetMicros / totalDurationMicros) * 100 : 0,
      widthPercent:
        totalDurationMicros > 0
          ? Math.max((spanDurationMicros / totalDurationMicros) * 100, MINIMUM_BAR_WIDTH_PERCENT)
          : 0,
    };
  });
}

export interface UnexplainedTime {
  unexplainedMicros: number;
  unexplainedPercent: number;
}

interface Interval {
  startMicros: number;
  endMicros: number;
}

function mergeIntervals(intervals: Interval[]): Interval[] {
  const sorted = [...intervals].sort((a, b) => a.startMicros - b.startMicros);
  const merged: Interval[] = [];

  for (const current of sorted) {
    const last = merged[merged.length - 1];
    if (last && current.startMicros <= last.endMicros) {
      last.endMicros = Math.max(last.endMicros, current.endMicros);
    } else {
      merged.push({ ...current });
    }
  }

  return merged;
}

export function computeUnexplainedTime(trace: FlowTraceDetail): UnexplainedTime | null {
  if (trace.status === 'Running' || trace.spans.length === 0 || trace.durationMicros === null) {
    return null;
  }

  const intervals = trace.spans
    .filter((span) => span.status !== 'Running' && span.endedAt !== null)
    .map((span) => ({
      startMicros: parseTimestampMicros(span.startedAt),
      endMicros: parseTimestampMicros(span.endedAt!),
    }));

  const coveredMicros = mergeIntervals(intervals).reduce(
    (sum, interval) => sum + (interval.endMicros - interval.startMicros),
    0,
  );

  const totalMicros = trace.durationMicros;
  const unexplainedMicros = Math.max(0, totalMicros - coveredMicros);

  return {
    unexplainedMicros,
    unexplainedPercent: totalMicros > 0 ? (unexplainedMicros / totalMicros) * 100 : 0,
  };
}
