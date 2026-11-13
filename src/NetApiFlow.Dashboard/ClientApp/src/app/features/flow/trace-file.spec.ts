import { FlowTraceDetail } from '../../core/models';
import { parseTraceFile, traceExportFileName } from './trace-file';

function validTrace(): FlowTraceDetail {
  return {
    traceId: 't1',
    method: 'GET',
    routeTemplate: '/api/orders/{id}',
    httpStatus: 200,
    status: 'Success',
    startedAt: '2026-01-01T00:00:00.000000Z',
    endedAt: '2026-01-01T00:00:00.000100Z',
    durationMicros: 100,
    spanCount: 1,
    truncated: false,
    errorMessage: null,
    tags: {},
    spans: [
      {
        spanId: 's1',
        parentSpanId: null,
        sequence: 0,
        component: 'OrderController',
        operation: 'Get',
        type: 'Controller',
        startedAt: '2026-01-01T00:00:00.000000Z',
        endedAt: '2026-01-01T00:00:00.000100Z',
        durationMicros: 100,
        status: 'Success',
        errorType: null,
        errorMessage: null,
        isErrorOrigin: false,
        tags: {},
        logs: [
          {
            timestamp: '2026-01-01T00:00:00.000050Z',
            level: 'Information',
            message: 'done',
            exceptionType: null,
            exceptionMessage: null,
          },
        ],
      },
    ],
  };
}

describe('traceExportFileName', () => {
  it('builds a filename that includes the trace id', () => {
    expect(traceExportFileName('abc-123')).toBe('trace-abc-123.json');
  });
});

describe('parseTraceFile', () => {
  it('accepts a well-formed FlowTraceDetail export', () => {
    const result = parseTraceFile(JSON.stringify(validTrace()));
    expect(result).toEqual({ ok: true, trace: validTrace() });
  });

  it('rejects malformed JSON', () => {
    expect(parseTraceFile('{ not json')).toEqual({ ok: false, reason: 'invalid-json' });
  });

  it('rejects valid JSON that is not a trace, such as an empty object', () => {
    expect(parseTraceFile('{}')).toEqual({ ok: false, reason: 'invalid-shape' });
  });

  it('rejects valid JSON that is an array instead of an object', () => {
    expect(parseTraceFile('[]')).toEqual({ ok: false, reason: 'invalid-shape' });
  });

  it('rejects a trace whose status is outside the known FlowStatus values', () => {
    const trace = { ...validTrace(), status: 'Unknown' };
    expect(parseTraceFile(JSON.stringify(trace))).toEqual({ ok: false, reason: 'invalid-shape' });
  });

  it.each(['Cancelled', 'Timeout'])('accepts a trace whose status is %s', (status) => {
    const trace = { ...validTrace(), status };
    expect(parseTraceFile(JSON.stringify(trace))).toEqual({ ok: true, trace });
  });

  it('rejects a trace whose spans is not an array', () => {
    const trace = { ...validTrace(), spans: 'not-an-array' };
    expect(parseTraceFile(JSON.stringify(trace))).toEqual({ ok: false, reason: 'invalid-shape' });
  });

  it('rejects a trace with a malformed span', () => {
    const trace = validTrace();
    const malformedSpan = { ...trace.spans[0], sequence: 'zero' };
    const withMalformedSpan = { ...trace, spans: [malformedSpan] };
    expect(parseTraceFile(JSON.stringify(withMalformedSpan))).toEqual({
      ok: false,
      reason: 'invalid-shape',
    });
  });

  it('rejects a trace whose tags are not all strings', () => {
    const trace = { ...validTrace(), tags: { k: 1 } };
    expect(parseTraceFile(JSON.stringify(trace))).toEqual({ ok: false, reason: 'invalid-shape' });
  });
});
