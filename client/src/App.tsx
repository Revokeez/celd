import { useCallback, useEffect, useState, type JSX } from 'react';
import { api, ApiError } from './api';
import type { CurrentUser } from './types';
import { CatalogPage } from './pages/CatalogPage';
import { ItemPage } from './pages/ItemPage';
import { LoginPage } from './pages/LoginPage';
import { MyReservationsPage } from './pages/MyReservationsPage';
import { ReservationPage } from './pages/ReservationPage';
import { StaffQueuePage } from './pages/StaffQueuePage';

export const navigate = (path: string) => {
  window.location.hash = path;
};

function useHashRoute(): string {
  const read = () => window.location.hash.replace(/^#/, '') || '/';
  const [route, setRoute] = useState(read);
  useEffect(() => {
    const onChange = () => setRoute(read());
    window.addEventListener('hashchange', onChange);
    return () => window.removeEventListener('hashchange', onChange);
  }, []);
  return route;
}

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

  const [, section, id] = route.split('/');
  let page: JSX.Element;
  switch (section) {
    case 'items':
      page = <ItemPage itemId={id} user={user ?? null} />;
      break;
    case 'login':
      page = <LoginPage onSignedIn={(u) => { setUser(u); navigate(u.role === 'Staff' ? '/staff' : '/'); }} />;
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
      page = <CatalogPage />;
  }

  return (
    <>
      <a className="skip" href="#main">Skip to content</a>
      <header>
        <nav aria-label="Main">
          <a href="#/" className="brand">Equipment Lending Desk</a>
          <a href="#/">Catalog</a>
          {/* Navigation only. The server enforces every permission (FR-AUT-003). */}
          {user?.role === 'Student' && <a href="#/mine">My Reservations</a>}
          {user?.role === 'Staff' && <a href="#/staff">Staff Reservations</a>}
          <span className="spacer" />
          {user === undefined ? null : user ? (
            <>
              <span className="who">{user.displayName} <span className="role">({user.role})</span></span>
              <button type="button" onClick={signOut}>Sign out</button>
            </>
          ) : (
            <a href="#/login">Sign in</a>
          )}
        </nav>
      </header>
      <main id="main" tabIndex={-1}>{page}</main>
    </>
  );
}
