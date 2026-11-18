import { useState } from 'react';
import { api, describeError, NoResponseError } from '../api';
import type { CurrentUser, Reservation } from '../types';
import { Empty, ErrorMessage, ItemCell, Loading, Notice, StatusBadge, formatTime, useLoad } from '../components/Status';

type FilterKey = 'action' | 'Reserved' | 'CheckedOut' | 'Cancelled' | 'Returned' | 'all';

const filters: { key: FilterKey; label: string; match: (r: Reservation) => boolean }[] = [
  { key: 'action', label: 'Needs action', match: (r) => r.isActive },
  { key: 'Reserved', label: 'Awaiting pickup', match: (r) => r.status === 'Reserved' },
  { key: 'CheckedOut', label: 'Awaiting return', match: (r) => r.status === 'CheckedOut' },
  { key: 'Returned', label: 'Returned', match: (r) => r.status === 'Returned' },
  { key: 'Cancelled', label: 'Cancelled', match: (r) => r.status === 'Cancelled' },
  { key: 'all', label: 'All', match: () => true },
];

/** UC-004 steps 1-3 and UC-005 step 1: the Staff queue (FR-RES-004), with pickup/return right in the list. */
export function StaffQueuePage({ user }: { user: CurrentUser | null }) {
  // Staff mostly need what is waiting on them, so that is the default view.
  const [filter, setFilter] = useState<FilterKey>('action');
  const [busyId, setBusyId] = useState<string | null>(null);
  const [message, setMessage] = useState<{ kind: 'success' | 'error' | 'info'; text: string } | null>(null);
  const list = useLoad(
    () => (user?.role === 'Staff' ? api.staffQueue() : Promise.resolve([] as Reservation[])),
    [user?.id],
  );

  if (user?.role !== 'Staff') return <Notice kind="info">Sign in as lending staff to see the reservation queue.</Notice>;

  const record = async (r: Reservation) => {
    const pickup = r.status === 'Reserved';
    setBusyId(r.id);
    setMessage(null);
    try {
      await (pickup ? api.recordPickup(r.id) : api.recordReturn(r.id));
      setMessage({
        kind: 'success',
        text: pickup ? `Pickup recorded: ${r.itemName} for ${r.studentName}.` : `Return recorded: ${r.itemName} is available again.`,
      });
    } catch (error) {
      setMessage(error instanceof NoResponseError
        ? { kind: 'info', text: 'No response from the server. The list below shows the current saved state; check it before trying again.' }
        : { kind: 'error', text: describeError(error) });
    } finally {
      await list.reload(); // Always re-read the authoritative state (BR-RES-007).
      setBusyId(null);
    }
  };

  const all = list.data ?? [];
  const current = filters.find((f) => f.key === filter)!;
  const rows = all
    .filter(current.match)
    // Waiting work comes first, oldest first (serve in order); finished reservations follow, newest first.
    .sort((a, b) => {
      if (a.isActive !== b.isActive) return a.isActive ? -1 : 1;
      const byDate = a.createdAtUtc.localeCompare(b.createdAtUtc);
      return a.isActive ? byDate : -byDate;
    });

  return (
    <section aria-labelledby="staff-title">
      <h1 id="staff-title">Staff queue</h1>

      <div className="tabs" role="group" aria-label="Show reservations">
        {filters.map((f) => (
          <button
            key={f.key}
            type="button"
            className="tab"
            aria-pressed={filter === f.key}
            onClick={() => setFilter(f.key)}
          >
            {f.label}
            {list.data && <span className="count">{all.filter(f.match).length}</span>}
          </button>
        ))}
      </div>

      {message && <Notice kind={message.kind}>{message.text}</Notice>}

      {list.status === 'loading' && !list.data && <Loading what="reservations" />}
      {list.status === 'error' && <ErrorMessage error={list.error} onRetry={list.reload} />}
      {list.data && rows.length === 0 && (
        <Empty>{filter === 'action' ? 'Nothing is waiting. All reservations are picked up or finished.' : 'No reservations match this filter.'}</Empty>
      )}
      {list.data && rows.length > 0 && (
        <div className="table-wrap">
          <table className="data">
            <caption className="sr-only">{current.label}</caption>
            <thead>
              <tr>
                <th scope="col">Item</th>
                <th scope="col">Student</th>
                <th scope="col">Status</th>
                <th scope="col">Reserved on</th>
                <th scope="col"><span className="sr-only">Action</span></th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id}>
                  <td><ItemCell name={r.itemName} assetCode={r.assetCode} href={`#/reservations/${r.id}`} /></td>
                  <td>{r.studentName}</td>
                  <td><StatusBadge status={r.status} /></td>
                  <td className="nowrap">{formatTime(r.createdAtUtc)}</td>
                  <td className="row-action">
                    {r.isActive && (
                      <button
                        type="button"
                        className="btn-small"
                        onClick={() => record(r)}
                        disabled={busyId !== null}
                        aria-label={`${r.status === 'Reserved' ? 'Record pickup' : 'Record return'} for ${r.itemName}, ${r.studentName}`}
                      >
                        {busyId === r.id
                          ? (r.status === 'Reserved' ? 'Recording pickup…' : 'Recording return…')
                          : (r.status === 'Reserved' ? 'Record pickup' : 'Record return')}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
