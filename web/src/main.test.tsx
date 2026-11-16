import { describe, expect, it, vi } from 'vitest';

const rootSpies = vi.hoisted(() => ({
  createRoot: vi.fn(),
  render: vi.fn(),
}));

vi.mock('react-dom/client', () => ({
  createRoot: rootSpies.createRoot,
}));

describe('CashFlow bootstrap', () => {
  it('mounts the application into the root element', async () => {
    const rootElement = { id: 'root' };
    const getElementById = vi.fn().mockReturnValue(rootElement);
    rootSpies.createRoot.mockReturnValue({ render: rootSpies.render });
    vi.stubGlobal('document', { getElementById });

    try {
      await import('./main');

      expect(getElementById).toHaveBeenCalledWith('root');
      expect(rootSpies.createRoot).toHaveBeenCalledWith(rootElement);
      expect(rootSpies.render).toHaveBeenCalledOnce();
    } finally {
      vi.unstubAllGlobals();
    }
  });
});
