import { eventTypeTint } from './event-type-tint';

describe('eventTypeTint', () => {
  it('tints started events flow', () => {
    expect(eventTypeTint('TraceStarted')).toBe('flow');
    expect(eventTypeTint('SpanStarted')).toBe('flow');
  });

  it('tints completed events success', () => {
    expect(eventTypeTint('SpanCompleted')).toBe('success');
    expect(eventTypeTint('TraceCompleted')).toBe('success');
  });

  it('tints a failed span danger', () => {
    expect(eventTypeTint('SpanFailed')).toBe('danger');
  });

  it('tints everything else muted', () => {
    expect(eventTypeTint('TracePersisted')).toBe('muted');
  });
});
