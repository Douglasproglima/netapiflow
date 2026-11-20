import { parseTimestampMicros } from './timestamp-micros';

describe('parseTimestampMicros', () => {
  const wholeSecond = Date.parse('2026-01-01T00:00:00Z') * 1_000;

  it.each([
    ['2026-01-01T00:00:00Z', 0],
    ['2026-01-01T00:00:00.123Z', 123_000],
    ['2026-01-01T00:00:00.123456Z', 123_456],
    ['2026-01-01T00:00:00.1234567Z', 123_456],
    ['2026-01-01T00:00:00.5Z', 500_000],
    ['2026-01-01T00:00:00.000001Z', 1],
  ])('reads %s as %i microseconds past the second', (timestamp, microsPastTheSecond) => {
    expect(parseTimestampMicros(timestamp)).toBe(wholeSecond + microsPastTheSecond);
  });

  it.each([
    ['2026-01-01T02:00:00.123456+02:00'],
    ['2025-12-31T21:00:00.123456-03:00'],
    ['2026-01-01T02:00:00.1234567+02:00'],
    ['2026-01-01T02:00:00.123+02:00'],
  ])('reads the numeric offset of %s', (timestamp) => {
    const expectedMicrosPastTheSecond = timestamp.includes('.123456') ? 123_456 : 123_000;

    expect(parseTimestampMicros(timestamp)).toBe(wholeSecond + expectedMicrosPastTheSecond);
  });

  it('never hands the fraction to the date parser', () => {
    const parse = vi.spyOn(Date, 'parse');

    parseTimestampMicros('2026-01-01T00:00:00.1234567Z');

    expect(parse).toHaveBeenCalledWith('2026-01-01T00:00:00Z');
    parse.mockRestore();
  });

  it('keeps two timestamps inside the same millisecond apart', () => {
    const earlier = parseTimestampMicros('2026-01-01T00:00:00.123150Z');
    const later = parseTimestampMicros('2026-01-01T00:00:00.123270Z');

    expect(later - earlier).toBe(120);
  });
});
