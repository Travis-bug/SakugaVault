import { NavLink } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { VaultBrand } from "./VaultBrand";

interface SidebarProps {
  open: boolean;
  onClose: () => void;
}

const navItems = [
  { to: "/", label: "Catalog" },
  { to: "/downloads", label: "Downloads" },
  { to: "/profile", label: "Profile" }
];

export function Sidebar({ open, onClose }: SidebarProps) {
  const { user, logout } = useAuth();

  async function handleLogout() {
    await logout();
    onClose();
  }

  return (
    <>
      <button
        type="button"
        aria-label="Close navigation"
        className={`sidebar-backdrop ${open ? "is-open" : ""}`}
        onClick={onClose}
      />
      <aside className={`sidebar ${open ? "is-open" : ""}`}>
        <div className="sidebar__panel">
          <div className="sidebar__header">
            <div className="sidebar__brand">
              <VaultBrand mode="default" subtitle={`Signed in as ${user?.displayName ?? "Viewer"}`} />
            </div>
            <button type="button" className="sidebar__close" aria-label="Close sidebar" onClick={onClose}>
              <span />
              <span />
            </button>
          </div>
          <nav className="sidebar__nav">
            {navItems.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                className={({ isActive }) => `sidebar__link ${isActive ? "is-active" : ""}`}
                onClick={onClose}
              >
                {item.label}
              </NavLink>
            ))}
          </nav>
          <div className="sidebar__footer">
            <button type="button" className="sidebar__logout" onClick={handleLogout}>
              Logout
            </button>
          </div>
        </div>
      </aside>
    </>
  );
}
