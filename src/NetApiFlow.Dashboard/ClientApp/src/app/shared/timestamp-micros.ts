const MICROSECONDS_PER_MILLISECOND = 1_000;
const MILLISECOND_DIGITS = 3;
const MICROSECOND_DIGITS = 6;
const FRACTION_DIGITS_PATTERN = /\.(\d+)/;

export function parseTimestampMicros(timestamp: string): number {
  const wholeMilliseconds = Date.parse(timestamp);
  const fractionDigits = FRACTION_DIGITS_PATTERN.exec(timestamp)?.[1] ?? '';
  const microsecondsWithinMillisecond = Number(
    fractionDigits.padEnd(MICROSECOND_DIGITS, '0').slice(MILLISECOND_DIGITS, MICROSECOND_DIGITS),
  );

  return wholeMilliseconds * MICROSECONDS_PER_MILLISECOND + microsecondsWithinMillisecond;
}
