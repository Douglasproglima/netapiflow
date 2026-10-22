function pad(value: number, length = 2): string {
  return String(value).padStart(length, '0');
}

/** Short local-time stamp for the history table: `21/10 17:05:37`. */
export function formatStartedAtShort(date: Date): string {
  return `${pad(date.getDate())}/${pad(date.getMonth() + 1)} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

/** Full local-time stamp for the `title` attribute: `21/10/2026 17:05:37.000`. */
export function formatStartedAtFull(date: Date): string {
  return `${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}.${pad(date.getMilliseconds(), 3)}`;
}
