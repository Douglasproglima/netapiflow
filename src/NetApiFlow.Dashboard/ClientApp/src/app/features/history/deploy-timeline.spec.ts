import { DeployAnnotation, FlowTraceSummary } from '../../core/models';
import { mergeTraceRowsWithDeployMarkers } from './deploy-timeline';

function trace(traceId: string, startedAt: string): FlowTraceSummary {
  return {
    traceId,
    method: 'GET',
    routeTemplate: '/api/orders',
    httpStatus: 200,
    status: 'Success',
    startedAt,
    durationMicros: 1_000,
    truncated: false,
  };
}

function deploy(id: string, timestamp: string, label = 'v1.0.0'): DeployAnnotation {
  return { id, timestamp, label };
}

describe('mergeTraceRowsWithDeployMarkers', () => {
  it('interleaves a deploy marker between executions, descending', () => {
    const rows = mergeTraceRowsWithDeployMarkers(
      [trace('newer', '2026-11-13T12:00:00Z'), trace('older', '2026-11-13T10:00:00Z')],
      [deploy('d1', '2026-11-13T11:00:00Z')],
      'desc',
    );

    expect(rows.map(rowId)).toEqual(['newer', 'd1', 'older']);
  });

  it('interleaves a deploy marker between executions, ascending', () => {
    const rows = mergeTraceRowsWithDeployMarkers(
      [trace('older', '2026-11-13T10:00:00Z'), trace('newer', '2026-11-13T12:00:00Z')],
      [deploy('d1', '2026-11-13T11:00:00Z')],
      'asc',
    );

    expect(rows.map(rowId)).toEqual(['older', 'd1', 'newer']);
  });

  it('returns only trace rows when there are no deploy markers', () => {
    const rows = mergeTraceRowsWithDeployMarkers([trace('t1', '2026-11-13T10:00:00Z')], [], 'desc');

    expect(rows).toEqual([{ kind: 'trace', trace: trace('t1', '2026-11-13T10:00:00Z') }]);
  });

  it('returns only deploy rows when there are no executions', () => {
    const rows = mergeTraceRowsWithDeployMarkers(
      [],
      [deploy('d1', '2026-11-13T10:00:00Z')],
      'desc',
    );

    expect(rows).toEqual([{ kind: 'deploy', deploy: deploy('d1', '2026-11-13T10:00:00Z') }]);
  });
});

function rowId(row: ReturnType<typeof mergeTraceRowsWithDeployMarkers>[number]): string {
  return row.kind === 'trace' ? row.trace.traceId : row.deploy.id;
}
