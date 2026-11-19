/** Formats a duration in microseconds for display. Never rounds the underlying value, only the text. */
export function formatDurationMicros(micros: number | null): string {
  if (micros === null) {
    return '—';
  }

  if (micros < 1_000) {
    return `${micros} µs`;
  }

  if (micros < 1_000_000) {
    return `${trimTrailingZeros((micros / 1_000).toFixed(1))} ms`;
  }

  return `${trimTrailingZeros((micros / 1_000_000).toFixed(2))} s`;
}

function trimTrailingZeros(value: string): string {
  return value.includes('.') ? value.replace(/0+$/, '').replace(/\.$/, '') : value;
}

/**
 * Same unit rules as `formatDurationMicros`, but caps the number itself at three significant
 * digits (347 ms, not 346.9 ms) so a value never needs CSS `text-overflow: ellipsis` to fit a
 * narrow metric card — an ellipsis there swallows the unit along with the digits it cuts.
 * Only for displays that tight on space; every other consumer keeps the uncapped precision.
 */
export function formatDurationMicrosCompact(micros: number | null): string {
  if (micros === null) {
    return '—';
  }

  if (micros < 1_000) {
    return `${micros} µs`;
  }

  if (micros < 1_000_000) {
    return `${compactNumber(micros / 1_000)} ms`;
  }

  return `${compactNumber(micros / 1_000_000)} s`;
}

function compactNumber(value: number): string {
  const decimals = value < 10 ? 2 : value < 100 ? 1 : 0;
  return trimTrailingZeros(value.toFixed(decimals));
}
