import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { ServiceCatalogService } from '../../core/services/service-catalog.service';
import { AuthService } from '../../core/services/auth.service';
import { ProviderServicesOverviewDto, ProviderAssignedServiceItemDto } from '../../core/models/service.models';

@Component({
  selector: 'app-provider-services',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './provider-services.component.html',
  styleUrls: ['./provider-services.component.css']
})
export class ProviderServicesComponent implements OnInit {
  private readonly serviceCatalogService = inject(ServiceCatalogService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  readonly overview = signal<ProviderServicesOverviewDto | null>(null);
  readonly isLoading = signal<boolean>(true);
  readonly errorMessage = signal<string | null>(null);
  readonly searchQuery = signal<string>('');

  // Filtered assigned services
  readonly filteredServices = computed<ProviderAssignedServiceItemDto[]>(() => {
    const list = this.overview()?.services || [];
    const query = this.searchQuery().trim().toLowerCase();
    if (!query) return list;
    return list.filter(s =>
      s.name.toLowerCase().includes(query) ||
      (s.description && s.description.toLowerCase().includes(query))
    );
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
}
