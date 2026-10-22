import { FlowStatus } from '../core/models';

export type StatusTint = 'success' | 'danger' | 'flow';

/** Maps a trace/span's own status to the color + icon family used across recent executions and events (guide seções 6.7-6.8). */
export function statusTint(status: FlowStatus): StatusTint {
  if (status === 'Error') {
    return 'danger';
  }

  if (status === 'Running') {
    return 'flow';
  }

  return 'success';
}
