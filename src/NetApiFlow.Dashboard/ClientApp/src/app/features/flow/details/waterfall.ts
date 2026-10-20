import { FlowStatus, FlowTraceDetail } from '../../../core/models';

export interface WaterfallRow {
  spanId: string;
  component: string;
  operation: string;
  status: FlowStatus;
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
 * `durationMicros`, never derived from these parsed timestamps (seção 7.3 do prompt).
 */
export function buildWaterfall(trace: FlowTraceDetail): WaterfallRow[] {
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
      durationMicros: span.durationMicros,
      offsetPercent: totalDurationMs > 0 ? (startOffsetMs / totalDurationMs) * 100 : 0,
      widthPercent:
        totalDurationMs > 0 ? Math.max((spanDurationMs / totalDurationMs) * 100, 0.5) : 0,
    };
  });
}
