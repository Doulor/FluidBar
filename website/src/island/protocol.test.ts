import { describe, expect, it } from 'vitest';
import {
  DEFAULT_PROTOTYPE_STATE,
  isPrototypeState,
  isPrototypeStateMessage,
  normalizePage,
} from './protocol';

describe('prototype protocol', () => {
  it('accepts the real state shape', () => {
    expect(isPrototypeState(DEFAULT_PROTOTYPE_STATE)).toBe(true);
  });

  it('rejects invented compact views', () => {
    expect(isPrototypeState({ ...DEFAULT_PROTOTYPE_STATE, view: 'preview' })).toBe(false);
  });

  it('rejects out-of-range pages', () => {
    expect(isPrototypeState({ ...DEFAULT_PROTOTYPE_STATE, page: 6 })).toBe(false);
  });

  it('normalizes page values', () => {
    expect(normalizePage(-4)).toBe(0);
    expect(normalizePage(2.8)).toBe(2);
    expect(normalizePage(99)).toBe(5);
    expect(normalizePage(Number.NaN)).toBe(0);
  });

  it('validates state messages before they reach React', () => {
    expect(
      isPrototypeStateMessage({ type: 'fluidbar:state', state: DEFAULT_PROTOTYPE_STATE }),
    ).toBe(true);
    expect(isPrototypeStateMessage({ type: 'fluidbar:set', state: {} })).toBe(false);
  });
});
