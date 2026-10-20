import { HttpErrorResponse } from '@angular/common/http';
import { environment } from '../../environments/environment';

/** Normalized error used throughout the UI. */
export interface ApiError {
  /** HTTP status; 0 when the server could not be reached. */
  status: number;
  /** Stable code from the API's problem details (for example "inbox_expired"). */
  code: string;
  message: string;
  retryAfterSeconds: number | null;
}

interface ProblemBody {
  title?: string;
  detail?: string;
  code?: string;
  retryAfterSeconds?: number;
}

export function toApiError(error: unknown): ApiError {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return {
        status: 0,
        code: 'network_error',
        message: `Can't reach the server at ${environment.apiBaseUrl}. Check that the API is running.`,
        retryAfterSeconds: null,
      };
    }

    const body: ProblemBody = typeof error.error === 'object' && error.error !== null ? (error.error as ProblemBody) : {};
    return {
      status: error.status,
      code: body.code ?? `http_${error.status}`,
      message: body.detail ?? body.title ?? `The server returned HTTP ${error.status}.`,
      retryAfterSeconds: typeof body.retryAfterSeconds === 'number' ? body.retryAfterSeconds : null,
    };
  }

  return { status: -1, code: 'unknown', message: 'Something went wrong. Try again.', retryAfterSeconds: null };
}
