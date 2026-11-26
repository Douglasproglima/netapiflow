import { describeResponse, RESPONSE_BODY_LIMIT } from './runner-response';

describe('describeResponse', () => {
  it('keeps the status and the whole of a short text body', () => {
    expect(describeResponse(200, 'text/plain', 'pong')).toEqual({
      status: 200,
      content: { kind: 'text', text: 'pong', truncated: false },
    });
  });

  it('keeps only the first characters of a body over the limit, and says it was cut', () => {
    const response = describeResponse(200, 'text/plain', 'a'.repeat(RESPONSE_BODY_LIMIT + 1));

    expect(response.content).toEqual({
      kind: 'text',
      text: 'a'.repeat(RESPONSE_BODY_LIMIT),
      truncated: true,
    });
  });

  it('does not say it cut a body that is exactly at the limit', () => {
    const response = describeResponse(200, 'text/plain', 'a'.repeat(RESPONSE_BODY_LIMIT));

    expect(response.content).toMatchObject({ kind: 'text', truncated: false });
  });

  it.each([null, ''])('calls a body of %j empty', (body) => {
    expect(describeResponse(204, 'text/plain', body).content).toEqual({ kind: 'empty' });
  });

  it.each([
    'text/html; charset=utf-8',
    'application/json',
    'application/problem+json',
    'application/xml',
    'image/svg+xml',
  ])('shows a body of type %s as text', (contentType) => {
    expect(describeResponse(200, contentType, 'x').content.kind).toBe('text');
  });

  it('shows a body as text when the answer does not say what it is', () => {
    expect(describeResponse(200, null, 'x').content.kind).toBe('text');
  });

  it('shows as JSON text a body that already arrived parsed, as the recorded demo delivers it', () => {
    expect(describeResponse(200, 'application/json', { id: 5, items: [1, 2] }).content).toEqual({
      kind: 'text',
      text: '{"id":5,"items":[1,2]}',
      truncated: false,
    });
  });

  it('does not show a body of another type, and gives the type without its parameters', () => {
    expect(describeResponse(200, 'application/octet-stream; charset=binary', 'x').content).toEqual({
      kind: 'not-shown',
      contentType: 'application/octet-stream',
    });
  });
});
