import { FlowEventType } from '../core/models';

export type EventTypeTint = 'flow' | 'success' | 'danger' | 'muted';

/** Color per event type (guide seção 6.8): started events are "flow", completions "success", failures "danger". */
export function eventTypeTint(type: FlowEventType): EventTypeTint {
  if (type === 'TraceStarted' || type === 'SpanStarted') {
    return 'flow';
  }

  if (type === 'SpanCompleted' || type === 'TraceCompleted') {
    return 'success';
  }

  if (type === 'SpanFailed') {
    return 'danger';
  }

  return 'muted';
}
