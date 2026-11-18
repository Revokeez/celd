import { useState, type FormEvent } from 'react';
import { api, describeError } from '../api';
import type { CurrentUser } from '../types';
import { Notice } from '../components/Status';

/** FR-AUT-001. Accounts are provisioned by seed data; there is no registration. */
export function LoginPage({ onSignedIn }: { onSignedIn: (user: CurrentUser) => void }) {
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      onSignedIn(await api.login(userName, password));
    } catch (e) {
      setError(describeError(e));
    } finally {
      setBusy(false);
      setPassword('');
    }
  };

  return (
    <section aria-labelledby="login-title" className="narrow card login-card">
      <h1 id="login-title">Sign in</h1>
      <p className="muted">Use the account given to you by the lending desk.</p>
      {error && <Notice kind="error">{error}</Notice>}
      <form onSubmit={submit} className="stack">
        <label>
          User name
          <input autoComplete="username" required value={userName} onChange={(e) => setUserName(e.target.value)} />
        </label>
        <label>
          Password
          <input type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} />
        </label>
        <button type="submit" disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button>
      </form>
    </section>
  );
}
