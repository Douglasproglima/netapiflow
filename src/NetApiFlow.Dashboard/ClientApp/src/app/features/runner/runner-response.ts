export const RESPONSE_BODY_LIMIT = 4000;

export type ResponseContent =
  | { kind: 'empty' }
  | { kind: 'text'; text: string; truncated: boolean }
  | { kind: 'not-shown'; contentType: string };

export interface RunnerResponse {
  status: number;
  content: ResponseContent;
}

function isShownAsText(contentType: string | null): boolean {
  if (contentType === null) {
    return true;
  }

  const type = contentType.toLowerCase();
  return type.startsWith('text/') || type.includes('json') || type.includes('xml');
}

function textOf(body: unknown): string | null {
  if (body === null || body === undefined) {
    return null;
  }

  return typeof body === 'string' ? body : JSON.stringify(body);
}

export function describeResponse(
  status: number,
  contentType: string | null,
  rawBody: unknown,
): RunnerResponse {
  const body = textOf(rawBody);
  if (!body) {
    return { status, content: { kind: 'empty' } };
  }

  if (!isShownAsText(contentType)) {
    return {
      status,
      content: { kind: 'not-shown', contentType: contentType!.split(';')[0].trim() },
    };
  }

  return {
    status,
    content: {
      kind: 'text',
      text: body.slice(0, RESPONSE_BODY_LIMIT),
      truncated: body.length > RESPONSE_BODY_LIMIT,
    },
  };
}
