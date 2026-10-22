import { httpMethodTagClass } from './http-method-style';

describe('httpMethodTagClass', () => {
  it('maps each known verb to its own badge class', () => {
    expect(httpMethodTagClass('GET')).toBe('naf-tag--get');
    expect(httpMethodTagClass('POST')).toBe('naf-tag--post');
    expect(httpMethodTagClass('PUT')).toBe('naf-tag--put');
    expect(httpMethodTagClass('PATCH')).toBe('naf-tag--patch');
    expect(httpMethodTagClass('DELETE')).toBe('naf-tag--delete');
  });

  it('is case-insensitive', () => {
    expect(httpMethodTagClass('get')).toBe('naf-tag--get');
  });

  it('falls back to the plain tag style for an unlisted verb', () => {
    expect(httpMethodTagClass('OPTIONS')).toBe('');
  });
});
