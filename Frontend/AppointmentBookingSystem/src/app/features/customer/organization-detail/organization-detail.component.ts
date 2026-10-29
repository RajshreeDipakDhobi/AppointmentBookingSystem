import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TenantService } from '../../../core/services/tenant.service';
import { ProviderService } from '../../../core/services/provider.service';
import { TenantDto } from '../../../core/models/tenant.models';
import { ServiceProviderDto } from '../../../core/models/provider.models';
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

  readonly tenant = signal<TenantDto | null>(null);
  readonly providers = signal<ServiceProviderDto[]>([]);
  readonly isLoadingTenant = signal<boolean>(true);
  readonly isLoadingProviders = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Filters
  readonly searchQuery = signal<string>('');

  // Modals
  readonly isBookingModalOpen = signal<boolean>(false);
  readonly selectedProviderId = signal<number | null>(null);
  readonly toastMessage = signal<string | null>(null);

  readonly filteredProviders = computed(() => {
    const q = this.searchQuery().trim().toLowerCase();
    let list = this.providers();

    if (q) {
      list = list.filter(p =>
        (p.name && p.name.toLowerCase().includes(q)) ||
        (p.serviceType && p.serviceType.toLowerCase().includes(q)) ||
        (p.address && p.address.toLowerCase().includes(q)) ||
        (p.bio && p.bio.toLowerCase().includes(q))
      );
    }

    return list;
  });

  ngOnInit(): void {
    this.route.paramMap.subscribe(params => {
      const slug = params.get('slug');
      if (slug) {
        this.loadTenantBySlugOrId(slug);
      } else {
        this.backToDirectory();
      }
    });
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

  openBookingModal(providerId?: number): void {
    this.selectedProviderId.set(providerId ?? null);
    this.isBookingModalOpen.set(true);
  }

  closeBookingModal(): void {
    this.selectedProviderId.set(null);
    this.isBookingModalOpen.set(false);
  }

  onAppointmentBooked(appointment: AppointmentDto): void {
    this.closeBookingModal();
    this.showToast(`Appointment booked successfully! Reference #: ${appointment.appointmentNumber || '#' + appointment.id}`);
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
}
