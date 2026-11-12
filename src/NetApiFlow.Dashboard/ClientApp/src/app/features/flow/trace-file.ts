import { FlowSpan, FlowStatus, FlowTraceDetail, LogEntry } from '../../core/models';

export type ParseTraceFileResult =
  | { readonly ok: true; readonly trace: FlowTraceDetail }
  | { readonly ok: false; readonly reason: 'invalid-json' | 'invalid-shape' };

const FLOW_STATUSES: readonly FlowStatus[] = ['Running', 'Success', 'Error'];

export function traceExportFileName(traceId: string): string {
  return `trace-${traceId}.json`;
}

/** Trust boundary: a file picked by the user, parsed and shape-checked before it ever reaches a signal. */
export function parseTraceFile(raw: string): ParseTraceFileResult {
  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return { ok: false, reason: 'invalid-json' };
  }

  return isFlowTraceDetail(parsed)
    ? { ok: true, trace: parsed }
    : { ok: false, reason: 'invalid-shape' };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

function isNullableNumber(value: unknown): value is number | null {
  return value === null || typeof value === 'number';
}

function isStringRecord(value: unknown): value is Record<string, string> {
  return isRecord(value) && Object.values(value).every((entry) => typeof entry === 'string');
}

function isFlowStatus(value: unknown): value is FlowStatus {
  return typeof value === 'string' && FLOW_STATUSES.includes(value as FlowStatus);
}

function isLogEntry(value: unknown): value is LogEntry {
  return (
    isRecord(value) &&
    typeof value['timestamp'] === 'string' &&
    typeof value['level'] === 'string' &&
    typeof value['message'] === 'string' &&
    isNullableString(value['exceptionType']) &&
    isNullableString(value['exceptionMessage'])
  );
}

function isFlowSpan(value: unknown): value is FlowSpan {
  return (
    isRecord(value) &&
    typeof value['spanId'] === 'string' &&
    isNullableString(value['parentSpanId']) &&
    typeof value['sequence'] === 'number' &&
    typeof value['component'] === 'string' &&
    typeof value['operation'] === 'string' &&
    typeof value['type'] === 'string' &&
    typeof value['startedAt'] === 'string' &&
    isNullableString(value['endedAt']) &&
    isNullableNumber(value['durationMicros']) &&
    isFlowStatus(value['status']) &&
    isNullableString(value['errorType']) &&
    isNullableString(value['errorMessage']) &&
    typeof value['isErrorOrigin'] === 'boolean' &&
    isStringRecord(value['tags']) &&
    Array.isArray(value['logs']) &&
    value['logs'].every(isLogEntry)
  );
}

function isFlowTraceDetail(value: unknown): value is FlowTraceDetail {
  return (
    isRecord(value) &&
    typeof value['traceId'] === 'string' &&
    typeof value['method'] === 'string' &&
    typeof value['routeTemplate'] === 'string' &&
    isNullableNumber(value['httpStatus']) &&
    isFlowStatus(value['status']) &&
    typeof value['startedAt'] === 'string' &&
    isNullableString(value['endedAt']) &&
    isNullableNumber(value['durationMicros']) &&
    typeof value['spanCount'] === 'number' &&
    typeof value['truncated'] === 'boolean' &&
    isNullableString(value['errorMessage']) &&
    isStringRecord(value['tags']) &&
    Array.isArray(value['spans']) &&
    value['spans'].every(isFlowSpan)
  );
}
