import { useEffect, useState } from "react";
import { Users } from "lucide-react";
import { ApiError, request } from "./api";

interface AdminUser {
  id: string;
  displayName: string;
  email: string;
  roles: string[];
}
const GRANTABLE = [
  { value: "Instructor", label: "Instructor" },
  { value: "CTLReviewer", label: "CTL reviewer" },
  { value: "AcademicAdmin", label: "Academic administrator" },
];

/**
 * SystemAdmin role management (ADR 014). Instructor, CTL reviewer and Academic administrator can be granted or revoked; SystemAdmin
 * itself is managed by deployment. Every change is audited, and the person's session picks it up within a minute.
 */
export function AdminUsers({ onSessionExpired, readOnly = false }: { onSessionExpired: (error: unknown) => void; readOnly?: boolean }) {
  const [users, setUsers] = useState<AdminUser[] | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);

  function fail(e: unknown) {
    if (e instanceof ApiError && e.status === 401) onSessionExpired(e);
    else setError((e as Error).message);
  }
  useEffect(() => {
    request<AdminUser[]>("/admin/users").then(setUsers).catch(fail);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function change(user: AdminUser, role: string, granted: boolean) {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const updated = await request<AdminUser>(`/admin/users/${user.id}/roles`, { role, granted }, "PUT");
      setUsers((current) => current!.map((u) => (u.id === updated.id ? updated : u)));
      const label = GRANTABLE.find((g) => g.value === role)!.label;
      setNotice(`${granted ? "Granted" : "Removed"} ${label} ${granted ? "to" : "from"} ${user.displayName}.`);
    } catch (e) {
      fail(e);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="panel" aria-labelledby="admin-users-title">
      <h2 id="admin-users-title">
        <Users size={18} /> People and roles
      </h2>
      <p className="fine">
        Roles decide what each person can do; course access still needs ownership or an explicit share. Every change is recorded in the audit
        trail. The administrator role itself is managed by your deployment, not here.
      </p>
      {readOnly && (
        <p className="fine" role="note">
          Role changes are turned off in the public demo, so every visitor finds the same personas. Run TeachWell locally to try them.
        </p>
      )}
      {error && (
        <p className="error" role="alert">
          {error}
        </p>
      )}
      {notice && (
        <p className="success" role="status">
          {notice}
        </p>
      )}
      {!users ? (
        !error && <p role="status">Loading people…</p>
      ) : (
        <div className="table-scroll">
          <table className="feedback-table">
            <thead>
              <tr>
                <th scope="col">Person</th>
                {GRANTABLE.map((g) => (
                  <th scope="col" key={g.value}>
                    {g.label}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {users.map((u) => (
                <tr key={u.id}>
                  <th scope="row">
                    {u.displayName} <small>{u.email}</small>
                    {u.roles.includes("SystemAdmin") && <small>System administrator</small>}
                  </th>
                  {GRANTABLE.map((g) => (
                    <td key={g.value}>
                      <input
                        type="checkbox"
                        aria-label={`${g.label}: ${u.displayName}`}
                        checked={u.roles.includes(g.value)}
                        disabled={busy || readOnly}
                        onChange={(e) => change(u, g.value, e.target.checked)}
                      />
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
