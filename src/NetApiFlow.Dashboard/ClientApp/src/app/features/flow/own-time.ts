import { FlowSpan } from '../../core/models';

export function ownTimeOfEachStep(spans: readonly FlowSpan[]): Map<string, number> {
  const childrenMicros = new Map<string, number>();
  for (const span of spans) {
    if (span.parentSpanId !== null) {
      childrenMicros.set(
        span.parentSpanId,
        (childrenMicros.get(span.parentSpanId) ?? 0) + (span.durationMicros ?? 0),
      );
    }
  }

  return new Map(
    spans.map((span) => [
      span.spanId,
      Math.max(0, (span.durationMicros ?? 0) - (childrenMicros.get(span.spanId) ?? 0)),
    ]),
  );
}
