import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Observable, catchError, map, of, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CartDto, CartItemDto, AddToCartRequestDto, MergeCartRequestDto } from '../models/cart.models';

const GUEST_CART_TOKEN_KEY = 'guest_cart_token';

@Injectable({
  providedIn: 'root'
})
export class CartService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/cart`;

  // Reactive state using Angular Signals
  readonly cart = signal<CartDto | null>(null);
  readonly isLoading = signal<boolean>(false);

  // Computed signals
  readonly cartItems = computed<CartItemDto[]>(() => this.cart()?.items ?? []);
  readonly cartCount = computed<number>(() => {
    const c = this.cart();
    if (c?.totalItemsCount !== undefined && c.totalItemsCount !== null) return c.totalItemsCount;
    if (c?.itemCount !== undefined && c.itemCount !== null) return c.itemCount;
    return this.cartItems().length;
  });
  readonly cartTotalAmount = computed<number>(() => {
    const c = this.cart();
    if (c?.totalPrice !== undefined && c.totalPrice !== null) return c.totalPrice;
    if (c?.totalAmount !== undefined && c.totalAmount !== null) return c.totalAmount;
    return this.cartItems().reduce((sum, item) => sum + (item.price || 0), 0);
  });
  readonly cartTotalDuration = computed<number>(() => {
    const c = this.cart();
    if (c?.totalDurationMinutes !== undefined && c.totalDurationMinutes !== null) return c.totalDurationMinutes;
    return this.cartItems().reduce((sum, item) => sum + (item.durationMinutes || 0), 0);
  });
  readonly cartServiceIds = computed<number[]>(() => this.cartItems().map(item => item.serviceId));

  constructor() {
    this.ensureGuestToken();
    // Auto-load user or guest cart on service initialization
    this.loadCart().subscribe({ error: () => {} });
  }

  /**
   * Retrieves or initializes a persistent guest token for unauthenticated visitors
   */
  getGuestToken(): string {
    return this.ensureGuestToken();
  }

  private ensureGuestToken(): string {
    let token = localStorage.getItem(GUEST_CART_TOKEN_KEY);
    if (!token) {
      if (typeof crypto !== 'undefined' && crypto.randomUUID) {
        token = crypto.randomUUID().replace(/-/g, '');
      } else {
        token = 'guest_' + Math.random().toString(36).substring(2) + Date.now().toString(36);
      }
      localStorage.setItem(GUEST_CART_TOKEN_KEY, token);
    }
    return token;
  }

  private getRequestHeaders(): HttpHeaders {
    const token = this.getGuestToken();
    return new HttpHeaders({
      'X-Guest-Cart-Token': token
    });
  }

  /**
   * Check if a specific service is in the cart
   */
  isInCart(serviceId: number): boolean {
    return this.cartItems().some(item => item.serviceId === serviceId);
  }

  /**
   * Load current cart from backend
   */
  loadCart(tenantId?: number): Observable<CartDto> {
    this.isLoading.set(true);
    let params = new HttpParams().set('guestToken', this.getGuestToken());
    if (tenantId) {
      params = params.set('tenantId', tenantId.toString());
    }

    return this.http.get<CartDto>(this.apiUrl, {
      headers: this.getRequestHeaders(),
      params
    }).pipe(
      tap({
        next: (data) => {
          this.cart.set(data);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
        }
      }),
      catchError(() => {
        const fallback: CartDto = {
          items: [],
          totalAmount: 0,
          totalDurationMinutes: 0,
          itemCount: 0
        };
        this.cart.set(fallback);
        this.isLoading.set(false);
        return of(fallback);
      })
    );
  }

  /**
   * Add a service to the backend cart (strictly 1 item per service)
   */
  addToCart(serviceId: number, tenantId?: number): Observable<CartDto> {
    this.isLoading.set(true);
    const payload: AddToCartRequestDto = {
      serviceId,
      tenantId,
      guestToken: this.getGuestToken()
    };

    return this.http.post<CartDto>(`${this.apiUrl}/items`, payload, {
      headers: this.getRequestHeaders()
    }).pipe(
      tap({
        next: (updatedCart) => {
          this.cart.set(updatedCart);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Remove a service from the backend cart
   */
  removeFromCart(serviceId: number, tenantId?: number): Observable<CartDto> {
    this.isLoading.set(true);
    let params = new HttpParams().set('guestToken', this.getGuestToken());
    if (tenantId) {
      params = params.set('tenantId', tenantId.toString());
    }

    return this.http.delete<CartDto>(`${this.apiUrl}/items/${serviceId}`, {
      headers: this.getRequestHeaders(),
      params
    }).pipe(
      tap({
        next: (updatedCart) => {
          this.cart.set(updatedCart);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Clear all items in cart
   */
  clearCart(tenantId?: number): Observable<boolean> {
    this.isLoading.set(true);
    let params = new HttpParams().set('guestToken', this.getGuestToken());
    if (tenantId) {
      params = params.set('tenantId', tenantId.toString());
    }

    return this.http.delete<boolean>(this.apiUrl, {
      headers: this.getRequestHeaders(),
      params
    }).pipe(
      tap({
        next: () => {
          this.cart.set({
            items: [],
            totalAmount: 0,
            totalDurationMinutes: 0,
            itemCount: 0
          });
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
        }
      }),
      catchError(() => {
        this.isLoading.set(false);
        return of(false);
      })
    );
  }

  /**
   * Merge anonymous guest cart with customer account upon authentication
   */
  mergeCartOnLogin(tenantId?: number): Observable<CartDto | null> {
    const guestToken = localStorage.getItem(GUEST_CART_TOKEN_KEY);
    if (!guestToken) {
      return this.loadCart(tenantId);
    }

    const payload: MergeCartRequestDto = {
      guestToken,
      tenantId
    };

    return this.http.post<CartDto>(`${this.apiUrl}/merge`, payload, {
      headers: this.getRequestHeaders()
    }).pipe(
      tap({
        next: (mergedCart) => {
          this.cart.set(mergedCart);
          // Rotate guest token for future guest sessions
          localStorage.removeItem(GUEST_CART_TOKEN_KEY);
          this.ensureGuestToken();
        }
      }),
      catchError(() => {
        return this.loadCart(tenantId);
      })
    );
  }
}

