import { DependencyList, useCallback, useEffect, useRef, useState } from "react";

export function useAsync<T>(fn: (signal: AbortSignal) => Promise<T>, deps: DependencyList) {
  const [state, setState] = useState<{ data?: T; error?: unknown; loading: boolean }>({ loading: true });
  const [tick, setTick] = useState(0);
  const fnRef = useRef(fn);
  fnRef.current = fn;
  useEffect(() => {
    const ctrl = new AbortController();
    setState((s) => ({ data: s.data, loading: true }));
    fnRef.current(ctrl.signal).then(
      (data) => !ctrl.signal.aborted && setState({ data, loading: false }),
      (error) => !ctrl.signal.aborted && (error as Error)?.name !== "AbortError" && setState({ error, loading: false }),
    );
    return () => ctrl.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick]);
  const reload = useCallback(() => setTick((n) => n + 1), []);
  return { ...state, reload };
}
