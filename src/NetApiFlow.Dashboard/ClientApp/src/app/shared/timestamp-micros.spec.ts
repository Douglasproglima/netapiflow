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

  it('keeps two timestamps inside the same millisecond apart', () => {
    const earlier = parseTimestampMicros('2026-01-01T00:00:00.123150Z');
    const later = parseTimestampMicros('2026-01-01T00:00:00.123270Z');

    expect(later - earlier).toBe(120);
  });
});
