import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ComponentStat,
  EndpointDescriptor,
  FlowTraceDetail,
  FlowTraceSummary,
  MetricsSummary,
  OpenApiDocument,
  PagedResult,
  SpanDetail,
  StatsQuery,
  TraceQuery,
} from './models';

/**
 * HTTP client for the ten routes frozen in docs/plano/contratos.md, seção 3. Every path is
 * relative (no leading slash), so it resolves against whatever base path the host page sets via
 * <base href> (risco R-03): the dashboard can be mounted anywhere, and there is never a CORS
 * round-trip because the API is always same-origin, relative to wherever this page was served.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly basePath = 'api';

  getTraces(query: TraceQuery): Observable<PagedResult<FlowTraceSummary>> {
    return this.http.get<PagedResult<FlowTraceSummary>>(`${this.basePath}/traces`, {
      params: toHttpParams(query),
    });
  }

  getLiveTraces(): Observable<FlowTraceSummary[]> {
    return this.http.get<FlowTraceSummary[]>(`${this.basePath}/traces/live`);
  }

  getTrace(traceId: string): Observable<FlowTraceDetail> {
    return this.http.get<FlowTraceDetail>(`${this.basePath}/traces/${encodeURIComponent(traceId)}`);
  }

  getSpan(traceId: string, spanId: string): Observable<SpanDetail> {
    return this.http.get<SpanDetail>(
      `${this.basePath}/traces/${encodeURIComponent(traceId)}/spans/${encodeURIComponent(spanId)}`,
    );
  }

  /** Returns `null` when the API responds 204 (no previous successful execution). */
  getBlueprint(method: string, route: string): Observable<FlowTraceDetail | null> {
    return this.http.get<FlowTraceDetail | null>(`${this.basePath}/blueprint`, {
      params: new HttpParams().set('method', method).set('route', route),
    });
  }

  getEndpoints(): Observable<EndpointDescriptor[]> {
    return this.http.get<EndpointDescriptor[]>(`${this.basePath}/endpoints`);
  }

  getMetrics(query: StatsQuery): Observable<MetricsSummary> {
    return this.http.get<MetricsSummary>(`${this.basePath}/metrics`, {
      params: toHttpParams(query),
    });
  }

  getComponentMetrics(query: StatsQuery): Observable<ComponentStat[]> {
    return this.http.get<ComponentStat[]>(`${this.basePath}/metrics/components`, {
      params: toHttpParams(query),
    });
  }

  deleteTraces(): Observable<void> {
    return this.http.delete<void>(`${this.basePath}/traces`);
  }

  /**
   * Dispatches a request to an arbitrary endpoint of the host application (the Runner, tarefa
   * 4.11), as opposed to every other method here, which calls the dashboard's own ten routes.
   * `path` is root-relative, exactly as declared in the host's OpenAPI document (never prefixed
   * with `basePath`, since it does not belong to the dashboard's own API namespace).
   */
  callEndpoint(method: string, path: string, body?: unknown): Observable<HttpResponse<unknown>> {
    return this.http.request(method, path, { body, observe: 'response' });
  }

  /** Fetches the host application's own OpenAPI document, at a root-relative, configurable URL. */
  loadOpenApiDocument(url: string): Observable<OpenApiDocument> {
    return this.http.get<OpenApiDocument>(url);
  }
}

/** Builds HttpParams from a plain object, skipping undefined values (query param absent = no filter). */
function toHttpParams(query: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined) {
      params = params.set(key, String(value));
    }
  }

  return params;
}
