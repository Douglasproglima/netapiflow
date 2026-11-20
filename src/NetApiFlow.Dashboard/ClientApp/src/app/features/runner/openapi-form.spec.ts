import {
  buildAuthApplication,
  buildEndpointForm,
  decodeJwtExpiry,
  effectiveSecurity,
  hasSecurityDeclaration,
  listOpenApiEndpoints,
  listSecuritySchemes,
  partitionDashboardRoutes,
  previewAuthApplication,
  resolveAgainstHostRoot,
} from './openapi-form';
import { OpenApiDocument, OpenApiSecurityScheme } from '../../core/models';

const DOC: OpenApiDocument = {
  paths: {
    '/api/orders/{id}': {
      get: {
        operationId: 'GetOrder',
        summary: 'Get an order by id',
        parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'integer' } }],
      },
      post: {
        operationId: 'CreateOrder',
        parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'integer' } }],
        requestBody: {
          required: true,
          content: {
            'application/json': {
              schema: {
                type: 'object',
                properties: { customerName: { type: 'string' } },
                required: ['customerName'],
              },
            },
          },
        },
      },
    },
    '/api/orders': {
      get: {
        operationId: 'ListOrders',
        parameters: [{ name: 'status', in: 'query', schema: { type: 'string' } }],
      },
    },
  },
};

describe('listOpenApiEndpoints', () => {
  it('flattens every operation in every path', () => {
    const endpoints = listOpenApiEndpoints(DOC);

    expect(endpoints).toHaveLength(3);
    expect(endpoints).toContainEqual({
      method: 'GET',
      path: '/api/orders/{id}',
      operationId: 'GetOrder',
      summary: 'Get an order by id',
    });
    expect(endpoints).toContainEqual({
      method: 'POST',
      path: '/api/orders/{id}',
      operationId: 'CreateOrder',
      summary: null,
    });
  });

  it('returns an empty list when the document has no paths', () => {
    expect(listOpenApiEndpoints({})).toEqual([]);
  });
});

describe('partitionDashboardRoutes', () => {
  const endpoints = listOpenApiEndpoints({
    paths: {
      '/api/orders': { get: {} },
      '/flow/api/traces': { get: {}, delete: {} },
      '/flow/api/config': { get: {} },
    },
  });

  it('hides every route under the dashboard base path and reports how many were hidden', () => {
    const { visible, hiddenCount } = partitionDashboardRoutes(endpoints, '/flow');

    expect(visible).toEqual([
      { method: 'GET', path: '/api/orders', operationId: null, summary: null },
    ]);
    expect(hiddenCount).toBe(3);
  });

  it('keeps every route when the dashboard is mounted somewhere that matches none of them', () => {
    const { visible, hiddenCount } = partitionDashboardRoutes(endpoints, '/internal/flow');

    expect(visible).toHaveLength(endpoints.length);
    expect(hiddenCount).toBe(0);
  });
});

describe('buildEndpointForm', () => {
  it('builds a path param field plus a single JSON body field for an endpoint with both', () => {
    const form = buildEndpointForm(DOC, 'post', '/api/orders/{id}');

    expect(form).toEqual({
      method: 'POST',
      path: '/api/orders/{id}',
      operationId: 'CreateOrder',
      hasBody: true,
      fields: [
        { name: 'id', kind: 'path', type: 'integer', required: true },
        { name: 'body', kind: 'body', type: 'object', required: true },
      ],
    });
  });

  it('builds only a query field for an endpoint with no path params or body', () => {
    const form = buildEndpointForm(DOC, 'get', '/api/orders');

    expect(form).toEqual({
      method: 'GET',
      path: '/api/orders',
      operationId: 'ListOrders',
      hasBody: false,
      fields: [{ name: 'status', kind: 'query', type: 'string', required: false }],
    });
  });

  it('is case-insensitive on the HTTP method', () => {
    const form = buildEndpointForm(DOC, 'GET', '/api/orders/{id}');
    expect(form?.operationId).toBe('GetOrder');
  });

  it('returns null when the method/path pair is not in the document', () => {
    expect(buildEndpointForm(DOC, 'delete', '/api/orders/{id}')).toBeNull();
    expect(buildEndpointForm(DOC, 'get', '/unknown')).toBeNull();
  });
});

