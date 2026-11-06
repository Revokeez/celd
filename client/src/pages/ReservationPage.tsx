import { useState } from 'react';
import { api, ApiError, describeError, NoResponseError } from '../api';
import type { CurrentUser, Reservation } from '../types';
import { ErrorMessage, Loading, Notice, StatusBadge, formatTime, useLoad } from '../components/Status';

type Action = { label: string; run: (id: string) => Promise<Reservation>; done: string };

/** Which buttons to SHOW. The server decides what is ALLOWED (BR-AUT-003). */
function actionsFor(user: CurrentUser | null, r: Reservation): Action[] {
  if (user?.role === 'Student' && r.studentId === user.id && r.status === 'Reserved') {
    return [{ label: 'Cancel reservation', run: api.cancel, done: 'Reservation cancelled.' }];
  }
  if (user?.role === 'Staff' && r.status === 'Reserved') {
    return [{ label: 'Record pickup', run: api.recordPickup, done: 'Pickup recorded.' }];
  }
  if (user?.role === 'Staff' && r.status === 'CheckedOut') {
    return [{ label: 'Record return', run: api.recordReturn, done: 'Return recorded. The item is available again.' }];
  }
  return [];
}

/** UC-003 steps 4-8, UC-004 steps 4-8, UC-005. */
export function ReservationPage({ reservationId, user }: { reservationId: string; user: CurrentUser | null }) {
  const reservation = useLoad(() => api.getReservation(reservationId), [reservationId, user?.id]);
  const history = useLoad(() => api.history(reservationId), [reservationId, user?.id]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ kind: 'success' | 'error' | 'info'; text: string } | null>(null);

  const perform = async (action: Action) => {
    setBusy(true);
    setMessage(null);
    try {
      await action.run(reservationId);
      setMessage({ kind: 'success', text: action.done });
    } catch (error) {
      if (error instanceof NoResponseError) {
        setMessage({ kind: 'info', text: 'No response from the server. The page below shows the current saved state; check it before trying again.' });
      } else {
        setMessage({ kind: 'error', text: describeError(error) });
      }
    } finally {
      // Success, conflict or silence: always re-read the authoritative state (BR-RES-007, E6.2, E8.1).
      await Promise.all([reservation.reload(), history.reload()]);
      setBusy(false);
    }
  };

  if (!user) return <Notice kind="info"><a href="#/login">Sign in</a> to view reservations.</Notice>;
  if (reservation.status === 'loading' && !reservation.data) return <Loading what="reservation" />;
  if (reservation.status === 'error') {
    const missing = reservation.error instanceof ApiError && reservation.error.problem.status === 404;
    return missing
      ? <Notice kind="info">Reservation not found.</Notice>
      : <ErrorMessage error={reservation.error} onRetry={reservation.reload} />;
  }

  const r = reservation.data!;
  const back = user.role === 'Staff' ? '#/staff' : '#/mine';
  return (
    <section aria-labelledby="res-title">
      <p><a href={back}>← Back</a></p>
      <h1 id="res-title">Reservation: {r.itemName}</h1>
      {message && <Notice kind={message.kind}>{message.text}</Notice>}

      <dl className="facts">
        <dt>Status</dt><dd><StatusBadge status={r.status} /></dd>
        <dt>Item</dt><dd>{r.itemName} <code>{r.assetCode}</code></dd>
        <dt>Student</dt><dd>{r.studentName}</dd>
        <dt>Created</dt><dd>{formatTime(r.createdAtUtc)}</dd>
      </dl>

      <div className="actions">
        {actionsFor(user, r).map((a) => (
          <button key={a.label} type="button" onClick={() => perform(a)} disabled={busy}>
            {busy ? 'Working…' : a.label}
          </button>
        ))}
      </div>

      <h2>History</h2>
      {history.status === 'loading' && !history.data && <Loading what="history" />}
      {history.status === 'error' && <ErrorMessage error={history.error} onRetry={history.reload} />}
      {history.data && (
        <ol className="history">
          {history.data.map((h, i) => (
            <li key={i}>
              <strong>{h.eventType}</strong> → <StatusBadge status={h.resultingStatus} /> by {h.actorName}, {formatTime(h.occurredAtUtc)}
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
