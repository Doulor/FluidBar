import { useCallback, useRef, useState } from 'react';
import type { PrototypeCommand, PrototypeState } from './protocol';
import { DEFAULT_PROTOTYPE_STATE } from './protocol';
import type { PrototypeFrameHandle } from './PrototypeFrame';

export function usePrototype() {
  const frameRef = useRef<PrototypeFrameHandle>(null);
  const [state, setState] = useState<PrototypeState>(DEFAULT_PROTOTYPE_STATE);

  const send = useCallback((command: Omit<PrototypeCommand, 'type'>) => {
    frameRef.current?.send(command);
  }, []);

  return { frameRef, state, setState, send };
}
