import { describe, expect, it } from 'vitest';
import { businessDate } from './time';
describe('business reference date São Paulo', () => {
  it('uses calendar in São Paulo rather than browser UTC', () => {
    expect(businessDate(new Date('2026-11-17T01:00:00Z'))).toBe('2026-11-16');
    expect(businessDate(new Date('2026-11-17T04:00:00Z'))).toBe('2026-11-17');
  });
});
