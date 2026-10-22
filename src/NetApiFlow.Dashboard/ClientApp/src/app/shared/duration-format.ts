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
