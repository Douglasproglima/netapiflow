import { formatDurationMicros } from './duration-format';

describe('formatDurationMicros', () => {
  it('renders a dash for null (still running)', () => {
    expect(formatDurationMicros(null)).toBe('—');
  });

  it('renders microseconds under 1000', () => {
    expect(formatDurationMicros(432)).toBe('432 µs');
  });

  it('renders milliseconds with one decimal under 1 second, dropping a trailing .0', () => {
    expect(formatDurationMicros(1_832)).toBe('1.8 ms');
    expect(formatDurationMicros(250_000)).toBe('250 ms');
    expect(formatDurationMicros(98_100)).toBe('98.1 ms');
  });

  it('renders seconds with two decimals at or above 1 second, dropping trailing zeros', () => {
    expect(formatDurationMicros(1_000_000)).toBe('1 s');
    expect(formatDurationMicros(2_500_000)).toBe('2.5 s');
    expect(formatDurationMicros(1_520_000)).toBe('1.52 s');
  });
});
