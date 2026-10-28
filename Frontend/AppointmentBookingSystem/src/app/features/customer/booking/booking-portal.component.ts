import { Component, OnInit, OnDestroy, inject, signal, computed, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router, ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { ProviderService } from '../../../core/services/provider.service';
import { TenantService } from '../../../core/services/tenant.service';
import { AuthService } from '../../../core/services/auth.service';
import { LayoutService } from '../../../core/services/layout.service';
import { UserRole } from '../../../core/models/auth.models';
import { ServiceProviderDto } from '../../../core/models/provider.models';
import { TenantDto } from '../../../core/models/tenant.models';
import { AppointmentDto } from '../../../core/models/appointment.models';
import { TopbarComponent } from '../../../layout/topbar/topbar.component';
import { BookingModalComponent } from '../../appointments/booking-modal/booking-modal.component';
import { LoginComponent } from '../../auth/login/login.component';
import { getInitials } from '../../../shared/utils/formatters';
import { AppPhonePipe } from '../../../shared/pipes';
import { OrganizationsComponent } from './components/organizations/organizations.component';

@Component({
  selector: 'app-booking-portal',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
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
  private readonly providerService = inject(ProviderService);
  private readonly tenantService = inject(TenantService);
  readonly authService = inject(AuthService);
  private readonly layoutService = inject(LayoutService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  // Organizations state
  readonly tenants = signal<TenantDto[]>([]);
  readonly isLoadingTenants = signal<boolean>(false);
  readonly selectedTenant = signal<TenantDto | null>(null);

  // Search filter
  searchQuery: string = '';
  selectedServiceType: string = '';
  readonly serviceTypes = signal<string[]>([]);
  isCategoryDropdownOpen: boolean = false;

  // Filtered organizations on /booking-portal home view
  readonly filteredTenants = computed(() => {
    const q = (this.searchQuery || '').trim().toLowerCase();
    const list = this.tenants();
    if (!q) return list;
    return list.filter(t =>
      t.name.toLowerCase().includes(q) ||
      (t.address && t.address.toLowerCase().includes(q)) ||
      (t.serviceTypes && t.serviceTypes.some(s => s.toLowerCase().includes(q)))
    );
  });

  // Specialists state
  readonly providers = signal<ServiceProviderDto[]>([]);
  readonly filteredProviders = signal<ServiceProviderDto[]>([]);
  readonly isLoadingProviders = signal<boolean>(false);

  // Modals state
  readonly isBookingModalOpen = signal<boolean>(false);
  readonly selectedProviderIdForModal = signal<number | null>(null);

  // Slide-in Auth Drawer state
  readonly isLoginDrawerOpen = signal<boolean>(false);
  readonly loginDrawerMode = signal<'login' | 'register'>('login');
  private authDrawerSub?: Subscription;

  // Conditional UI: Render Login/Register form directly in directory right section when:
  // 1. No organizations exist (!isLoadingTenants && tenants.length === 0)
  // OR 2. User clicked Sign In / Sign Up while on directory page (isLoginDrawerOpen === true)
  readonly shouldShowAuthInDirectory = computed(() => {
    if (this.selectedTenant()) return false;
    const noOrgsAvailable = !this.isLoadingTenants() && this.tenants().length === 0;
    const authTriggered = this.isLoginDrawerOpen();
    return noOrgsAvailable || authTriggered;
  });

  ngOnInit(): void {
    // 1. Listen for route changes (e.g. /booking-portal or /booking-portal/:slug)
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

    // 4. Initialize Auth if needed
    if (!this.authService.isInitialized()) {
      this.authService.checkAuth().subscribe(() => {
        this.checkResumeBooking();
      });
    } else {
      this.checkResumeBooking();
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
        this.tenants.set(list);
        this.isLoadingTenants.set(false);
      },
      error: () => {
        this.isLoadingTenants.set(false);
      }
    });
  }

  // Resolve tenant dynamically by slug or ID from active tenants list
  private resolveAndLoadTenant(slug: string): void {
    const cleanSlug = slug.trim().toLowerCase();
    this.isLoadingTenants.set(true);

    const matchFromList = (list: TenantDto[]) => {
      const matched = list.find(t =>
        (t.slug && t.slug.toLowerCase() === cleanSlug) ||
        t.id.toString() === cleanSlug
      );

      if (matched) {
        this.selectedTenant.set(matched);
        this.isLoadingTenants.set(false);
        this.loadProviders(matched.id);
      } else {
        this.isLoadingTenants.set(false);
        this.backToAllOrganizations();
      }
    };

    if (this.tenants().length > 0) {
      matchFromList(this.tenants());
    } else {
      this.tenantService.getActiveTenants().subscribe({
        next: (list) => {
          this.tenants.set(list);
          matchFromList(list);
        },
        error: () => {
          this.isLoadingTenants.set(false);
          this.backToAllOrganizations();
        }
      });
    }
  }

  // Navigate to dedicated organization page: /organization/:slug
  navigateToOrganization(tenant: TenantDto): void {
    const slug = tenant.slug || tenant.id.toString();
    this.router.navigate(['/organization', slug]);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  // Navigate back to the main directory (/booking-portal)
  backToAllOrganizations(): void {
    this.selectedTenant.set(null);
    this.searchQuery = '';
    this.selectedServiceType = '';
    this.router.navigate(['/booking-portal']);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  // Load specialists for the organization
  loadProviders(tenantId: number): void {
    this.isLoadingProviders.set(true);
    this.providerService.getActiveProviders(tenantId).subscribe({
      next: (list) => {
        // Guarantee only specialists belonging to this organization are displayed
        const tenantSpecific = (list || []).filter(p => !tenantId || p.tenantId === tenantId);
        this.providers.set(tenantSpecific);
        this.filteredProviders.set(tenantSpecific);

        const types = Array.from(new Set(tenantSpecific.map(p => p.serviceType).filter(Boolean)));
        this.serviceTypes.set(types);

        this.isLoadingProviders.set(false);
      },
      error: () => {
        this.isLoadingProviders.set(false);
      }
    });
  }

  // Search input handler
  onSearchInput(): void {
    if (this.isLoginDrawerOpen()) {
      this.closeLoginDrawer();
    }
    if (this.selectedTenant()) {
      this.filterProviders();
    }
  }

  executeSearch(): void {
    if (this.isLoginDrawerOpen()) {
      this.closeLoginDrawer();
    }
    if (this.selectedTenant()) {
      this.filterProviders();
    }
  }

  clearSearch(): void {
    this.searchQuery = '';
    if (this.selectedTenant()) {
      this.filterProviders();
    }
  }

  // Filter specialists within the active organization
  filterProviders(): void {
    const query = (this.searchQuery || '').toLowerCase().trim();
    const type = this.selectedServiceType;

    let result = this.providers();
    if (type && type !== 'all') {
      result = result.filter(p => p.serviceType === type);
    }
    if (query) {
      result = result.filter(p =>
        (p.name && p.name.toLowerCase().includes(query)) ||
        (p.serviceType && p.serviceType.toLowerCase().includes(query)) ||
        (p.address && p.address.toLowerCase().includes(query)) ||
        (p.bio && p.bio.toLowerCase().includes(query))
      );
    }
    this.filteredProviders.set(result);
  }

  showAllProviders(): void {
    this.selectedServiceType = 'all';
    this.searchQuery = '';
    this.filteredProviders.set(this.providers());
  }

  // Category Dropdown Filter
  toggleCategoryDropdown(event: Event): void {
    event.stopPropagation();
    if (this.isLoginDrawerOpen()) {
      this.closeLoginDrawer();
    }
    this.isCategoryDropdownOpen = !this.isCategoryDropdownOpen;
  }

  selectCategoryDropdown(type: string): void {
    this.selectedServiceType = type;
    this.isCategoryDropdownOpen = false;
    this.filterProviders();
  }

  getCategoryLabel(): string {
    if (!this.selectedServiceType || this.selectedServiceType === 'all') {
      return 'All Specialties';
    }
    return this.selectedServiceType;
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.isCategoryDropdownOpen) {
      const target = event.target as HTMLElement;
      if (!target.closest('.custom-dropdown-wrap')) {
        this.isCategoryDropdownOpen = false;
      }
    }
  }

  // Organization Banner Photo
  getTenantBannerImage(tenant: TenantDto): string {
    return tenant.logoUrl || '';
  }

  getAvatarGradient(name: string): string {
    const gradients = [
      'linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%)',
      'linear-gradient(135deg, #0ea5e9 0%, #3b82f6 100%)',
      'linear-gradient(135deg, #10b981 0%, #059669 100%)',
      'linear-gradient(135deg, #f59e0b 0%, #d97706 100%)',
      'linear-gradient(135deg, #ec4899 0%, #8b5cf6 100%)',
      'linear-gradient(135deg, #06b6d4 0%, #6366f1 100%)'
    ];
    let hash = 0;
    for (let i = 0; i < (name || '').length; i++) {
      hash = name.charCodeAt(i) + ((hash << 5) - hash);
    }
    const index = Math.abs(hash) % gradients.length;
    return gradients[index];
  }

  getInitials(name: string): string {
    return getInitials(name);
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



  onAppointmentBooked(appointment: AppointmentDto): void {
    // Confirmed via booking modal
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
    this.checkResumeBooking();

    // If no booking draft to resume, redirect to dashboard if user has management role or no orgs are present
    if (!sessionStorage.getItem('pending_booking_draft')) {
      const user = this.authService.currentUser();
      if (user && (user.role !== UserRole.Customer || this.tenants().length === 0)) {
        this.router.navigate(['/dashboard']);
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
