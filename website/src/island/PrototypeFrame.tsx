import {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
} from 'react';
import type { PrototypeCommand, PrototypeState } from './protocol';
import { isPrototypeStateMessage, makeCommand } from './protocol';
import './prototype-frame.css';

const FRAME_WIDTH = 488;
const FRAME_HEIGHT = 444;

export interface PrototypeFrameHandle {
  send: (command: Omit<PrototypeCommand, 'type'>) => void;
  reload: () => void;
}

interface PrototypeFrameProps {
  title: string;
  name?: string;
  poster?: string;
  className?: string;
  onState?: (state: PrototypeState) => void;
  onReady?: () => void;
  lazy?: boolean;
  defer?: boolean;
}

function scheduleIdle(callback: () => void) {
  if (typeof window === 'undefined') return () => {};
  const w = window as Window & {
    requestIdleCallback?: (cb: () => void, options?: { timeout: number }) => number;
    cancelIdleCallback?: (id: number) => void;
  };
  if (w.requestIdleCallback) {
    const id = w.requestIdleCallback(callback, { timeout: 1800 });
    return () => w.cancelIdleCallback?.(id);
  }
  const id = window.setTimeout(callback, 700);
  return () => window.clearTimeout(id);
}

export const PrototypeFrame = forwardRef<PrototypeFrameHandle, PrototypeFrameProps>(
  function PrototypeFrame(
    { title, name, poster, className = '', onState, onReady, lazy = false, defer = false },
    forwardedRef,
  ) {
    const wrapperRef = useRef<HTMLDivElement>(null);
    const iframeRef = useRef<HTMLIFrameElement>(null);
    const pendingRef = useRef<Omit<PrototypeCommand, 'type'>[]>([]);
    const [scale, setScale] = useState(1);
    const [ready, setReady] = useState(false);
    const [failed, setFailed] = useState(false);
    const [shouldLoad, setShouldLoad] = useState(!lazy && !defer);
    const [attempt, setAttempt] = useState(0);

    const send = useCallback((command: Omit<PrototypeCommand, 'type'>) => {
      const target = iframeRef.current?.contentWindow;
      if (!target) {
        pendingRef.current.push(command);
        return;
      }
      target.postMessage(makeCommand(command), window.location.origin);
    }, []);

    useImperativeHandle(
      forwardedRef,
      () => ({
        send,
        reload: () => setAttempt((value) => value + 1),
      }),
      [send],
    );

    useLayoutEffect(() => {
      const element = wrapperRef.current;
      if (!element) return;
      const update = () => {
        const width = element.getBoundingClientRect().width;
        setScale(Math.min(1, Math.max(0.4, width / FRAME_WIDTH)));
      };
      update();
      const observer = new ResizeObserver(update);
      observer.observe(element);
      return () => observer.disconnect();
    }, []);

    useEffect(() => {
      if (!lazy) return;
      const element = wrapperRef.current;
      if (!element) return;
      const observer = new IntersectionObserver(
        (entries) => {
          if (entries.some((entry) => entry.isIntersecting)) {
            setShouldLoad(true);
            observer.disconnect();
          }
        },
        { rootMargin: '600px 0px' },
      );
      observer.observe(element);
      return () => observer.disconnect();
    }, [lazy]);

    useEffect(() => {
      if (!defer) return;
      return scheduleIdle(() => setShouldLoad(true));
    }, [defer]);

    useEffect(() => {
      const handleMessage = (event: MessageEvent) => {
        if (event.origin !== window.location.origin) return;
        if (event.source !== iframeRef.current?.contentWindow) return;
        if (!isPrototypeStateMessage(event.data)) return;

        const next = event.data.state;
        setReady(next.ready);
        setFailed(false);
        onState?.(next);
        if (next.ready) {
          onReady?.();
          const queue = pendingRef.current;
          pendingRef.current = [];
          queue.forEach((command) => {
            iframeRef.current?.contentWindow?.postMessage(
              makeCommand(command),
              window.location.origin,
            );
          });
        }
      };

      window.addEventListener('message', handleMessage);
      return () => window.removeEventListener('message', handleMessage);
    }, [onReady, onState]);

    useEffect(() => {
      if (ready) return;
      const timeout = window.setTimeout(() => {
        if (!ready) setFailed(true);
      }, 9000);
      return () => window.clearTimeout(timeout);
    }, [ready, attempt]);



    const frameStyle: CSSProperties = {
      width: FRAME_WIDTH,
      height: FRAME_HEIGHT,
      transform: `scale(${scale})`,
    };
    const wrapperStyle: CSSProperties = { height: FRAME_HEIGHT * scale };

    return (
      <div
        ref={wrapperRef}
        className={`prototype-frame ${ready ? 'is-ready' : ''} ${className}`.trim()}
        style={wrapperStyle}
      >
        {poster ? (
          <img className="prototype-frame__poster" src={poster} alt="" width={FRAME_WIDTH} height={FRAME_HEIGHT} />
        ) : (
          <div className="prototype-frame__poster prototype-frame__poster--empty" aria-hidden="true" />
        )}

        {shouldLoad && !failed ? (
          <iframe
            key={attempt}
            ref={iframeRef}
            className="prototype-frame__iframe"
            title={title}
            name={name}
            src={`${import.meta.env.BASE_URL}island/embed.html`}
            style={frameStyle}
            loading="eager"
          />
        ) : null}

        {!ready ? (
          <div className="prototype-frame__loading" aria-hidden="true">
            <span />
            <span />
            <span />
          </div>
        ) : null}

        {failed ? (
          <div className="prototype-frame__fallback" role="status">
            <strong>原型暂时无法启动</strong>
            <a href={`${import.meta.env.BASE_URL}island/embed.html`} target="_blank" rel="noreferrer">
              在新窗口打开真实原型
            </a>
          </div>
        ) : null}
      </div>
    );
  },
);
