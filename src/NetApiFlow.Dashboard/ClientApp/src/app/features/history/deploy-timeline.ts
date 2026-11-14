import { DeployAnnotation, FlowTraceSummary, SortDirection } from '../../core/models';

export type HistoryRow =
  { kind: 'trace'; trace: FlowTraceSummary } | { kind: 'deploy'; deploy: DeployAnnotation };

/**
 * Interleaves deploy markers among execution rows by timestamp, matching `direction`. Array.sort
 * is stable, so trace and deploy rows sharing a timestamp keep their relative input order.
 */
export function mergeTraceRowsWithDeployMarkers(
  traces: readonly FlowTraceSummary[],
  deploys: readonly DeployAnnotation[],
  direction: SortDirection,
): HistoryRow[] {
  const rows: HistoryRow[] = [
    ...traces.map((trace): HistoryRow => ({ kind: 'trace', trace })),
    ...deploys.map((deploy): HistoryRow => ({ kind: 'deploy', deploy })),
  ];

  const sign = direction === 'asc' ? 1 : -1;
  return rows.sort((a, b) => sign * (rowTimestamp(a) - rowTimestamp(b)));
}

function rowTimestamp(row: HistoryRow): number {
  return new Date(row.kind === 'trace' ? row.trace.startedAt : row.deploy.timestamp).getTime();
}
