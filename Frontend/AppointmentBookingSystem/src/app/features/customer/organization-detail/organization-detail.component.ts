import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TenantService } from '../../../core/services/tenant.service';
import { ProviderService } from '../../../core/services/provider.service';
import { ServiceCatalogService } from '../../../core/services/service-catalog.service';
import { AuthService } from '../../../core/services/auth.service';
import { CartService } from '../../../core/services/cart.service';
import { TenantDto } from '../../../core/models/tenant.models';
import { ServiceProviderDto } from '../../../core/models/provider.models';
import { ServiceDto } from '../../../core/models/service.models';
import { AppointmentDto } from '../../../core/models/appointment.models';
import { BookingModalComponent } from '../../appointments/booking-modal/booking-modal.component';
import { CartModalComponent, CartModalItem } from '../../../shared/components/cart-modal/cart-modal.component';
import { TopbarComponent } from '../../../layout/topbar/topbar.component';
import { AppPhonePipe } from '../../../shared/pipes';
import { getInitials } from '../../../shared/utils/formatters';

export interface CartServiceItem extends ServiceDto {
  quantity: number;
}

@Component({
  selector: 'app-organization-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    FormsModule,
    TopbarComponent,
    BookingModalComponent,
    CartModalComponent,
    AppPhonePipe
  ],
  templateUrl: './organization-detail.component.html',
  styleUrls: ['./organization-detail.component.css']
})
export class OrganizationDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly tenantService = inject(TenantService);
  private readonly providerService = inject(ProviderService);
  private readonly serviceCatalogService = inject(ServiceCatalogService);
  private readonly authService = inject(AuthService);
  private readonly cartService = inject(CartService);

  readonly tenant = signal<TenantDto | null>(null);
  readonly services = signal<ServiceDto[]>([]);
  readonly providers = signal<ServiceProviderDto[]>([]);
  readonly isLoadingTenant = signal<boolean>(true);
  readonly isLoadingServices = signal<boolean>(false);
  readonly isLoadingProviders = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Filters
  readonly searchQuery = signal<string>('');

  // Modals
  readonly isBookingModalOpen = signal<boolean>(false);
  readonly selectedProviderId = signal<number | null>(null);
  readonly selectedServiceId = signal<number | null>(null);
  readonly bookingInitialStep = signal<1 | 2 | 3 | 4 | 5>(1);
  readonly toastMessage = signal<string | null>(null);

  // Multi-Service Cart State (Urban Company Style) - Backend Persistent
  readonly cartServices = computed<CartServiceItem[]>(() => {
    const allServices = this.services();
    return this.cartService.cartItems().map(item => {
      const match = allServices.find(s => s.id === item.serviceId);
      if (match) {
        return {
          ...match,
          quantity: 1
        };
      }
      return {
        id: item.serviceId,
        name: item.serviceName,
        description: item.serviceDescription || '',
        price: item.price,
        durationMinutes: item.durationMinutes,
        quantity: 1,
        isActive: true,
        providersCount: 1,
        userId: 0,
        createdAt: ''
      };
    });
  });
  readonly isCartModalOpen = signal<boolean>(false);
  readonly cartTotalAmount = computed<number>(() => this.cartService.cartTotalAmount());
  readonly cartTotalDuration = computed<number>(() => this.cartService.cartTotalDuration());
  readonly cartTotalItemsCount = computed<number>(() => this.cartService.cartCount());
  readonly cartCount = computed<number>(() => this.cartService.cartCount());
  readonly selectedCartServiceIds = computed<number[]>(() => this.cartService.cartServiceIds());

  readonly filteredServices = computed(() => {
    const q = this.searchQuery().trim().toLowerCase();
    let list = this.services();

    if (q) {
      list = list.filter(s =>
        (s.name && s.name.toLowerCase().includes(q)) ||
        (s.description && s.description.toLowerCase().includes(q))
      );
    }

    return list;
  });

  getProviderCountForService(serviceName: string): number {
    const sName = serviceName.trim().toLowerCase();
    const serviceMatch = this.services().find(s => s.name.toLowerCase() === sName);
    const count = this.providers().filter(p =>
      (p.serviceType && p.serviceType.toLowerCase() === sName) ||
      (serviceMatch && p.serviceId === serviceMatch.id)
    ).length;
    return count > 0 ? count : (serviceMatch?.providersCount ?? 1);
  }

  ngOnInit(): void {
    this.route.paramMap.subscribe(params => {
      const slug = params.get('slug');
      if (slug) {
        this.loadTenantBySlugOrId(slug);
      } else {
        this.backToDirectory();
      }
    });

    if (!this.authService.isInitialized()) {
      this.authService.checkAuth().subscribe(() => {
        this.checkResumeBooking();
        this.cartService.loadCart(this.tenant()?.id).subscribe({ error: () => {} });
      });
    }
  }

  loadTenantBySlugOrId(slugOrId: string): void {
    const clean = slugOrId.trim().toLowerCase();
    this.isLoadingTenant.set(true);
    this.errorMessage.set(null);

    this.tenantService.getActiveTenants().subscribe({
      next: (tenants) => {
        const found = (tenants || []).find(t =>
          (t.slug && t.slug.toLowerCase() === clean) ||
          (t.tenantCode && t.tenantCode.toLowerCase() === clean) ||
          t.id.toString() === clean
        );

        if (found) {
          this.tenant.set(found);
          this.isLoadingTenant.set(false);
          this.loadServices(found.id);
          this.loadProviders(found.id);
          this.cartService.loadCart(found.id).subscribe({ error: () => {} });
        } else {
          this.isLoadingTenant.set(false);
          this.errorMessage.set('Organization not found or inactive.');
        }
      },
      error: () => {
        this.isLoadingTenant.set(false);
        this.errorMessage.set('Failed to load organization details. Please check your connection.');
      }
    });
  }

  loadServices(tenantId: number): void {
    this.isLoadingServices.set(true);
    // Strictly load only services that have assigned specialists for booking
    this.serviceCatalogService.getActiveServices(tenantId, true).subscribe({
      next: (list) => {
        const bookable = (list || []).filter(s =>
          (s.providersCount ?? 0) > 0 ||
          (s.assignedProviders && s.assignedProviders.length > 0) ||
          (s.assignedProviderIds && s.assignedProviderIds.length > 0)
        );
        this.services.set(bookable);
        this.isLoadingServices.set(false);
        this.checkResumeBooking();
      },
      error: () => {
        this.isLoadingServices.set(false);
      }
    });
  }

  loadProviders(tenantId: number): void {
    this.isLoadingProviders.set(true);
    this.providerService.getActiveProviders(tenantId).subscribe({
      next: (list) => {
        const tenantSpecific = (list || []).filter(p => !tenantId || p.tenantId === tenantId);
        this.providers.set(tenantSpecific);
        this.isLoadingProviders.set(false);
      },
      error: () => {
        this.isLoadingProviders.set(false);
      }
    });
  }

  private checkResumeBooking(): void {
    const isResume = this.route.snapshot.queryParamMap.get('resumeBooking') === 'true';
    const draftRaw = sessionStorage.getItem('pending_booking_draft');

    if (draftRaw && (isResume || this.authService.isAuthenticated())) {
      try {
        const draft = JSON.parse(draftRaw);
        if (draft) {
          if (draft.serviceIds && Array.isArray(draft.serviceIds) && draft.serviceIds.length > 0) {
            this.selectedServiceId.set(Number(draft.serviceIds[0]));
          } else if (draft.serviceId) {
            this.selectedServiceId.set(Number(draft.serviceId));
          } else {
            this.selectedServiceId.set(null);
          }
          this.selectedProviderId.set(draft.providerId ? Number(draft.providerId) : null);
          this.isBookingModalOpen.set(true);

          if (this.route.snapshot.queryParamMap.has('resumeBooking')) {
            this.router.navigate([], {
              relativeTo: this.route,
              queryParams: { resumeBooking: null },
              queryParamsHandling: 'merge',
              replaceUrl: true
            });
          }
        }
      } catch {
        sessionStorage.removeItem('pending_booking_draft');
      }
    }
  }

  onSearchInput(value: string): void {
    this.searchQuery.set(value || '');
  }

  clearSearch(): void {
    this.searchQuery.set('');
  }

  backToDirectory(): void {
    this.router.navigate(['/booking-portal']);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  isServiceInCart(serviceId: number): boolean {
    return this.cartService.isInCart(serviceId);
  }

  getServiceQuantity(serviceId: number): number {
    return this.isServiceInCart(serviceId) ? 1 : 0;
  }

  toggleCartService(service: ServiceDto, event?: Event): void {
    if (event) event.stopPropagation();
    if (this.isServiceInCart(service.id)) {
      this.removeFromCart(service.id, event);
    } else {
      this.cartService.addToCart(service.id, this.tenant()?.id).subscribe({
        next: () => {
          this.showToast(`Added "${service.name}" to booking cart.`);
        },
        error: (err) => {
          this.showToast(err.error?.message || 'Failed to add service to cart.');
        }
      });
    }
  }

  incrementService(serviceId: number, event?: Event): void {
    if (event) event.stopPropagation();
    const match = this.services().find(s => s.id === serviceId);
    if (match && !this.isServiceInCart(serviceId)) {
      this.cartService.addToCart(serviceId, this.tenant()?.id).subscribe({
        next: () => this.showToast(`Added "${match.name}" to booking cart.`),
        error: () => {}
      });
    }
  }

  decrementService(serviceId: number, event?: Event): void {
    if (event) event.stopPropagation();
    this.removeFromCart(serviceId);
  }

  openCartModal(): void {
    if (this.cartServices().length > 0) {
      this.isCartModalOpen.set(true);
    }
  }

  closeCartModal(): void {
    this.isCartModalOpen.set(false);
  }

  openCartDrawer(): void {
    this.openCartModal();
  }

  closeCartDrawer(): void {
    this.closeCartModal();
  }

  removeFromCart(serviceId: number, event?: Event): void {
    if (event) event.stopPropagation();
    const item = this.cartServices().find(s => s.id === serviceId);
    this.cartService.removeFromCart(serviceId, this.tenant()?.id).subscribe({
      next: () => {
        if (this.cartServices().length === 0) {
          this.isCartModalOpen.set(false);
        }
        if (item) {
          this.showToast(`Removed "${item.name}" from booking cart.`);
        }
      },
      error: () => {}
    });
  }

  clearCart(event?: Event): void {
    if (event) event.stopPropagation();
    this.cartService.clearCart(this.tenant()?.id).subscribe({
      next: () => {
        this.isCartModalOpen.set(false);
        this.showToast('Cleared all items from booking cart.');
      },
      error: () => {}
    });
  }

  openCartBooking(): void {
    if (this.cartServices().length === 0) return;
    this.closeCartModal();
    this.selectedServiceId.set(this.cartServices()[0].id);
    this.selectedProviderId.set(null);
    this.bookingInitialStep.set(2);
    this.isBookingModalOpen.set(true);
  }

  openBookingModal(serviceId?: number): void {
    if (!this.route.snapshot.queryParamMap.has('resumeBooking')) {
      sessionStorage.removeItem('pending_booking_draft');
    }
    this.bookingInitialStep.set(1);
    if (serviceId) {
      if (!this.isServiceInCart(serviceId)) {
        this.cartService.addToCart(serviceId, this.tenant()?.id).subscribe({
          next: () => {
            this.selectedServiceId.set(serviceId);
            this.selectedProviderId.set(null);
            this.isBookingModalOpen.set(true);
          },
          error: () => {
            this.selectedServiceId.set(serviceId);
            this.selectedProviderId.set(null);
            this.isBookingModalOpen.set(true);
          }
        });
        return;
      }
      this.selectedServiceId.set(serviceId);
    } else if (this.cartServices().length > 0) {
      this.selectedServiceId.set(this.cartServices()[0].id);
    } else {
      this.selectedServiceId.set(null);
    }
    this.selectedProviderId.set(null);
    this.isBookingModalOpen.set(true);
  }

  closeBookingModal(): void {
    this.selectedServiceId.set(null);
    this.selectedProviderId.set(null);
    this.bookingInitialStep.set(1);
    this.isBookingModalOpen.set(false);
  }

  onAppointmentBooked(_appointment: AppointmentDto): void {
    this.closeBookingModal();
    this.cartService.clearCart(this.tenant()?.id).subscribe({ error: () => {} });
    sessionStorage.removeItem('pending_booking_draft');
    this.router.navigate(['/my-appointments']);
  }

  showToast(msg: string): void {
    this.toastMessage.set(msg);
    setTimeout(() => {
      this.toastMessage.set(null);
    }, 5000);
  }

  getInitials(name?: string): string {
    return getInitials(name || 'ORG');
  }
}
