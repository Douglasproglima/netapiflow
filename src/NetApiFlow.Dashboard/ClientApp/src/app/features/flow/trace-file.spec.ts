import { traceExportFileName } from './trace-file';

describe('traceExportFileName', () => {
  it('builds a filename that includes the trace id', () => {
    expect(traceExportFileName('abc-123')).toBe('trace-abc-123.json');
  });
});
