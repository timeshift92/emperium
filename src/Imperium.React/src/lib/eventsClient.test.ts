// Simple test harness for EventsClient batch flush logic
import eventsClient from './eventsClient';
import { describe, it, expect } from 'vitest';

// Basic sanity test: APIs exist.
describe('eventsClient batch api (sanity)', () => {
  it('exposes onEventBatch and onEvent', () => {
    expect(typeof eventsClient.onEvent).toBe('function');
    expect(typeof eventsClient.onEventBatch).toBe('function');
  });
});
