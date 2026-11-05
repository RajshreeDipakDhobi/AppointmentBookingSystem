import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { ServiceCatalogService } from '../../core/services/service-catalog.service';
import {
  ProviderServicesOverviewDto,
  ProviderAssignedServiceItemDto
} from '../../core/models/service.models';

@Component({
  selector: 'app-provider-services',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterModule
  ],
  templateUrl: './provider-services.component.html',
  styleUrls: ['./provider-services.component.css']
})
export class ProviderServicesComponent implements OnInit {
  private readonly serviceCatalogService = inject(ServiceCatalogService);
  private readonly router = inject(Router);

  readonly overview = signal<ProviderServicesOverviewDto | null>(null);
  readonly isLoading = signal<boolean>(true);
  readonly errorMessage = signal<string | null>(null);
  readonly searchQuery = signal<string>('');
  readonly selectedStatusFilter = signal<'ALL' | 'ACTIVE' | 'INACTIVE'>('ALL');

  // Service Details Modal / Drawer
  readonly selectedServiceForDetails = signal<ProviderAssignedServiceItemDto | null>(null);

  // Computed metrics for stats bar
  readonly activeServicesCount = computed<number>(() => {
    const list = this.overview()?.services || [];
    return list.filter(s => s.isActive).length;
  });

  readonly inactiveServicesCount = computed<number>(() => {
    const list = this.overview()?.services || [];
    return list.filter(s => !s.isActive).length;
  });

  readonly allTimeTotalServed = computed<number>(() => {
    const list = this.overview()?.services || [];
    return list.reduce((sum, s) => sum + (s.allTimeBookingsCount || 0), 0);
  });

  readonly todayTotalBookings = computed<number>(() => {
    const list = this.overview()?.services || [];
    return list.reduce((sum, s) => sum + (s.todayBookingsCount || 0), 0);
  });

  // Filtered assigned services for data grid
  readonly filteredServices = computed<ProviderAssignedServiceItemDto[]>(() => {
    const list = this.overview()?.services || [];
    const query = this.searchQuery().trim().toLowerCase();
    const filter = this.selectedStatusFilter();

    return list.filter(s => {
      const matchesQuery = !query ||
        s.name.toLowerCase().includes(query) ||
        (s.description && s.description.toLowerCase().includes(query));

      const matchesStatus =
        filter === 'ALL' ||
        (filter === 'ACTIVE' && s.isActive) ||
        (filter === 'INACTIVE' && !s.isActive);

      return matchesQuery && matchesStatus;
    });
  });

  ngOnInit(): void {
    this.loadOverview();
  }

  loadOverview(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.serviceCatalogService.getMyAssignedServices().subscribe({
      next: (data) => {
        this.overview.set(data);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.errorMessage.set(
          err.error?.message ||
          'Unable to load your assigned services. Please ensure your specialist account is active.'
        );
      }
    });
  }

  formatDuration(minutes: number): string {
    const m = Number(minutes) || 0;
    if (m <= 0) return '0 mins';
    const hrs = Math.floor(m / 60);
    const rem = m % 60;
    if (hrs === 0) return `${rem} mins`;
    if (rem === 0) return `${hrs} ${hrs === 1 ? 'hr' : 'hrs'}`;
    return `${hrs} hr ${rem} mins`;
  }

  viewAllAppointments(): void {
    this.router.navigate(['/appointments']);
  }

  viewServiceBookings(service: ProviderAssignedServiceItemDto): void {
    this.router.navigate(['/appointments'], { queryParams: { search: service.name } });
  }

  openServiceDetails(service: ProviderAssignedServiceItemDto): void {
    this.selectedServiceForDetails.set(service);
  }

  closeServiceDetails(): void {
    this.selectedServiceForDetails.set(null);
  }
}
