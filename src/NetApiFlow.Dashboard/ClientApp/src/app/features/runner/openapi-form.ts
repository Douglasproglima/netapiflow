import { OpenApiDocument } from '../../core/models';

const HTTP_METHODS = ['get', 'post', 'put', 'patch', 'delete'];

export interface OpenApiEndpoint {
  method: string;
  path: string;
  operationId: string | null;
  summary: string | null;
}

/** Flattens every operation in every path into one list, for an endpoint picker. */
export function listOpenApiEndpoints(doc: OpenApiDocument): OpenApiEndpoint[] {
  const endpoints: OpenApiEndpoint[] = [];

  for (const [path, operations] of Object.entries(doc.paths ?? {})) {
    for (const [method, operation] of Object.entries(operations)) {
      if (!HTTP_METHODS.includes(method.toLowerCase())) {
        continue;
      }

      endpoints.push({
        method: method.toUpperCase(),
        path,
        operationId: operation.operationId ?? null,
        summary: operation.summary ?? null,
      });
    }
  }

  return endpoints;
}

/**
 * Splits endpoints into the host's own and the dashboard's own routes (guide seção 6.3, D2):
 * the dashboard maps its REST API on the same route builder as the host app, so a host that calls
 * `MapOpenApi()` documents `{basePath}/api/*` (e.g. `DELETE /flow/api/traces`, which erases the
 * trace history) alongside the user's actual endpoints. Hidden by default so a stray click can't
 * reach them; `hiddenCount` drives the "N dashboard routes hidden" note.
 */
export function partitionDashboardRoutes(
  endpoints: OpenApiEndpoint[],
  dashboardBasePath: string,
): { visible: OpenApiEndpoint[]; hiddenCount: number } {
  if (!dashboardBasePath) {
    return { visible: endpoints, hiddenCount: 0 };
  }

  const prefix = `${dashboardBasePath}/`;
  const visible = endpoints.filter((endpoint) => !endpoint.path.startsWith(prefix));
  return { visible, hiddenCount: endpoints.length - visible.length };
}

export type FormFieldKind = 'path' | 'query' | 'body';

export interface FormField {
  name: string;
  kind: FormFieldKind;
  type: string;
  required: boolean;
}

export interface EndpointForm {
  method: string;
  path: string;
  operationId: string | null;
  fields: FormField[];
  hasBody: boolean;
}

/**
 * Builds the form for one endpoint: one field per path/query parameter, plus a single JSON body
 * field when the operation declares a requestBody: the body is
 * one raw JSON field, never decomposed into one field per schema property.
 */
export function buildEndpointForm(
  doc: OpenApiDocument,
  method: string,
  path: string,
): EndpointForm | null {
  const operation = doc.paths?.[path]?.[method.toLowerCase()];
  if (!operation) {
    return null;
  }

  const fields: FormField[] = (operation.parameters ?? [])
    .filter((parameter) => parameter.in === 'path' || parameter.in === 'query')
    .map((parameter) => ({
      name: parameter.name,
      kind: parameter.in as FormFieldKind,
      type: parameter.schema?.type ?? 'string',
      required: parameter.required ?? false,
    }));

  const hasBody = !!operation.requestBody;
  if (hasBody) {
    fields.push({
      name: 'body',
      kind: 'body',
      type: 'object',
      required: operation.requestBody?.required ?? false,
    });
  }

  return {
    method: method.toUpperCase(),
    path,
    operationId: operation.operationId ?? null,
    fields,
    hasBody,
  };
}
