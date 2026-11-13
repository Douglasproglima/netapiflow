import { statusTint } from './status-tint';

describe('statusTint', () => {
  it('maps Error to danger', () => {
    expect(statusTint('Error')).toBe('danger');
  });

  it('maps Running to flow', () => {
    expect(statusTint('Running')).toBe('flow');
  });

  it('maps Success to success', () => {
    expect(statusTint('Success')).toBe('success');
  });

  it('maps Timeout to danger', () => {
    expect(statusTint('Timeout')).toBe('danger');
  });

  it('maps Cancelled to idle', () => {
    expect(statusTint('Cancelled')).toBe('idle');
  });
});
