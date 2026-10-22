import { formatStartedAtFull, formatStartedAtShort } from './started-at-format';

describe('started-at-format', () => {
  const date = new Date(2026, 9, 21, 17, 5, 37, 0);

  it('formats the short stamp as dd/mm hh:mm:ss', () => {
    expect(formatStartedAtShort(date)).toBe('21/10 17:05:37');
  });

  it('formats the full stamp as dd/mm/yyyy hh:mm:ss.sss', () => {
    expect(formatStartedAtFull(date)).toBe('21/10/2026 17:05:37.000');
  });
});
