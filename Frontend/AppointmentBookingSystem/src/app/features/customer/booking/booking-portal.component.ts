import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router, ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { TenantService } from '../../../core/services/tenant.service';
import { AuthService } from '../../../core/services/auth.service';
import { LayoutService } from '../../../core/services/layout.service';
import { UserRole } from '../../../core/models/auth.models';
import { TenantDto } from '../../../core/models/tenant.models';
import { AppointmentDto } from '../../../core/models/appointment.models';
import { TopbarComponent } from '../../../layout/topbar/topbar.component';
import { BookingModalComponent } from '../../appointments/booking-modal/booking-modal.component';
import { LoginComponent } from '../../auth/login/login.component';
import { OrganizationsComponent } from './components/organizations/organizations.component';

@Component({
  selector: 'app-booking-portal',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    TopbarComponent,
    BookingModalComponent,
    LoginComponent,
    OrganizationsComponent
  ],
  templateUrl: './booking-portal.component.html',
  styleUrls: ['./booking-portal.component.css']
})
export class BookingPortalComponent implements OnInit, OnDestroy {
  private readonly tenantService = inject(TenantService);
  readonly authService = inject(AuthService);
  private readonly layoutService = inject(LayoutService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  // Organizations state
  readonly tenants = signal<TenantDto[]>([]);
  readonly isLoadingTenants = signal<boolean>(false);
  readonly selectedTenant = signal<TenantDto | null>(null);

  // Search filter query passed to organizations component
  searchQuery: string = '';

  // Modals state
  readonly isBookingModalOpen = signal<boolean>(false);
  readonly selectedProviderIdForModal = signal<number | null>(null);

  // Slide-in Auth Drawer state
  readonly isLoginDrawerOpen = signal<boolean>(false);
  readonly loginDrawerMode = signal<'login' | 'register'>('login');
  private authDrawerSub?: Subscription;

  // Render Login/Register form in right section when:
  // 1. No organizations exist (!isLoadingTenants && tenants.length === 0)
  // OR 2. User clicked Sign In / Sign Up while on directory page (isLoginDrawerOpen === true)
  readonly shouldShowAuthInDirectory = computed(() => {
    if (this.selectedTenant()) return false;
    const noOrgsAvailable = !this.isLoadingTenants() && this.tenants().length === 0;
    const authTriggered = this.isLoginDrawerOpen();
    return noOrgsAvailable || authTriggered;
  });

  ngOnInit(): void {
    // 1. Listen for route changes
    this.route.paramMap.subscribe(params => {
      const slug = params.get('slug');
      if (slug) {
        this.router.navigate(['/organization', slug]);
      } else {
        this.selectedTenant.set(null);
        this.loadTenants();
      }
    });

    // 2. Listen for header drawer trigger
    this.authDrawerSub = this.layoutService.openAuthDrawer$.subscribe((mode) => {
      if (this.isLoginDrawerOpen() && this.loginDrawerMode() === mode) {
        this.closeLoginDrawer();
      } else {
        this.loginDrawerMode.set(mode);
        this.isLoginDrawerOpen.set(true);
        window.scrollTo({ top: 0, behavior: 'smooth' });
      }
    });

    // 3. Listen for ?auth=login or ?auth=register query parameters
    this.route.queryParams.subscribe(params => {
      if (params['auth'] === 'login' || params['auth'] === 'register') {
        this.loginDrawerMode.set(params['auth']);
        this.isLoginDrawerOpen.set(true);
        window.scrollTo({ top: 0, behavior: 'smooth' });
      }
    });

    // 4. Initialize Auth and redirect authenticated users away from public portal
    const handleAuthRedirect = (user: any) => {
      if (!user) {
        this.checkResumeBooking();
        return;
      }
      const role = user.role;
      if (role === UserRole.Customer || (role as unknown) === 4 || (role as unknown) === '4') {
        this.router.navigate(['/my-appointments']);
        return;
      }
      if (role === UserRole.ServiceProvider || (role as unknown) === 3 || (role as unknown) === '3') {
        this.router.navigate(['/appointments']);
        return;
      }
      this.router.navigate(['/dashboard']);
    };

    if (!this.authService.isInitialized()) {
      this.authService.checkAuth().subscribe((user) => {
        handleAuthRedirect(user);
      });
    } else {
      handleAuthRedirect(this.authService.currentUser());
    }
  }

  ngOnDestroy(): void {
    this.authDrawerSub?.unsubscribe();
  }

  // Load all active organizations
  loadTenants(): void {
    this.isLoadingTenants.set(true);
    this.tenantService.getActiveTenants().subscribe({
      next: (list) => {
        this.tenants.set(list || []);
        this.isLoadingTenants.set(false);
      },
      error: () => {
        this.isLoadingTenants.set(false);
      }
    });
  }

  // Navigate to dedicated organization page: /organization/:slug
  navigateToOrganization(tenant: TenantDto): void {
    const slug = tenant.slug || tenant.id.toString();
    this.router.navigate(['/organization', slug]);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  // Modal Handlers
  openBookingModal(providerId?: number): void {
    this.closeLoginDrawer();
    this.selectedProviderIdForModal.set(providerId ?? null);
    this.isBookingModalOpen.set(true);
  }

  closeBookingModal(): void {
    this.selectedProviderIdForModal.set(null);
    this.isBookingModalOpen.set(false);
  }

  onAppointmentBooked(_appointment: AppointmentDto): void {
    this.closeBookingModal();
    sessionStorage.removeItem('pending_booking_draft');
    this.router.navigate(['/my-appointments']);
  }

  // Drawer Auth Handlers
  closeLoginDrawer(): void {
    this.isLoginDrawerOpen.set(false);
  }

  onAuthModeChange(mode: 'login' | 'register'): void {
    this.loginDrawerMode.set(mode);
  }

  onLoginSuccess(): void {
    this.isLoginDrawerOpen.set(false);

    const user = this.authService.currentUser();
    const isCustomer = user && (user.role === UserRole.Customer || (user.role as unknown) === 4 || (user.role as unknown) === '4');

    if (isCustomer) {
      sessionStorage.removeItem('pending_booking_draft');
      this.router.navigate(['/my-appointments']);
      return;
    }

    this.checkResumeBooking();

    // If no booking draft to resume, redirect to role dashboard/appointments
    if (!sessionStorage.getItem('pending_booking_draft')) {
      if (user) {
        if (user.role === UserRole.ServiceProvider || (user.role as unknown) === 3 || (user.role as unknown) === '3') {
          this.router.navigate(['/appointments']);
        } else {
          this.router.navigate(['/dashboard']);
        }
      }
    }
  }

  private checkResumeBooking(): void {
    if (!this.authService.isAuthenticated()) {
      sessionStorage.removeItem('pending_booking_draft');
      if (this.route.snapshot.queryParamMap.has('resumeBooking')) {
        this.router.navigate([], {
          relativeTo: this.route,
          queryParams: { resumeBooking: null },
          queryParamsHandling: 'merge',
          replaceUrl: true
        });
      }
      return;
    }

    const hasPendingDraft = sessionStorage.getItem('pending_booking_draft');
    const isResume = this.route.snapshot.queryParamMap.get('resumeBooking') === 'true';

    if (hasPendingDraft && isResume) {
      try {
        const draft = JSON.parse(hasPendingDraft);
        if (draft && draft.providerId) {
          this.openBookingModal(draft.providerId);
        }
      } catch {
        sessionStorage.removeItem('pending_booking_draft');
      }
    }
  }
}
