const MICROSECONDS_PER_MILLISECOND = 1_000;
const MICROSECOND_DIGITS = 6;
const FRACTION_PATTERN = /\.(\d+)/;

export function parseTimestampMicros(timestamp: string): number {
  const fractionDigits = FRACTION_PATTERN.exec(timestamp)?.[1] ?? '';
  const wholeSecondMilliseconds = Date.parse(timestamp.replace(FRACTION_PATTERN, ''));
  const microsecondsIntoTheSecond = Number(
    fractionDigits.padEnd(MICROSECOND_DIGITS, '0').slice(0, MICROSECOND_DIGITS),
  );

  return wholeSecondMilliseconds * MICROSECONDS_PER_MILLISECOND + microsecondsIntoTheSecond;
}
