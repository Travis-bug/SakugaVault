import { FormEvent, useState } from "react";
import { Navigate, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { isApiError } from "../lib/api";
import { VaultBrand } from "../components/VaultBrand";

type AuthMode = "login" | "register";

const initialRegisterState = {
  displayName: "",
  userName: "",
  email: "",
  password: ""
};

const initialLoginState = {
  identifier: "",
  password: ""
};

export function LoginPage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [mode, setMode] = useState<AuthMode>("login");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [loginForm, setLoginForm] = useState(initialLoginState);
  const [registerForm, setRegisterForm] = useState(initialRegisterState);

  if (!auth.isLoading && auth.isAuthenticated) {
    const destination = (location.state as { from?: string } | null)?.from ?? "/";
    return <Navigate to={destination} replace />;
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      if (mode === "login") {
        await auth.login(loginForm);
      } else {
        await auth.register(registerForm);
      }

      const destination = (location.state as { from?: string } | null)?.from ?? "/";
      navigate(destination, { replace: true });
    } catch (error) {
      if (isApiError(error)) {
        setErrorMessage(error.message);
      } else if (error instanceof Error) {
        setErrorMessage(error.message);
      } else {
        setErrorMessage("Something went wrong while opening the vault.");
      }
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <div className="login-screen">
      <div className="login-screen__art">
        <div className="login-screen__art-copy reveal">
          <VaultBrand mode="hero" subtitle="Cinematic anime relay, tuned for warm signal discovery" />
          <h2>Provider-fed discovery, resilient sessions, and a vault built to feel alive.</h2>
          <div className="login-screen__highlights">
            <span>Live catalog pulls</span>
            <span>Fast resume memory</span>
            <span>Warm-tech visual system</span>
          </div>
        </div>
      </div>
      <div className="login-card reveal">
        <span className="eyebrow">Access Node</span>
        <h1>Enter the Vault</h1>
        <p>
          Sign in to restore your queue, comments, and watch memory across every session.
        </p>

        <div className="auth-toggle">
          <button type="button" className={`auth-toggle__button ${mode === "login" ? "is-active" : ""}`}
            onClick={() => setMode("login")}
           >
            Sign In
          </button>
          
          <button
            type="button"
            className={`auth-toggle__button ${mode === "register" ? "is-active" : ""}`}
            onClick={() => setMode("register")}
          >
            Create Account
          </button>
        </div>

        <form className="auth-form" onSubmit={handleSubmit}>
          {mode === "register" ? (
            <>
              <label>
                Display Name
                <input
                  value={registerForm.displayName}
                  onChange={(event) =>
                    setRegisterForm((current) => ({ ...current, displayName: event.target.value }))
                  }
                  required
                  minLength={2}
                />
              </label>
              <label>
                Username
                <input
                  value={registerForm.userName}
                  onChange={(event) =>
                    setRegisterForm((current) => ({ ...current, userName: event.target.value }))
                  }
                  required
                  minLength={3}
                />
              </label>
              <label>
                Email
                <input
                  type="email"
                  value={registerForm.email}
                  onChange={(event) =>
                    setRegisterForm((current) => ({ ...current, email: event.target.value }))
                  }
                  required
                />
              </label>
              <label>
                Password
                <input
                  type="password"
                  value={registerForm.password}
                  onChange={(event) =>
                    setRegisterForm((current) => ({ ...current, password: event.target.value }))
                  }
                  required
                  minLength={8}
                />
              </label>
            </>
          ) : (
            <>
              <label>
                Username or Email
                <input
                  value={loginForm.identifier}
                  onChange={(event) =>
                    setLoginForm((current) => ({ ...current, identifier: event.target.value }))
                  }
                  required
                />
              </label>
              <label>
                Password
                <input
                  type="password"
                  value={loginForm.password}
                  onChange={(event) =>
                    setLoginForm((current) => ({ ...current, password: event.target.value }))
                  }
                  required
                  minLength={8}
                />
              </label>
            </>
          )}

          {errorMessage ? <div className="form-error">{errorMessage}</div> : null}

          <button type="submit" className="button auth-form__submit" disabled={isSubmitting}>
            {isSubmitting ? "Opening Vault..." : mode === "login" ? "Enter Catalog" : "Create Vault Access"}
          </button>
        </form>
      </div>
    </div>
  );
}
