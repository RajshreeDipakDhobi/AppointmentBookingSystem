import { Component, OnInit, inject, signal, computed, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ServiceDto } from '../../core/models/service.models';
import { ServiceCatalogService } from '../../core/services/service-catalog.service';
import { ServiceModalComponent } from './service-modal/service-modal.component';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';

@Component({
  selector: 'app-services',
  standalone: true,
  imports: [CommonModule, FormsModule, ServiceModalComponent, PaginationComponent],
  templateUrl: './services.component.html',
  styleUrls: ['./services.component.css']
})
export class ServicesComponent implements OnInit {
  private readonly serviceCatalogService = inject(ServiceCatalogService);

  readonly services = signal<ServiceDto[]>([]);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);

  // Search & Filters (Signals for 100% reactive filtering)
  readonly searchQuery = signal<string>('');
  readonly statusFilter = signal<'all' | 'active' | 'inactive'>('all');
  readonly isStatusFilterDropdownOpen = signal<boolean>(false);
  readonly openStatusDropdownId = signal<number | null>(null);

  // Pagination Signals (Default pageSize: 5)
  readonly currentPage = signal<number>(1);
  readonly pageSize = signal<number>(5);

  // Modal State
  readonly isModalOpen = signal<boolean>(false);
  readonly modalMode = signal<'create' | 'edit'>('create');
  readonly selectedService = signal<ServiceDto | null>(null);

  // Delete Confirmation State
  readonly serviceToDelete = signal<ServiceDto | null>(null);
  readonly isDeleting = signal<boolean>(false);
  readonly deleteError = signal<string | null>(null);

  // Quick Stats Computed
  readonly totalServicesCount = computed(() => this.services().length);
  readonly activeServicesCount = computed(() => this.services().filter(s => s.isActive).length);
  readonly inactiveServicesCount = computed(() => this.services().filter(s => !s.isActive).length);
  readonly totalProvidersAllocated = computed(() => this.services().reduce((acc, s) => acc + (s.providersCount || 0), 0));

  // Filtered Services List (Directly reacts to searchQuery() and statusFilter() signals)
  readonly filteredServices = computed(() => {
    let list = this.services();
    const query = this.searchQuery().trim().toLowerCase();

    if (query) {
      list = list.filter(s =>
        s.name.toLowerCase().includes(query) ||
        (s.createdByName && s.createdByName.toLowerCase().includes(query))
      );
    }

    const currentFilter = this.statusFilter();
    if (currentFilter === 'active') {
      list = list.filter(s => s.isActive);
    } else if (currentFilter === 'inactive') {
      list = list.filter(s => !s.isActive);
    }

    return list;
  });

  // Total count for pagination
  readonly totalCount = computed(() => this.filteredServices().length);

  // Total pages
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));

  // Sliced page items for current page
  readonly pagedServices = computed(() => {
    const start = (this.currentPage() - 1) * this.pageSize();
    return this.filteredServices().slice(start, start + this.pageSize());
  });

  readonly statusFilterLabel = computed<string>(() => {
    if (this.statusFilter() === 'active') return 'Active Only';
    if (this.statusFilter() === 'inactive') return 'Inactive Only';
    return 'All Status';
  });

  @HostListener('document:click')
  onDocumentClick(): void {
    this.openStatusDropdownId.set(null);
    this.isStatusFilterDropdownOpen.set(false);
  }

  ngOnInit(): void {
    this.loadServices();
  }

  loadServices(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.serviceCatalogService.getAllServices().subscribe({
      next: (data) => {
        this.services.set(data || []);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.errorMessage.set(err.error?.message || err.message || 'Failed to load services.');
      }
    });
  }

  onSearchChange(value: string): void {
    this.searchQuery.set(value || '');
    this.currentPage.set(1);
  }

  clearSearch(): void {
    this.searchQuery.set('');
    this.currentPage.set(1);
  }

  toggleStatusFilterDropdown(event: MouseEvent): void {
    event.stopPropagation();
    this.openStatusDropdownId.set(null);
    this.isStatusFilterDropdownOpen.update(v => !v);
  }

  selectStatusFilter(filter: 'all' | 'active' | 'inactive', event: MouseEvent): void {
    event.stopPropagation();
    this.statusFilter.set(filter);
    this.currentPage.set(1);
    this.isStatusFilterDropdownOpen.set(false);
  }

  onPageChange(page: number): void {
    this.currentPage.set(page);
  }

  onPageSizeChange(size: number): void {
    this.pageSize.set(size);
    this.currentPage.set(1);
  }

  toggleStatusDropdown(id: number, event: MouseEvent): void {
    event.stopPropagation();
    this.isStatusFilterDropdownOpen.set(false);
    this.openStatusDropdownId.update(curr => (curr === id ? null : id));
  }

  selectServiceStatus(service: ServiceDto, targetActive: boolean, event: MouseEvent): void {
    event.stopPropagation();
    this.openStatusDropdownId.set(null);
    if (service.isActive === targetActive) return;
    this.toggleServiceStatus(service, event);
  }

  openCreateModal(): void {
    this.modalMode.set('create');
    this.selectedService.set(null);
    this.isModalOpen.set(true);
  }

  openEditModal(service: ServiceDto, event?: MouseEvent): void {
    if (event) event.stopPropagation();
    this.modalMode.set('edit');
    this.selectedService.set(service);
    this.isModalOpen.set(true);
  }

  closeModal(): void {
    this.isModalOpen.set(false);
    this.selectedService.set(null);
  }

  onServiceSaved(savedService: ServiceDto): void {
    this.closeModal();
    const mode = this.modalMode();
    this.showSuccess(
      mode === 'create'
        ? `Service "${savedService.name}" created successfully.`
        : `Service "${savedService.name}" updated successfully.`
    );
    this.loadServices();
  }

  toggleServiceStatus(service: ServiceDto, event?: MouseEvent): void {
    if (event) event.stopPropagation();
    this.serviceCatalogService.toggleServiceStatus(service.id).subscribe({
      next: () => {
        const nextState = !service.isActive;
        this.showSuccess(`Service "${service.name}" is now ${nextState ? 'Active' : 'Inactive'}.`);
        this.services.update(list =>
          list.map(s => s.id === service.id ? { ...s, isActive: nextState } : s)
        );
      },
      error: (err) => {
        this.showError(err.error?.message || 'Failed to toggle service status.');
      }
    });
  }

  confirmDelete(service: ServiceDto, event: MouseEvent): void {
    event.stopPropagation();
    this.deleteError.set(null);
    this.serviceToDelete.set(service);
  }

  cancelDelete(): void {
    this.serviceToDelete.set(null);
    this.deleteError.set(null);
    this.isDeleting.set(false);
  }

  executeDelete(): void {
    const toDelete = this.serviceToDelete();
    if (!toDelete) return;

    this.isDeleting.set(true);
    this.deleteError.set(null);

    this.serviceCatalogService.deleteService(toDelete.id).subscribe({
      next: () => {
        this.isDeleting.set(false);
        this.showSuccess(`Service "${toDelete.name}" deleted successfully.`);
        this.serviceToDelete.set(null);
        this.services.update(list => list.filter(s => s.id !== toDelete.id));
      },
      error: (err) => {
        this.isDeleting.set(false);
        this.deleteError.set(err.error?.message || 'Unable to delete service. Active appointments may be linked.');
      }
    });
  }

  private showSuccess(msg: string): void {
    this.successMessage.set(msg);
    setTimeout(() => {
      if (this.successMessage() === msg) {
        this.successMessage.set(null);
      }
    }, 4000);
  }

  private showError(msg: string): void {
    this.errorMessage.set(msg);
    setTimeout(() => {
      if (this.errorMessage() === msg) {
        this.errorMessage.set(null);
      }
    }, 5000);
  }
}
