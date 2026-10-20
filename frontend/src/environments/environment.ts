/**
 * Production settings. By default the API is expected on the same origin under /api
 * (for example behind a reverse proxy). Set an absolute URL if the API lives elsewhere,
 * and add that site's origin to TempMail:AllowedOrigins on the backend.
 */
export const environment = {
  production: true,
  apiBaseUrl: '/api',
  /** Used until the server reports its own poll interval. */
  defaultPollIntervalSeconds: 8,
};
