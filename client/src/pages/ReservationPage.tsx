import { useState } from 'react';
import { api, ApiError, describeError, NoResponseError } from '../api';
import { signInHref } from '../nav';
import type { CurrentUser, Reservation } from '../types';
import {
  ConfirmPanel, ErrorMessage, Loading, Notice, StatusBadge, describeEvent, formatTime, useLoad,
} from '../components/Status';

type Action = {
  label: string;
  busyLabel: string;
  run: (id: string) => Promise<Reservation>;
  done: string;
  /** Destructive actions are confirmed first because they cannot be undone. */
  confirm?: (r: Reservation) => string;
  danger?: boolean;
};

/** Which buttons to SHOW. The server decides what is ALLOWED (BR-AUT-003). */
export function actionsFor(user: CurrentUser | null, r: Reservation): Action[] {
  if (user?.role === 'Student' && r.studentId === user.id && r.status === 'Reserved') {
    return [{
      label: 'Cancel reservation',
      busyLabel: 'Cancelling…',
      run: api.cancel,
      done: 'Reservation cancelled. The item is available to others again.',
      confirm: (res) => `Cancel your reservation for ${res.itemName}? This can't be undone. You would need to reserve it again, if it is still available.`,
      danger: true,
    }];
  }
  if (user?.role === 'Staff' && r.status === 'Reserved') {
    return [{ label: 'Record pickup', busyLabel: 'Recording pickup…', run: api.recordPickup, done: 'Pickup recorded.' }];
  }
  if (user?.role === 'Staff' && r.status === 'CheckedOut') {
    return [{ label: 'Record return', busyLabel: 'Recording return…', run: api.recordReturn, done: 'Return recorded. The item is available again.' }];
  }
  return [];
}

/** UC-003 steps 4-8, UC-004 steps 4-8, UC-005. */
export function ReservationPage({ reservationId, user }: { reservationId: string; user: CurrentUser | null }) {
  const reservation = useLoad(() => api.getReservation(reservationId), [reservationId, user?.id]);
  const history = useLoad(() => api.history(reservationId), [reservationId, user?.id]);
  const [busy, setBusy] = useState<Action | null>(null);
  const [confirming, setConfirming] = useState<Action | null>(null);
  const [message, setMessage] = useState<{ kind: 'success' | 'error' | 'info'; text: string } | null>(null);

  const perform = async (action: Action) => {
    setBusy(action);
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
      setBusy(null);
      setConfirming(null);
    }
  };

  if (!user) return <Notice kind="info"><a href={signInHref()}>Sign in</a> to view reservations.</Notice>;
  if (reservation.status === 'loading' && !reservation.data) return <Loading what="reservation" />;

  const isStaff = user.role === 'Staff';
  const back = isStaff
    ? <p className="back"><a href="#/staff">← Back to staff queue</a></p>
    : <p className="back"><a href="#/mine">← Back to my reservations</a></p>;

  if (reservation.status === 'error') {
    const missing = reservation.error instanceof ApiError && reservation.error.problem.status === 404;
    return missing
      ? <>{back}<Notice kind="info">Reservation not found.</Notice></>
      : <ErrorMessage error={reservation.error} onRetry={reservation.reload} />;
  }

  const r = reservation.data!;
  const actions = actionsFor(user, r);
  return (
    <section aria-labelledby="res-title">
      {back}
      <div className="card">
        <div className="item-head">
          <div>
            <p className="muted eyebrow">Reservation</p>
            <h1 id="res-title">{r.itemName}</h1>
          </div>
          <StatusBadge status={r.status} />
        </div>
        {message && <Notice kind={message.kind}>{message.text}</Notice>}

        <dl className="facts">
          <dt>Asset code</dt><dd><code>{r.assetCode}</code></dd>
          <dt>Student</dt><dd>{r.studentName}</dd>
          <dt>Reserved on</dt><dd>{formatTime(r.createdAtUtc)}</dd>
        </dl>

        {confirming ? (
          <ConfirmPanel
            message={confirming.confirm!(r)}
            confirmLabel={`Yes, ${confirming.label.toLowerCase()}`}
            cancelLabel="No, keep it"
            busy={busy !== null}
            busyLabel={confirming.busyLabel}
            onConfirm={() => perform(confirming)}
            onCancel={() => setConfirming(null)}
          />
        ) : actions.length > 0 && (
          <div className="actions">
            {actions.map((a) => (
              <button
                key={a.label}
                type="button"
                className={a.danger ? 'btn-danger' : undefined}
                onClick={() => (a.confirm ? setConfirming(a) : perform(a))}
                disabled={busy !== null}
              >
                {busy === a ? a.busyLabel : a.label}
              </button>
            ))}
          </div>
        )}
      </div>

      <h2>History</h2>
      {history.status === 'loading' && !history.data && <Loading what="history" />}
      {history.status === 'error' && <ErrorMessage error={history.error} onRetry={history.reload} />}
      {history.data && (
        <ol className="timeline">
          {history.data.map((h, i) => (
            <li key={i}>
              <span className="timeline-what"><strong>{describeEvent(h.eventType)}</strong> by {h.actorName}</span>
              <span className="muted">{formatTime(h.occurredAtUtc)}</span>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
