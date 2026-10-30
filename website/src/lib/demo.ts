export type DemoPage = 'media' | 'notifications' | 'clipboard' | 'agent' | 'widgets';
export const DEMO_PAGES: Record<DemoPage, number> = {
  media: 1, notifications: 2, clipboard: 3, agent: 4, widgets: 5,
};

export function openDemo(page: DemoPage) {
  window.dispatchEvent(new CustomEvent('fluidbar:demo', { detail: page }));
  document.getElementById('story')?.scrollIntoView({ behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: 'start' });
}
