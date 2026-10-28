import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, catchError, map, of, tap, shareReplay } from 'rxjs';
import { AuthResponse, LoginRequest, UserDto, UserRole } from '../models/auth.models';
import { environment } from '../../../environments/environment';

export { UserRole };
export type { UserDto, LoginRequest, AuthResponse };

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly apiUrl = `${environment.apiUrl}/auth`;

  // Pure in-memory reactive state (ZERO localStorage - all cookies are HttpOnly)
  readonly currentUser = signal<UserDto | null>(null);
  readonly isInitialized = signal<boolean>(false);
  readonly isLoading = signal<boolean>(false);

  // Concurrency-safe in-flight refresh observable to eliminate multiple simultaneous refresh requests
  private refreshInProgress = false;
  private refreshObservable$: Observable<AuthResponse | null> | null = null;

  readonly isAuthenticated = computed(() => !!this.currentUser());
  readonly isSuperAdmin = computed(() => {
    const u = this.currentUser();
    if (!u) return false;
    const r = u.role;
    const rn = u.roleName;
    return r === UserRole.SuperAdmin || (r as unknown) === 1 || (r as unknown) === '1' || (r as unknown) === 'SuperAdmin' || rn === 'SuperAdmin';
  });
  readonly isAdmin = computed(() => {
    const u = this.currentUser();
    if (!u) return false;
    const r = u.role;
    const rn = u.roleName;
    return r === UserRole.Admin || (r as unknown) === 2 || (r as unknown) === '2' || (r as unknown) === 'Admin' || rn === 'Admin' ||
      r === UserRole.SuperAdmin || (r as unknown) === 1 || (r as unknown) === '1' || (r as unknown) === 'SuperAdmin' || rn === 'SuperAdmin';
  });
  readonly isServiceProvider = computed(() => {
    const u = this.currentUser();
    if (!u) return false;
    const r = u.role;
    const rn = u.roleName;
    return r === UserRole.ServiceProvider || (r as unknown) === 3 || (r as unknown) === '3' || (r as unknown) === 'ServiceProvider' || rn === 'ServiceProvider';
  });
  readonly isCustomer = computed(() => {
    const u = this.currentUser();
    if (!u) return false;
    const r = u.role;
    const rn = u.roleName;
    return (r === UserRole.Customer || (r as unknown) === 4 || (r as unknown) === '4' || (r as unknown) === 'Customer' || rn === 'Customer') && !this.isAdmin();
  });

  login(credentials: LoginRequest): Observable<AuthResponse> {
    this.isLoading.set(true);

    return this.http.post<AuthResponse>(`${this.apiUrl}/login`, credentials).pipe(
      tap({
        next: (response) => {
          this.isLoading.set(false);
          this.currentUser.set(response.user);
          this.isInitialized.set(true);
        },
        error: () => {
          this.isLoading.set(false);
        }
      })
    );
  }

  register(data: import('../models/auth.models').RegisterCustomerRequest): Observable<AuthResponse> {
    this.isLoading.set(true);

    return this.http.post<AuthResponse>(`${this.apiUrl}/register`, data).pipe(
      tap({
        next: (response) => {
          this.isLoading.set(false);
          this.currentUser.set(response.user);
          this.isInitialized.set(true);
        },
        error: () => {
          this.isLoading.set(false);
        }
      })
    );
  }

  logout(): void {
    this.http.post(`${this.apiUrl}/logout`, {}).pipe(
      catchError(() => of(null))
    ).subscribe(() => {
      this.currentUser.set(null);
      this.isInitialized.set(true);
      this.router.navigate(['/book']);
    });
  }

  /**
   * Performs silent token rotation via HttpOnly refresh-token cookie.
   * Uses shareReplay(1) to deduplicate concurrent 401 refresh attempts.
   */
  refreshToken(): Observable<AuthResponse | null> {
    if (this.refreshInProgress && this.refreshObservable$) {
      return this.refreshObservable$;
    }

    this.refreshInProgress = true;
    this.refreshObservable$ = this.http.post<AuthResponse>(`${this.apiUrl}/refresh-token`, {}).pipe(
      tap({
        next: (response) => {
          this.refreshInProgress = false;
          this.refreshObservable$ = null;
          if (response && response.user) {
            this.currentUser.set(response.user);
            this.isInitialized.set(true);
          }
        },
        error: () => {
          this.refreshInProgress = false;
          this.refreshObservable$ = null;
          this.currentUser.set(null);
          this.isInitialized.set(true);
        }
      }),
      shareReplay(1)
    );

    return this.refreshObservable$;
  }

  /**
   * Initializes user authentication context.
   * If access token is expired, attempts silent refresh via refresh-token cookie.
   */
  checkAuth(): Observable<UserDto | null> {
    return this.http.get<UserDto>(`${this.apiUrl}/me`).pipe(
      map((user) => {
        this.currentUser.set(user);
        this.isInitialized.set(true);
        return user;
      }),
      catchError(() => {
        // If /me fails with 401, attempt silent refresh using the refresh-token cookie
        return this.http.post<AuthResponse>(`${this.apiUrl}/refresh-token`, {}).pipe(
          map((response) => {
            const user = response?.user ?? null;
            this.currentUser.set(user);
            this.isInitialized.set(true);
            return user;
          }),
          catchError(() => {
            // No valid refresh token either: purely anonymous guest
            this.currentUser.set(null);
            this.isInitialized.set(true);
            return of(null);
          })
        );
      })
    );
  }
}
