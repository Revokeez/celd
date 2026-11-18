import { useCallback, useEffect, useState, type JSX } from 'react';
import { api, ApiError } from './api';
import type { CurrentUser } from './types';
import { navigate, safeNext, signInHref, useHashRoute } from './nav';
import { CatalogPage } from './pages/CatalogPage';
import { ItemPage } from './pages/ItemPage';
import { LoginPage } from './pages/LoginPage';
import { MyReservationsPage } from './pages/MyReservationsPage';
import { ReservationPage } from './pages/ReservationPage';
import { StaffQueuePage } from './pages/StaffQueuePage';

export function App() {
  const route = useHashRoute();
  // undefined = still asking the server who we are; null = Visitor.
  const [user, setUser] = useState<CurrentUser | null | undefined>(undefined);

  const refreshUser = useCallback(async () => {
    try {
      setUser(await api.me());
    } catch (error) {
      // 401 just means "Visitor". Anything else we still treat as signed out, safely.
      if (!(error instanceof ApiError) || error.problem.status !== 401) console.warn('Could not load session', error);
      setUser(null);
    }
  }, []);

  useEffect(() => {
    void refreshUser();
  }, [refreshUser]);

  const signOut = async () => {
    try {
      await api.logout();
    } finally {
      setUser(null);
      navigate('/');
    }
  };

  const [, section, id] = route.path.split('/');
  let page: JSX.Element;
  switch (section) {
    case 'items':
      page = <ItemPage itemId={id} user={user ?? null} />;
      break;
    case 'login':
      page = (
        <LoginPage
          onSignedIn={(u) => {
            setUser(u);
            // Go back to where the user was heading (e.g. the item they wanted to reserve).
            navigate(safeNext(route.query.get('next')) ?? (u.role === 'Staff' ? '/staff' : '/'));
          }}
        />
      );
      break;
    case 'mine':
      page = <MyReservationsPage user={user ?? null} />;
      break;
    case 'reservations':
      page = <ReservationPage reservationId={id} user={user ?? null} />;
      break;
    case 'staff':
      page = <StaffQueuePage user={user ?? null} />;
      break;
    default:
      page = <CatalogPage query={route.query} user={user ?? null} />;
  }

  const current = (s: string | undefined) => (section === s ? 'page' : undefined);

  return (
    <>
      <a className="skip" href="#main">Skip to content</a>
      <header className="site-header">
        <nav aria-label="Main">
          <a href="#/" className="brand">Equipment Lending Desk</a>
          <div className="nav-links">
            <a href="#/" aria-current={!section || section === 'items' ? 'page' : undefined}>Catalog</a>
            {/* Navigation only. The server enforces every permission (FR-AUT-003). */}
            {user?.role === 'Student' && <a href="#/mine" aria-current={current('mine')}>My reservations</a>}
            {user?.role === 'Staff' && <a href="#/staff" aria-current={current('staff')}>Staff queue</a>}
          </div>
          <span className="spacer" />
          {user === undefined ? null : user ? (
            <div className="who">
              <span>{user.displayName} <span className="role">({user.role})</span></span>
              <button type="button" className="btn-secondary btn-small" onClick={signOut}>Sign out</button>
            </div>
          ) : (
            section !== 'login' && <a className="btn-link" href={signInHref()}>Sign in</a>
          )}
        </nav>
      </header>
      <main id="main" tabIndex={-1}>{page}</main>
    </>
  );
}
