import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode
} from "react";
import { useNavigate } from "react-router-dom";
import { requestJson, ApiError } from "../lib/api";
import { resolveApiUrl } from "../lib/config";
import type {
  AuthResponseDto,
  CurrentUserDto,
  LoginRequestDto,
  RegisterRequestDto
} from "../lib/types";

interface AuthorizedRequestOptions {
  method?: string;
  body?: unknown;
  signal?: AbortSignal;
}

interface AuthContextValue {
  user: CurrentUserDto | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (request: LoginRequestDto) => Promise<void>;
  register: (request: RegisterRequestDto) => Promise<void>;
  logout: () => Promise<void>;
  apiRequest: <T>(path: string, options?: AuthorizedRequestOptions) => Promise<T>;
  apiKeepalive: (path: string, body?: unknown) => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUserDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const accessTokenRef = useRef<string | null>(null);
  const refreshPromiseRef = useRef<Promise<string> | null>(null);
  const navigate = useNavigate();

  useEffect(() => {
    let cancelled = false;

    async function hydrateSession() {
      try
      {
        const response = await requestJson<AuthResponseDto>("/api/auth/refresh", {
          method: "POST"
        });

        if (!cancelled) {
          accessTokenRef.current = response.accessToken;
          setUser(response.user);
        }
      }
      catch (error)
      {
        if (!cancelled && error instanceof ApiError && (error.status === 401 || error.status === 404)) {
          accessTokenRef.current = null;
          setUser(null);
        }
      }
      finally
      {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    }

    void hydrateSession();

    return () => {
      cancelled = true;
    };
  }, []);

  async function login(request: LoginRequestDto) {
    const response = await requestJson<AuthResponseDto>("/api/auth/login", {
      method: "POST",
      body: request
    });

    accessTokenRef.current = response.accessToken;
    setUser(response.user);
  }

  async function register(request: RegisterRequestDto) {
    const response = await requestJson<AuthResponseDto>("/api/auth/register", {
      method: "POST",
      body: request
    });

    accessTokenRef.current = response.accessToken;
    setUser(response.user);
  }

  const apiKeepalive = useCallback((path: string, body?: unknown) => {
    const headers = new Headers();
    headers.set("Accept", "application/json");

    if (body !== undefined) {
      headers.set("Content-Type", "application/json");
    }

    if (accessTokenRef.current) {
      headers.set("Authorization", `Bearer ${accessTokenRef.current}`);
    }

    fetch(resolveApiUrl(path), {
      method: "POST",
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      credentials: "include",
      keepalive: true
    }).catch(() => {
      // Navigation-time telemetry must not interrupt the user.
    });
  }, []);

  async function logout() {
    try {
      await requestJson<void>("/api/auth/logout", {
        method: "POST"
      });
    } finally {
      //clears credentials regradless of server response
      accessTokenRef.current = null;
      setUser(null);

      // Navigate to /login with a clean state object.
      // replace: true means the watch page (or wherever the user was) is removed
      // from the browser history stack, so the back button doesn't return them
      // to a protected page after logout.
      // state: {} is an explicit empty state so location.state?.from is undefined
      // on the next login, which sends the new user to / instead of wherever
      // the previous user was watching.
      navigate("/login", { replace: true, state: {} });
    }
  }

  async function refreshAccessToken() {
    if (!refreshPromiseRef.current) {
      refreshPromiseRef.current = requestJson<AuthResponseDto>("/api/auth/refresh", {
        method: "POST"
      })
        .then((response) => {
          accessTokenRef.current = response.accessToken;
          setUser(response.user);
          return response.accessToken;
        })
        .finally(() => {
          refreshPromiseRef.current = null;
        });
    }

    return await refreshPromiseRef.current;
  }

  async function apiRequest<T>(path: string, options: AuthorizedRequestOptions = {}) {
    try {
      return await requestJson<T>(path, {
        method: options.method,
        body: options.body,
        signal: options.signal,
        accessToken: accessTokenRef.current
      });
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        try {
          const refreshedAccessToken = await refreshAccessToken();
          return await requestJson<T>(path, {
            method: options.method,
            body: options.body,
            signal: options.signal,
            accessToken: refreshedAccessToken
          });
        } catch (refreshError) {
          accessTokenRef.current = null;
          setUser(null);
          throw refreshError;
        }
      }

      throw error;
    }
  }

  return (
    <AuthContext.Provider
      value={{
        user,
        isAuthenticated: user !== null,
        isLoading,
        login,
        register,
        logout,
        apiRequest,
        apiKeepalive
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within AuthProvider.");
  }

  return context;
}
