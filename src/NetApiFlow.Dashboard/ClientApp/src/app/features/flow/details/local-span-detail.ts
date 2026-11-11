import { FlowSpan, FlowTraceDetail, SpanDetail, SpanRef } from '../../../core/models';

function toSpanRef(span: FlowSpan): SpanRef {
  return { spanId: span.spanId, component: span.component, operation: span.operation };
}

/**
 * Builds a SpanDetail straight from a trace already in memory, with no round trip to
 * GET /traces/{traceId}/spans/{spanId} — the only option for an imported trace, whose id belongs
 * to a different instance and whose history this instance never recorded.
 */
export function buildLocalSpanDetail(trace: FlowTraceDetail, spanId: string): SpanDetail | null {
  const span = trace.spans.find((candidate) => candidate.spanId === spanId);
  if (!span) {
    return null;
  }

  const spansById = new Map(trace.spans.map((candidate) => [candidate.spanId, candidate]));
  const ancestors: SpanRef[] = [];
  let parentId = span.parentSpanId;
  while (parentId) {
    const parent = spansById.get(parentId);
    if (!parent) {
      break;
    }

    ancestors.unshift(toSpanRef(parent));
    parentId = parent.parentSpanId;
  }

  const children = trace.spans
    .filter((candidate) => candidate.parentSpanId === span.spanId)
    .map(toSpanRef);

  return {
    span,
    ancestors,
    children,
    medianDurationMicros: null,
    deltaFromMedianMicros: null,
  };
}
