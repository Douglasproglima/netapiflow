import {
  OpenApiDocument,
  OpenApiSecurityRequirement,
  OpenApiSecurityScheme,
} from '../../core/models';

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

/**
 * Resolves a path the host declares (its OpenAPI document, one of its endpoints) against the
 * host's own root. Both are root-relative to the host, never to wherever the dashboard happens to
 * be mounted, so the dashboard's `<base href>` is the wrong prefix: it already carries the
 * dashboard's own mount path.
 */
export function resolveAgainstHostRoot(hostPathBase: string, path: string): string {
  const base = hostPathBase.replace(/\/+$/, '');
  if (base === '') {
    return path;
  }

  return path.startsWith('/') ? `${base}${path}` : `${base}/${path}`;
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

export interface SecuritySchemeEntry {
  name: string;
  scheme: OpenApiSecurityScheme;
}

/**
 * Every scheme declared in `components.securitySchemes`, unfiltered by type: `oauth2` and
 * `openIdConnect` are listed like any other, since the endpoint form treats them as "paste an
 * already-obtained access token" rather than hiding them for lacking a full login flow.
 */
export function listSecuritySchemes(doc: OpenApiDocument): SecuritySchemeEntry[] {
  return Object.entries(doc.components?.securitySchemes ?? {}).map(([name, scheme]) => ({
    name,
    scheme,
  }));
}

/**
 * The `security` that actually governs one operation: its own `security` (including an explicit
 * empty array, which OpenAPI defines as "this operation is public") takes precedence over the
 * document root; falling back to the root only when the operation declares no `security` key at
 * all, which is how a global `AddSecurityRequirement` protects every undecorated operation.
 */
export function effectiveSecurity(
  doc: OpenApiDocument,
  method: string,
  path: string,
): OpenApiSecurityRequirement[] {
  const operation = doc.paths?.[path]?.[method.toLowerCase()];
  if (operation?.security !== undefined) {
    return operation.security;
  }

  return doc.security ?? [];
}

/**
 * Whether anything in the document, operation or root, declares a `security` key at all,
 * distinct from `effectiveSecurity` returning an empty list: that also happens for an operation
 * explicitly marked `security: []` (intentionally public), which is not the "documento
 * incompleto" case this distinguishes.
 */
export function hasSecurityDeclaration(
  doc: OpenApiDocument,
  method: string,
  path: string,
): boolean {
  const operation = doc.paths?.[path]?.[method.toLowerCase()];
  return operation?.security !== undefined || doc.security !== undefined;
}

function isSupportedAuthScheme(scheme: OpenApiSecurityScheme): boolean {
  if (scheme.type === 'apiKey') {
    return scheme.in !== 'cookie';
  }
  if (scheme.type === 'http') {
    return (scheme.scheme ?? '').toLowerCase() === 'bearer';
  }
  return scheme.type === 'oauth2' || scheme.type === 'openIdConnect';
}

/**
 * Strips what a pasted token commonly carries along: surrounding whitespace, and (for a scheme
 * that will be sent as `Authorization: Bearer <value>`) an already-typed `"Bearer "` prefix, so
 * the applied header never duplicates it.
 */
function sanitizePastedAuthValue(scheme: OpenApiSecurityScheme, rawValue: string): string {
  const trimmed = rawValue.trim();
  const isBearerLike =
    (scheme.type === 'http' && (scheme.scheme ?? '').toLowerCase() === 'bearer') ||
    scheme.type === 'oauth2' ||
    scheme.type === 'openIdConnect';
  return isBearerLike ? trimmed.replace(/^bearer\s+/i, '') : trimmed;
}

export interface AuthApplication {
  target: 'header' | 'query';
  name: string;
  value: string;
}

/**
 * What to attach to the request for one scheme's pasted value: a header for `http`/`bearer`,
 * `oauth2`/`openIdConnect` (treated as "paste an already-obtained access token") and `apiKey
 * in: header`; a query parameter for `apiKey in: query`. `null` when the scheme is out of reach
 * for this panel (`apiKey in: cookie`, `http`/`basic`) or the value is empty.
 */
export function buildAuthApplication(
  scheme: OpenApiSecurityScheme,
  rawValue: string,
): AuthApplication | null {
  if (!isSupportedAuthScheme(scheme)) {
    return null;
  }

  const value = sanitizePastedAuthValue(scheme, rawValue);
  if (value === '') {
    return null;
  }

  if (scheme.type === 'apiKey') {
    const name = scheme.name ?? 'api_key';
    return scheme.in === 'query'
      ? { target: 'query', name, value }
      : { target: 'header', name, value };
  }

  return { target: 'header', name: 'Authorization', value: `Bearer ${value}` };
}

function maskAuthValue(value: string): string {
  return value.length <= 4 ? '••••' : `••••${value.slice(-4)}`;
}

/** Same shape as `buildAuthApplication`, with the secret masked for display. */
export function previewAuthApplication(
  scheme: OpenApiSecurityScheme,
  rawValue: string,
): AuthApplication | null {
  const application = buildAuthApplication(scheme, rawValue);
  if (!application) {
    return null;
  }

  const secret = sanitizePastedAuthValue(scheme, rawValue);
  const prefix = application.value.slice(0, application.value.length - secret.length);
  return { ...application, value: `${prefix}${maskAuthValue(secret)}` };
}
