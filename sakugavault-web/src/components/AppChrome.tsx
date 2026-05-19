import { type ReactNode, useState } from "react";
import { Link, NavLink } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { Sidebar } from "./Sidebar";
import { VaultBrand } from "./VaultBrand";

interface AppChromeProps {
  eyebrow: string;
  title: string;
  description: string;
  actions?: ReactNode;
  children: ReactNode;
}

const navigationLinks = [
  { to: "/search", label: "Search" }
    
];

export function AppChrome({ eyebrow, title, description, actions, children }: AppChromeProps) {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const { user } = useAuth();
  const displayName = user?.displayName?.trim() || "Viewer";

  return (
    <div className="shell">
      <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />
      <div className="shell__aurora shell__aurora--top" />
      <div className="shell__aurora shell__aurora--bottom" />
      <header className="topbar reveal">
        <div className="topbar__left">
          <button
            type="button"
            className="icon-button"
            aria-label={sidebarOpen ? "Close navigation" : "Open navigation"}
            onClick={() => setSidebarOpen((current) => !current)}
          >
            <span />
            <span />
            <span />
          </button>
          <Link to="/" className="topbar__brand-link" aria-label="Go to SakugaVault home">
            <VaultBrand mode="compact" subtitle={null} />
          </Link>
        </div>
        <div className="topbar__right">
          <nav className="topbar__nav" aria-label="Primary">
            {navigationLinks.map((link) => (
              <NavLink
                key={link.to}
                to={link.to}
                className={({ isActive }) => `topbar__nav-link ${isActive ? "is-active" : ""}`}
              >
                {link.label}
              </NavLink>
            ))}
          </nav>
          <div className="topbar__status">
            <p className="topbar__greeting">Welcome, {displayName}</p>
          </div>
          <div className="topbar__actions">{actions}</div>
        </div>
      </header>
      <section className="masthead reveal">
        <div className="masthead__copy">
          <span className="eyebrow">{eyebrow}</span>
          <h1>{title}</h1>
          <p>{description}</p>
        </div>
      </section>
      <main className="content">{children}</main>
    </div>
  );
}
