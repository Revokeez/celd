import { useCallback, useEffect, useState, type DependencyList, type ReactNode } from 'react';
import { describeError } from '../api';
import type { ReservationStatus } from '../types';

/** Loads data and exposes loading / ready / error states plus a reload (FR-ERR-002). */
export function useLoad<T>(load: () => Promise<T>, deps: DependencyList) {
  const [state, setState] = useState<{ status: 'loading' | 'ready' | 'error'; data?: T; error?: unknown }>({ status: 'loading' });

  // eslint-disable-next-line react-hooks/exhaustive-deps
  const run = useCallback(load, deps);

  const reload = useCallback(async () => {
    setState((s) => ({ ...s, status: 'loading' }));
    try {
      setState({ status: 'ready', data: await run() });
    } catch (error) {
      setState({ status: 'error', error });
    }
  }, [run]);

  useEffect(() => {
    void reload();
  }, [reload]);

  return { ...state, reload };
}

export function Loading({ what }: { what: string }) {
  return <p role="status" aria-live="polite" className="muted">Loading {what}…</p>;
}

export function Empty({ children }: { children: ReactNode }) {
  return <p role="status" className="empty">{children}</p>;
}

export function ErrorMessage({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  return (
    <div role="alert" className="banner error">
      <strong>Error: </strong>{describeError(error)}
      {onRetry && <button type="button" onClick={onRetry}>Try again</button>}
    </div>
  );
}

export function Notice({ kind, children }: { kind: 'success' | 'info' | 'error'; children: ReactNode }) {
  return (
    <div role={kind === 'error' ? 'alert' : 'status'} aria-live="polite" className={`banner ${kind}`}>
      {kind === 'success' && <strong>Done: </strong>}
      {kind === 'error' && <strong>Not completed: </strong>}
      {children}
    </div>
  );
}

/** Status is always written out as text, never shown by colour alone (NFR-USAB-001). */
export function AvailabilityBadge({ available }: { available: boolean }) {
  return <span className={`badge ${available ? 'ok' : 'no'}`}>{available ? '✓ Available' : '✕ Unavailable'}</span>;
}

const statusLabel: Record<ReservationStatus, string> = {
  Reserved: 'Reserved',
  CheckedOut: 'Checked out',
  Cancelled: 'Cancelled',
  Returned: 'Returned',
};

export function StatusBadge({ status }: { status: ReservationStatus }) {
  return <span className={`badge status-${status}`}>{statusLabel[status]}</span>;
}

export function formatTime(utc: string): string {
  const date = new Date(utc);

  if (Number.isNaN(date.getTime())) {
    return 'Invalid date';
  }

  return date.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    timeZoneName: 'short'
  });
}