/**
 * Mirrors the DTOs frozen in docs/plano/contratos.md, seção 3. Field names match the JSON
 * exactly (camelCase); every duration is microseconds, every timestamp an ISO 8601 UTC string.
 */

export type FlowStatus = 'Running' | 'Success' | 'Error';

/** The six visual states of seção 7.2 do prompt (D-26: lives in `core/`, not under `features/flow`). */
export type NodeVisualState =
  'waiting' | 'running' | 'completed' | 'slow' | 'error' | 'not-executed';

export type ComponentType =
  'Endpoint' | 'Controller' | 'UseCase' | 'Service' | 'Repository' | 'Database' | 'Http' | 'Custom';

export type FlowEventType =
  | 'TraceStarted'
  | 'SpanStarted'
  | 'SpanCompleted'
  | 'SpanFailed'
  | 'TraceCompleted'
  | 'TracePersisted';

export const FLOW_EVENT_TYPES: readonly FlowEventType[] = [
  'TraceStarted',
  'SpanStarted',
  'SpanCompleted',
  'SpanFailed',
  'TraceCompleted',
  'TracePersisted',
];

export interface FlowSpan {
  spanId: string;
  parentSpanId: string | null;
  sequence: number;
  component: string;
  operation: string;
  type: ComponentType;
  startedAt: string;
  endedAt: string | null;
  durationMicros: number | null;
  status: FlowStatus;
  errorType: string | null;
  errorMessage: string | null;
  isErrorOrigin: boolean;
  tags: Record<string, string>;
  logs: LogEntry[];
}

export interface LogEntry {
  timestamp: string;
  level: string;
  message: string;
  exceptionType: string | null;
  exceptionMessage: string | null;
}

/** Shape shared by GET /traces/{traceId} and the SSE "trace" key: a trace without its spans. */
export interface FlowTrace {
  traceId: string;
  method: string;
  routeTemplate: string;
  httpStatus: number | null;
  status: FlowStatus;
  startedAt: string;
  endedAt: string | null;
  durationMicros: number | null;
  spanCount: number;
  truncated: boolean;
  errorMessage: string | null;
  tags: Record<string, string>;
}

/** GET /traces list item: no spans, no tags. */
export interface FlowTraceSummary {
  traceId: string;
  method: string;
  routeTemplate: string;
  httpStatus: number | null;
  status: FlowStatus;
  startedAt: string;
  durationMicros: number | null;
  truncated: boolean;
}

/** GET /traces/{traceId} and GET /blueprint: a trace with its spans. */
export interface FlowTraceDetail extends FlowTrace {
  spans: FlowSpan[];
}

export interface SpanRef {
  spanId: string;
  component: string;
  operation: string;
}

/** GET /traces/{traceId}/spans/{spanId} */
export interface SpanDetail {
  span: FlowSpan;
  ancestors: SpanRef[];
  children: SpanRef[];
  medianDurationMicros: number | null;
  deltaFromMedianMicros: number | null;
}

/** GET /endpoints list item. */
export interface EndpointDescriptor {
  method: string;
  routeTemplate: string;
  displayName: string;
}

/** GET /metrics (D-30: `errorCount` is the raw count behind `errorRate`). */
export interface MetricsSummary {
  totalTraces: number;
  errorCount: number;
  errorRate: number;
  averageDurationMicros: number;
  p95DurationMicros: number;
  maxDurationMicros: number;
}

/** GET /config (D-24, `version` added by D-33, tail sampling fields added by D-82). */
export interface DashboardConfig {
  slowThresholdMicros: number;
  version: string;
  tailSamplingEnabled: boolean;
  successSampleRate: number;
}

/** GET /metrics/components list item. */
export interface ComponentStat {
  component: string;
  operation: string;
  executionCount: number;
  errorCount: number;
  medianDurationMicros: number;
  averageDurationMicros: number;
  p95DurationMicros: number;
  maxDurationMicros: number;
}

/** GET /metrics/routes list item. */
export interface RouteStat {
  method: string;
  routeTemplate: string;
  executionCount: number;
  errorCount: number;
  medianDurationMicros: number;
  averageDurationMicros: number;
  p95DurationMicros: number;
  maxDurationMicros: number;
}

/** GET /architecture node: a distinct component/operation step observed while executing a route. */
export interface ArchitectureNode {
  component: string;
  operation: string;
  type: ComponentType;
  executionCount: number;
}

/** GET /architecture edge: a distinct parent-child relationship observed between two steps. */
export interface ArchitectureEdge {
  fromComponent: string;
  fromOperation: string;
  toComponent: string;
  toOperation: string;
  callCount: number;
}

/** GET /architecture: static graph of a route, built from every execution within the queried window. */
export interface ArchitectureMap {
  totalExecutions: number;
  nodes: ArchitectureNode[];
  edges: ArchitectureEdge[];
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

/** Sort field accepted by GET /traces (D-41). */
export type TraceSortField = 'startedAt' | 'duration' | 'httpStatus' | 'method' | 'route';

/** Sort direction accepted by GET /traces (D-41). */
export type SortDirection = 'asc' | 'desc';

/** Query string accepted by GET /traces. */
export interface TraceQuery {
  status?: FlowStatus;
  method?: string;
  route?: string;
  tenant?: string;
  from?: string;
  to?: string;
  minDurationMicros?: number;
  page?: number;
  pageSize?: number;
  sort?: TraceSortField;
  order?: SortDirection;
}

/** Query string accepted by GET /metrics and GET /metrics/components. */
export interface StatsQuery {
  from?: string;
  to?: string;
}

/**
 * Minimal OpenAPI 3.x subset read by the Runner: only the fields needed to list
 * endpoints and build a request form, not a full spec of the format.
 */
export interface OpenApiParameter {
  name: string;
  in: 'path' | 'query' | 'header' | 'cookie';
  required?: boolean;
  schema?: { type?: string };
}

export interface OpenApiOperation {
  operationId?: string;
  summary?: string;
  parameters?: OpenApiParameter[];
  requestBody?: {
    required?: boolean;
    content?: Record<string, { schema?: unknown }>;
  };
}

export interface OpenApiDocument {
  paths?: Record<string, Record<string, OpenApiOperation>>;
}

/** SSE event body (seção 2 do contrato). Exactly one of span/trace is present. */
export interface FlowEvent {
  eventId: number;
  type: FlowEventType;
  traceId: string;
  timestamp: string;
  span?: FlowSpan;
  trace?: FlowTrace;
}
