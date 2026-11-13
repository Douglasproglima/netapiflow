import { FlowStatus } from '../core/models';

export type StatusTint = 'success' | 'danger' | 'flow' | 'idle';

/**
 * Maps a trace/span's own status to the color + icon family used across recent executions and
 * events (guide seções 6.7-6.8). `Timeout` tints like `Error`: an internal cancellation usually
 * means a real server-side problem. `Cancelled` tints `idle`, same family as a skipped or waiting
 * step: a client disconnecting isn't a server-side problem.
 */
export function statusTint(status: FlowStatus): StatusTint {
  if (status === 'Error' || status === 'Timeout') {
    return 'danger';
  }

  if (status === 'Running') {
    return 'flow';
  }

  if (status === 'Cancelled') {
    return 'idle';
  }

  return 'success';
}
