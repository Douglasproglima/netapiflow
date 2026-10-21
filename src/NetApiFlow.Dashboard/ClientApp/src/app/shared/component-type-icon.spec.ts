import { componentTypeIcon } from './component-type-icon';

describe('componentTypeIcon', () => {
  it('maps every ComponentType to its own icon', () => {
    expect(componentTypeIcon('Endpoint')).toBe('endpoint');
    expect(componentTypeIcon('Controller')).toBe('controller');
    expect(componentTypeIcon('UseCase')).toBe('use-case');
    expect(componentTypeIcon('Service')).toBe('service');
    expect(componentTypeIcon('Repository')).toBe('repository');
    expect(componentTypeIcon('Database')).toBe('database');
    expect(componentTypeIcon('Http')).toBe('http');
    expect(componentTypeIcon('Custom')).toBe('custom');
  });
});
