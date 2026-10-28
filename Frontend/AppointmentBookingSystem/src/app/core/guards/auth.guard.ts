import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { UserRole } from '../models/auth.models';
import { map } from 'rxjs';

/**
 * Ensures user is authenticated. Redirects to /login if not authenticated.
 */
export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isInitialized()) {
    return authService.isAuthenticated() ? true : router.createUrlTree(['/login']);
  }

  return authService.checkAuth().pipe(
    map((user) => (user ? true : router.createUrlTree(['/login'])))
  );
};

/**
 * Ensures user is a guest (not authenticated). Redirects logged-in users to their role-appropriate page.
 */
export const guestGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  const getRedirect = (userRole?: UserRole) => {
    if (userRole === UserRole.ServiceProvider || (userRole as unknown) === 3 || (userRole as unknown) === '3') {
      return router.createUrlTree(['/appointments']);
    }
    if (userRole === UserRole.Customer || (userRole as unknown) === 4 || (userRole as unknown) === '4') {
      return router.createUrlTree(['/my-appointments']);
    }
    return router.createUrlTree(['/dashboard']);
  };

  if (authService.isInitialized()) {
    return !authService.isAuthenticated() ? true : getRedirect(authService.currentUser()?.role);
  }

  return authService.checkAuth().pipe(
    map((user) => (!user ? true : getRedirect(user?.role)))
  );
};

/**
 * Dashboard guard: Allows SuperAdmin (1), Tenant Admin (2), and Customer (4).
 * Blocks ServiceProvider (3) from Dashboard and redirects to /appointments.
 */
export const dashboardGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  const checkDashboardAccess = (userRole?: UserRole) => {
    if (!userRole) return router.createUrlTree(['/login']);
    if (userRole === UserRole.ServiceProvider || (userRole as unknown) === 3 || (userRole as unknown) === '3') {
      return router.createUrlTree(['/appointments']);
    }
    return true;
  };

  if (authService.isInitialized()) {
    return checkDashboardAccess(authService.currentUser()?.role);
  }

  return authService.checkAuth().pipe(
    map((user) => checkDashboardAccess(user?.role))
  );
};

/**
 * General staff guard: Allows SuperAdmin (1), Tenant Admin (2), and ServiceProvider (3).
 * Restricts Customers (4) to /dashboard or /my-appointments.
 */
export const adminGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  const checkStaffAccess = (userRole?: UserRole) => {
    if (!userRole) return router.createUrlTree(['/login']);
    if (userRole === UserRole.Customer) return router.createUrlTree(['/dashboard']);
    return true;
  };

  if (authService.isInitialized()) {
    return checkStaffAccess(authService.currentUser()?.role);
  }

  return authService.checkAuth().pipe(
    map((user) => checkStaffAccess(user?.role))
  );
};

/**
 * Strict Admin guard: Allows only SuperAdmin (1) and Tenant Admin (2).
 * Service Providers (3) and Customers (4) are blocked (e.g. for Availability management & Providers setup).
 */
export const tenantAdminGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  const checkAdminAccess = (userRole?: UserRole) => {
    if (!userRole) return router.createUrlTree(['/login']);
    if (userRole !== UserRole.SuperAdmin && userRole !== UserRole.Admin) {
      return router.createUrlTree(userRole === UserRole.ServiceProvider ? ['/appointments'] : ['/dashboard']);
    }
    return true;
  };

  if (authService.isInitialized()) {
    return checkAdminAccess(authService.currentUser()?.role);
  }

  return authService.checkAuth().pipe(
    map((user) => checkAdminAccess(user?.role))
  );
};

/**
 * Platform guard: Allows only SuperAdmin (1).
 */
export const superAdminGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  const checkSuperAdminAccess = (userRole?: UserRole) => {
    if (!userRole) return router.createUrlTree(['/login']);
    if (userRole !== UserRole.SuperAdmin) return router.createUrlTree(['/dashboard']);
    return true;
  };

  if (authService.isInitialized()) {
    return checkSuperAdminAccess(authService.currentUser()?.role);
  }

  return authService.checkAuth().pipe(
    map((user) => checkSuperAdminAccess(user?.role))
  );
};