describe('listSecuritySchemes', () => {
  it('returns every declared scheme, including oauth2 and openIdConnect, without filtering by type', () => {
    const doc: OpenApiDocument = {
      components: {
        securitySchemes: {
          Bearer: { type: 'http', scheme: 'bearer' },
          ApiKeyHeader: { type: 'apiKey', in: 'header', name: 'X-Api-Key' },
          OAuth2Scheme: { type: 'oauth2' },
          OidcScheme: { type: 'openIdConnect' },
        },
      },
    };

    const schemes = listSecuritySchemes(doc);

    expect(schemes).toHaveLength(4);
    expect(schemes).toContainEqual({ name: 'Bearer', scheme: { type: 'http', scheme: 'bearer' } });
    expect(schemes).toContainEqual({
      name: 'ApiKeyHeader',
      scheme: { type: 'apiKey', in: 'header', name: 'X-Api-Key' },
    });
    expect(schemes).toContainEqual({ name: 'OAuth2Scheme', scheme: { type: 'oauth2' } });
    expect(schemes).toContainEqual({ name: 'OidcScheme', scheme: { type: 'openIdConnect' } });
  });

  it('returns an empty list, without throwing, when the document declares no securitySchemes', () => {
    expect(listSecuritySchemes({})).toEqual([]);
    expect(listSecuritySchemes({ components: {} })).toEqual([]);
  });
});

describe('effectiveSecurity', () => {
  const doc: OpenApiDocument = {
    security: [{ Bearer: [] }],
    paths: {
      '/api/orders': {
        get: { security: [{ ApiKey: [] }] },
        delete: { security: [] },
        post: {},
      },
    },
  };

  it("favors the operation's own security over the document root", () => {
    expect(effectiveSecurity(doc, 'get', '/api/orders')).toEqual([{ ApiKey: [] }]);
  });

  it('treats an explicit empty security array on the operation as public, even with a root requirement', () => {
    expect(effectiveSecurity(doc, 'delete', '/api/orders')).toEqual([]);
  });

  it('falls back to the document root security when the operation declares no security key', () => {
    expect(effectiveSecurity(doc, 'post', '/api/orders')).toEqual([{ Bearer: [] }]);
  });

  it('returns an empty list when neither the operation nor the root declares security', () => {
    expect(effectiveSecurity({ paths: { '/x': { get: {} } } }, 'get', '/x')).toEqual([]);
  });
});

describe('hasSecurityDeclaration', () => {
  it('is true when the operation declares its own security, even an explicit empty one', () => {
    const doc: OpenApiDocument = { paths: { '/x': { get: { security: [] } } } };
    expect(hasSecurityDeclaration(doc, 'get', '/x')).toBe(true);
  });

  it('is true when only the document root declares security', () => {
    const doc: OpenApiDocument = { security: [{ Bearer: [] }], paths: { '/x': { get: {} } } };
    expect(hasSecurityDeclaration(doc, 'get', '/x')).toBe(true);
  });

  it('is false when neither the operation nor the root declares security at all', () => {
    const doc: OpenApiDocument = { paths: { '/x': { get: {} } } };
    expect(hasSecurityDeclaration(doc, 'get', '/x')).toBe(false);
  });
});

describe('buildAuthApplication', () => {
  const bearerScheme: OpenApiSecurityScheme = { type: 'http', scheme: 'bearer' };
  const oauth2Scheme: OpenApiSecurityScheme = { type: 'oauth2' };
  const oidcScheme: OpenApiSecurityScheme = { type: 'openIdConnect' };
  const apiKeyHeaderScheme: OpenApiSecurityScheme = {
    type: 'apiKey',
    in: 'header',
    name: 'X-Api-Key',
  };
  const apiKeyQueryScheme: OpenApiSecurityScheme = { type: 'apiKey', in: 'query', name: 'api_key' };
  const apiKeyCookieScheme: OpenApiSecurityScheme = { type: 'apiKey', in: 'cookie', name: 'sid' };
  const basicScheme: OpenApiSecurityScheme = { type: 'http', scheme: 'basic' };

  it('builds an Authorization: Bearer header for an http/bearer scheme', () => {
    expect(buildAuthApplication(bearerScheme, 'xyz')).toEqual({
      target: 'header',
      name: 'Authorization',
      value: 'Bearer xyz',
    });
  });

  it('treats oauth2 and openIdConnect as "paste an access token", same as http/bearer', () => {
    expect(buildAuthApplication(oauth2Scheme, 'xyz')).toEqual({
      target: 'header',
      name: 'Authorization',
      value: 'Bearer xyz',
    });
    expect(buildAuthApplication(oidcScheme, 'xyz')).toEqual({
      target: 'header',
      name: 'Authorization',
      value: 'Bearer xyz',
    });
  });

  it('trims whitespace and strips an already-pasted "Bearer " prefix, case-insensitively', () => {
    expect(buildAuthApplication(bearerScheme, '  xyz\n')).toEqual({
      target: 'header',
      name: 'Authorization',
      value: 'Bearer xyz',
    });
    expect(buildAuthApplication(bearerScheme, 'Bearer xyz')).toEqual({
      target: 'header',
      name: 'Authorization',
      value: 'Bearer xyz',
    });
    expect(buildAuthApplication(bearerScheme, 'bearer xyz')).toEqual({
      target: 'header',
      name: 'Authorization',
      value: 'Bearer xyz',
    });
  });

  it('builds a named header for apiKey in:header', () => {
    expect(buildAuthApplication(apiKeyHeaderScheme, 'xyz')).toEqual({
      target: 'header',
      name: 'X-Api-Key',
      value: 'xyz',
    });
  });

  it('builds a named query parameter for apiKey in:query', () => {
    expect(buildAuthApplication(apiKeyQueryScheme, 'xyz')).toEqual({
      target: 'query',
      name: 'api_key',
      value: 'xyz',
    });
  });

  it('returns null for apiKey in:cookie, out of reach for this panel', () => {
    expect(buildAuthApplication(apiKeyCookieScheme, 'xyz')).toBeNull();
  });

  it('returns null for http/basic, out of scope for this phase', () => {
    expect(buildAuthApplication(basicScheme, 'xyz')).toBeNull();
  });

  it('returns null when the value is empty or only whitespace', () => {
    expect(buildAuthApplication(bearerScheme, '')).toBeNull();
    expect(buildAuthApplication(bearerScheme, '   ')).toBeNull();
  });
});

