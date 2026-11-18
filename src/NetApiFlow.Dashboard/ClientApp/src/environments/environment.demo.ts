/**
 * Static demo build: there is no .NET host behind it. Requests are answered from recorded
 * fixtures and the SSE stream is never opened, so the pages show the captured state and do not
 * update on their own.
 */
export const environment = {
  isDemoMode: true,
};
