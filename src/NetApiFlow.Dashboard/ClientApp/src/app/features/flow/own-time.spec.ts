import { FlowSpan } from '../../core/models';
import { ownTimeOfEachStep } from './own-time';

function step(
  spanId: string,
  parentSpanId: string | null,
  durationMicros: number | null,
): FlowSpan {
  return {
    spanId,
    parentSpanId,
    sequence: 0,
    component: 'Component',
    operation: 'Operation',
    type: 'Custom',
    startedAt: '2026-01-01T00:00:00.000000Z',
    endedAt: null,
    durationMicros,
    status: 'Success',
    errorType: null,
    errorMessage: null,
    isErrorOrigin: false,
    tags: {},
    logs: [],
  };
}

describe('ownTimeOfEachStep', () => {
  it('gives a step without children all of its duration', () => {
    const own = ownTimeOfEachStep([step('only', null, 120)]);

    expect(own.get('only')).toBe(120);
  });

  it('takes the time of the direct children out of the parent, and only of the direct ones', () => {
    const own = ownTimeOfEachStep([
      step('root', null, 1000),
      step('controller', 'root', 900),
      step('query', 'controller', 5),
    ]);

    expect(own.get('root')).toBe(100);
    expect(own.get('controller')).toBe(895);
    expect(own.get('query')).toBe(5);
  });

  it('gives a parent whose parallel children add up to more than it a time of zero, never a negative one', () => {
    const own = ownTimeOfEachStep([
      step('root', null, 400),
      step('first', 'root', 380),
      step('second', 'root', 370),
    ]);

    expect(own.get('root')).toBe(0);
  });

  it('counts a step that has no duration yet as zero, for it and for its parent', () => {
    const own = ownTimeOfEachStep([step('root', null, 50), step('running', 'root', null)]);

    expect(own.get('root')).toBe(50);
    expect(own.get('running')).toBe(0);
  });
});
