import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TenantService } from '../../../core/services/tenant.service';
import { ProviderService } from '../../../core/services/provider.service';
import { ServiceCatalogService } from '../../../core/services/service-catalog.service';
import { AuthService } from '../../../core/services/auth.service';
import { TenantDto } from '../../../core/models/tenant.models';
import { ServiceProviderDto } from '../../../core/models/provider.models';
import { ServiceDto } from '../../../core/models/service.models';
import { AppointmentDto } from '../../../core/models/appointment.models';
import { BookingModalComponent } from '../../appointments/booking-modal/booking-modal.component';
import { TopbarComponent } from '../../../layout/topbar/topbar.component';
import { AppPhonePipe } from '../../../shared/pipes';
import { getInitials } from '../../../shared/utils/formatters';

@Component({
  selector: 'app-organization-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    FormsModule,
    TopbarComponent,
    BookingModalComponent,
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
  readonly toastMessage = signal<string | null>(null);

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
    this.serviceCatalogService.getActiveServices(tenantId).subscribe({
      next: (list) => {
        this.services.set(list || []);
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
          if (draft.serviceId) {
            this.selectedServiceId.set(draft.serviceId);
            this.selectedProviderId.set(null);
            this.isBookingModalOpen.set(true);
          } else if (draft.providerId) {
            this.selectedProviderId.set(draft.providerId);
            this.selectedServiceId.set(null);
            this.isBookingModalOpen.set(true);
          } else {
            this.selectedServiceId.set(null);
            this.selectedProviderId.set(null);
            this.isBookingModalOpen.set(true);
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

  openBookingModal(serviceId?: number): void {
    this.selectedServiceId.set(serviceId ?? null);
    this.selectedProviderId.set(null);
    this.isBookingModalOpen.set(true);
  }

  closeBookingModal(): void {
    this.selectedServiceId.set(null);
    this.selectedProviderId.set(null);
    this.isBookingModalOpen.set(false);
  }

  onAppointmentBooked(_appointment: AppointmentDto): void {
    this.closeBookingModal();
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