describe('previewAuthApplication', () => {
  it('masks everything but the last 4 characters of the bearer token', () => {
    expect(previewAuthApplication({ type: 'http', scheme: 'bearer' }, 'abcdef82a')).toEqual({
      target: 'header',
      name: 'Authorization',
      value: 'Bearer ••••f82a',
    });
  });

  it('masks the apiKey query preview the same way', () => {
    expect(
      previewAuthApplication({ type: 'apiKey', in: 'query', name: 'api_key' }, 'abcdef82a'),
    ).toEqual({ target: 'query', name: 'api_key', value: '••••f82a' });
  });

  it('fully masks a value with 4 characters or fewer', () => {
    expect(
      previewAuthApplication({ type: 'apiKey', in: 'header', name: 'X-Api-Key' }, 'abc'),
    ).toEqual({ target: 'header', name: 'X-Api-Key', value: '••••' });
  });

  it('returns null when there is nothing to preview', () => {
    expect(previewAuthApplication({ type: 'http', scheme: 'bearer' }, '')).toBeNull();
  });
});

describe('decodeJwtExpiry', () => {
  function jwtWithExp(exp: number): string {
    const payload = btoa(JSON.stringify({ sub: 'user', exp }))
      .replace(/\+/g, '-')
      .replace(/\//g, '_')
      .replace(/=+$/, '');
    return `header.${payload}.signature`;
  }

  it('reads a future exp claim', () => {
    const inTenMinutes = Math.floor(Date.now() / 1000) + 600;

    expect(decodeJwtExpiry(jwtWithExp(inTenMinutes))?.getTime()).toBe(inTenMinutes * 1000);
  });

  it('reads a past exp claim, without treating it as absent', () => {
    const threeMinutesAgo = Math.floor(Date.now() / 1000) - 180;

    expect(decodeJwtExpiry(jwtWithExp(threeMinutesAgo))?.getTime()).toBe(threeMinutesAgo * 1000);
  });

  it('returns null for an opaque token that is not a JWT', () => {
    expect(decodeJwtExpiry('d7f3a9c1e5b2')).toBeNull();
  });

  it('returns null for a JWT carrying no exp claim', () => {
    const payload = btoa(JSON.stringify({ sub: 'user' })).replace(/=+$/, '');

    expect(decodeJwtExpiry(`header.${payload}.signature`)).toBeNull();
  });

  it('returns null when the payload segment is not valid base64url JSON', () => {
    expect(decodeJwtExpiry('header.!!!not-base64!!!.signature')).toBeNull();
  });

  it('returns null for an empty value', () => {
    expect(decodeJwtExpiry('')).toBeNull();
  });
});

describe('resolveAgainstHostRoot', () => {
  it('leaves the path untouched when the host uses no PathBase', () => {
    expect(resolveAgainstHostRoot('', '/api/orders/5')).toBe('/api/orders/5');
  });

  it('prefixes the host PathBase, never the dashboard mount path', () => {
    expect(resolveAgainstHostRoot('/app', '/api/orders/5')).toBe('/app/api/orders/5');
    expect(resolveAgainstHostRoot('/app', '/openapi/v1.json')).toBe('/app/openapi/v1.json');
  });

  it('does not double the separator when the PathBase carries a trailing slash', () => {
    expect(resolveAgainstHostRoot('/app/', '/api/orders/5')).toBe('/app/api/orders/5');
  });

  it('inserts the separator for a path declared without a leading slash', () => {
    expect(resolveAgainstHostRoot('/app', 'api/orders/5')).toBe('/app/api/orders/5');
  });
});
