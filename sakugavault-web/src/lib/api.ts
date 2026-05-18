import { resolveApiUrl } from "./config";

export class ApiError extends Error {
  readonly status: number;
  readonly detail?: string;
  readonly title?: string;

  constructor(status: number, message: string, detail?: string, title?: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.detail = detail;
    this.title = title;
  }
}

interface ApiRequestOptions {
  method?: string;
  body?: unknown;
  signal?: AbortSignal;
  accessToken?: string | null;
}

type ProblemPayload = {
  detail?: unknown;
  title?: unknown;
  errors?: Record<string, unknown>;
};

async function readPayload(response: Response) {
  const text = await response.text();
  if (!text) {
    return undefined;
  }

  try {
    return JSON.parse(text) as Record<string, unknown>;
  } catch {
    return text;
  }
}

function buildHeaders(body?: unknown, accessToken?: string | null) {
  const headers = new Headers();
  headers.set("Accept", "application/json");

  if (body !== undefined) {
    headers.set("Content-Type", "application/json");
  }

  if (accessToken) {
    headers.set("Authorization", `Bearer ${accessToken}`);
  }

  return headers;
}

function fallbackMessageForStatus(status: number) {
  if (status === 400) {
    return "Some fields need attention. Review your input and try again.";
  }

  if (status === 401) {
    return "Your session or credentials were rejected. Try signing in again.";
  }

  if (status === 403) {
    return "You do not have permission to do that.";
  }

  if (status === 404) {
    return "The requested item could not be found.";
  }

  if (status === 409) {
    return "That request conflicts with existing data. Review your input and try again.";
  }

  if (status === 429) {
    return "Too many attempts were made. Wait a moment and try again.";
  }

  if (status >= 500) {
    return "The server hit a problem. Please try again in a moment.";
  }

  return `Request failed with status ${status}`;
}

function looksTechnical(message: string) {
  const normalized = message.trim().toLowerCase();
  return (
    normalized.startsWith("<!doctype html") ||
    normalized.startsWith("<html") ||
    normalized.includes("sakugavault.contracts") ||
    normalized.includes("record type") ||
    normalized.includes("stack trace") ||
    normalized.includes("system.") ||
    normalized.includes("exception")
  );
}

function readFirstValidationMessage(payload: unknown) {
  if (!payload || typeof payload !== "object" || !("errors" in payload)) {
    return undefined;
  }

  const { errors } = payload as ProblemPayload;
  if (!errors || typeof errors !== "object") {
    return undefined;
  }

  for (const value of Object.values(errors)) {
    if (!Array.isArray(value)) {
      continue;
    }

    const firstMessage = value.find(
      (entry): entry is string => typeof entry === "string" && entry.trim().length > 0
    );

    if (firstMessage) {
      return firstMessage;
    }
  }

  return undefined;
}

function buildErrorMessage(status: number, payload: unknown) {
  const validationMessage = readFirstValidationMessage(payload);
  if (validationMessage) {
    return validationMessage;
  }

  if (typeof payload === "string" && payload.trim().length > 0) {
    return looksTechnical(payload) ? fallbackMessageForStatus(status) : payload;
  }

  if (payload && typeof payload === "object") {
    const problem = payload as ProblemPayload;
    const detail = typeof problem.detail === "string" ? problem.detail.trim() : "";
    const title = typeof problem.title === "string" ? problem.title.trim() : "";
    if (detail && !looksTechnical(detail)) {
      return detail;
    }

    if (title && !looksTechnical(title)) {
      return title;
    }
  }

  return fallbackMessageForStatus(status);
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

export async function requestJson<T>(path: string, options: ApiRequestOptions = {}) {
  const response = await fetch(resolveApiUrl(path), {
    method: options.method ?? "GET",
    headers: buildHeaders(options.body, options.accessToken),
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    signal: options.signal,
    credentials: "include"
  });

  const payload = await readPayload(response);
  if (!response.ok) {
    const message = buildErrorMessage(response.status, payload);
    const detail = payload && typeof payload === "object" && "detail" in payload
      ? String(payload.detail ?? "")
      : undefined;
    const title = payload && typeof payload === "object" && "title" in payload
      ? String(payload.title ?? "")
      : undefined;

    throw new ApiError(response.status, message, detail, title);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return payload as T;
}
