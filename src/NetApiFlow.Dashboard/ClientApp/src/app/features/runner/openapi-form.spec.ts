import { buildEndpointForm, listOpenApiEndpoints } from './openapi-form';
import { OpenApiDocument } from '../../core/models';

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
