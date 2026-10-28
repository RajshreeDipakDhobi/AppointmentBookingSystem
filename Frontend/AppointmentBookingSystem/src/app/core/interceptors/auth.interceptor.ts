import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpInterceptorFn, HttpRequest, HttpResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, map, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * Automatically unwraps standard backend envelope: { success: true, message: "...", data: ... }
 */
function unwrapEnvelope(event: HttpEvent<unknown>): HttpEvent<unknown> {
  if (event instanceof HttpResponse) {
    const body = event.body as any;
    if (body && typeof body === 'object' && 'success' in body && 'data' in body) {
      if (body.success === true) {
        if (body.data !== undefined && body.data !== null) {
          if (typeof body.data === 'object' && !Array.isArray(body.data) && !('message' in body.data) && body.message) {
            body.data.message = body.message;
          }
          return event.clone({ body: body.data });
        }
      }
    }
  }
  return event;
}

export const authInterceptor: HttpInterceptorFn = (req: HttpRequest<unknown>, next: HttpHandlerFn) => {
  const router = inject(Router);
  const authService = inject(AuthService);

  // Send HttpOnly cookies with every API request
  const authReq = req.clone({
    withCredentials: true
  });

  return next(authReq).pipe(
    map(unwrapEnvelope),
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401) {
        // Exclude authentication requests themselves from triggering silent refresh loops
        const isAuthEndpoint = req.url.includes('/auth/login') ||
                               req.url.includes('/auth/register') ||
                               req.url.includes('/auth/refresh-token');

        // If the refresh-token endpoint itself returns 401, session is definitively expired
        if (req.url.includes('/auth/refresh-token')) {
          authService.currentUser.set(null);
          router.navigate(['/login']);
          return throwError(() => error);
        }

        // Pass through login, registration, or initial profile checks without redirect
        if (isAuthEndpoint || req.url.includes('/auth/me')) {
          return throwError(() => error);
        }

        // For all protected business APIs, perform silent token refresh and retry original request
        return authService.refreshToken().pipe(
          switchMap((authResponse) => {
            if (authResponse) {
              // Retry the original request with refreshed HttpOnly credentials
              return next(authReq).pipe(map(unwrapEnvelope));
            }
            authService.currentUser.set(null);
            router.navigate(['/login']);
            return throwError(() => error);
          }),
          catchError((refreshErr) => {
            authService.currentUser.set(null);
            router.navigate(['/login']);
            return throwError(() => refreshErr);
          })
        );
      }

      return throwError(() => error);
    })
  );
};

