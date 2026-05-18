import {
  createContext,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode
} from "react";
import { requestJson, ApiError } from "../lib/api";
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
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUserDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const accessTokenRef = useRef<string | null>(null);
  const refreshPromiseRef = useRef<Promise<string> | null>(null);

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

  async function logout() {
    try {
      await requestJson<void>("/api/auth/logout", {
        method: "POST"
      });
    } finally {
      accessTokenRef.current = null;
      setUser(null);
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
        apiRequest
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
