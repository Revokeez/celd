import { useState } from 'react';
import { api } from '../api';
import type { CurrentUser, ReservationStatus } from '../types';
import { Empty, ErrorMessage, Loading, Notice, StatusBadge, formatTime, useLoad } from '../components/Status';

const filters: { value: '' | ReservationStatus; label: string }[] = [
  { value: '', label: 'All statuses' },
  { value: 'Reserved', label: 'Reserved (awaiting pickup)' },
  { value: 'CheckedOut', label: 'Checked out (awaiting return)' },
  { value: 'Cancelled', label: 'Cancelled' },
  { value: 'Returned', label: 'Returned' },
];

/** UC-004 steps 1-3 and UC-005 step 1: the Staff queue (FR-RES-004). */
export function StaffQueuePage({ user }: { user: CurrentUser | null }) {
  const [status, setStatus] = useState<'' | ReservationStatus>('');
  const list = useLoad(
    () => (user?.role === 'Staff' ? api.staffQueue(status || undefined) : Promise.resolve([])),
    [status, user?.id],
  );

  if (user?.role !== 'Staff') return <Notice kind="info">Sign in as lending staff to see the reservation queue.</Notice>;

  return (
    <section aria-labelledby="staff-title">
      <h1 id="staff-title">Staff reservations</h1>
      <label className="filters">
        Show
        <select value={status} onChange={(e) => setStatus(e.target.value as '' | ReservationStatus)}>
          {filters.map((f) => <option key={f.value} value={f.value}>{f.label}</option>)}
        </select>
      </label>

      {list.status === 'loading' && <Loading what="reservations" />}
      {list.status === 'error' && <ErrorMessage error={list.error} onRetry={list.reload} />}
      {list.status === 'ready' && list.data!.length === 0 && <Empty>No reservations match this filter.</Empty>}
      {list.status === 'ready' && list.data!.length > 0 && (
        <table>
          <caption className="sr-only">Reservations</caption>
          <thead>
            <tr><th scope="col">Item</th><th scope="col">Student</th><th scope="col">Status</th><th scope="col">Created</th></tr>
          </thead>
          <tbody>
            {list.data!.map((r) => (
              <tr key={r.id}>
                <td><a href={`#/reservations/${r.id}`}>{r.itemName}</a> <code>{r.assetCode}</code></td>
                <td>{r.studentName}</td>
                <td><StatusBadge status={r.status} /></td>
                <td>{formatTime(r.createdAtUtc)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
