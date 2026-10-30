import { Routes } from '@angular/router';
import { 
  authGuard, 
  guestGuard, 
  adminGuard, 
  tenantAdminGuard, 
  superAdminGuard,
  dashboardGuard
} from './core/guards/auth.guard';

export const routes: Routes = [
  // --- Public Routes ---
  {
    path: '',
    redirectTo: 'booking-portal',
    pathMatch: 'full'
  },
  {
    path: 'booking-portal',
    loadComponent: () => import('./features/customer/booking/booking-portal.component').then(m => m.BookingPortalComponent)
  },
  {
    path: 'organization/:slug',
    loadComponent: () => import('./features/customer/organization-detail/organization-detail.component').then(m => m.OrganizationDetailComponent)
  },
  {
    path: 'booking-portal/:slug',
    loadComponent: () => import('./features/customer/organization-detail/organization-detail.component').then(m => m.OrganizationDetailComponent)
  },
  {
    path: 'book',
    redirectTo: 'booking-portal',
    pathMatch: 'full'
  },
  {
    path: 'book/:slug',
    redirectTo: 'organization/:slug'
  },

  // --- Unified Auth Route ---
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login/login.component').then(m => m.LoginComponent),
    canActivate: [guestGuard]
  },

  // --- Unified Authenticated Layout Routes (Admin, Staff & Customer) ---
  {
    path: '',
    loadComponent: () => import('./layout/main-layout.component').then(m => m.MainLayoutComponent),
    canActivate: [authGuard],
    children: [
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent),
        canActivate: [dashboardGuard]
      },
      {
        path: 'tenants',
        loadComponent: () => import('./features/tenants/tenants.component').then(m => m.TenantsComponent),
        canActivate: [superAdminGuard]
      },
      {
        path: 'services',
        loadComponent: () => import('./features/services/services.component').then(m => m.ServicesComponent),
        canActivate: [tenantAdminGuard]
      },
      {
        path: 'providers',
        loadComponent: () => import('./features/providers/providers.component').then(m => m.ProvidersComponent),
        canActivate: [tenantAdminGuard]
      },
      {
        path: 'availability',
        loadComponent: () => import('./features/availability/availability.component').then(m => m.AvailabilityComponent),
        canActivate: [tenantAdminGuard]
      },
      {
        path: 'appointments',
        loadComponent: () => import('./features/appointments/appointments.component').then(m => m.AppointmentsComponent),
        canActivate: [adminGuard]
      },
      {
        path: 'my-appointments',
        loadComponent: () => import('./features/customer/my-appointments/customer-appointments.component').then(m => m.CustomerAppointmentsComponent)
      }
    ]
  },

  // --- Aliases for standard clean navigation ---
  {
    path: 'admin',
    redirectTo: 'dashboard',
    pathMatch: 'full'
  },
  {
    path: 'admin/dashboard',
    redirectTo: 'dashboard',
    pathMatch: 'full'
  },
  {
    path: 'admin/tenants',
    redirectTo: 'tenants',
    pathMatch: 'full'
  },
  {
    path: 'admin/services',
    redirectTo: 'services',
    pathMatch: 'full'
  },
  {
    path: 'admin/providers',
    redirectTo: 'providers',
    pathMatch: 'full'
  },
  {
    path: 'admin/availability',
    redirectTo: 'availability',
    pathMatch: 'full'
  },
  {
    path: 'admin/appointments',
    redirectTo: 'appointments',
    pathMatch: 'full'
  },

  // --- Fallback ---
  {
    path: '**',
    redirectTo: 'book'
  }
];
