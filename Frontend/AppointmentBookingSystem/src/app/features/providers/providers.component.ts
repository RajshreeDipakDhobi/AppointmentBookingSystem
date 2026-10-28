import { Component, OnInit, inject, signal, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ProviderService } from '../../core/services/provider.service';
import { ServiceProviderDto, ProviderStatus } from '../../core/models/provider.models';
import { ProviderModalComponent } from './provider-modal/provider-modal.component';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { AppInitialsPipe, AppPhonePipe } from '../../shared/pipes';
import { getAvatarGradient } from '../../core/constants/app.constants';
import { ConfirmService } from '../../shared/components/confirmation-modal/confirm.service';

@Component({
  selector: 'app-providers',
  standalone: true,
  imports: [CommonModule, FormsModule, ProviderModalComponent, PaginationComponent, AppInitialsPipe, AppPhonePipe],
  templateUrl: './providers.component.html',
  styleUrls: ['./providers.component.css']
})
export class ProvidersComponent implements OnInit {
  private readonly providerService = inject(ProviderService);
  private readonly confirmService = inject(ConfirmService);

  readonly providers = signal<ServiceProviderDto[]>([]);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string>('');
  readonly successMessage = signal<string>('');

  // Pagination State
  readonly currentPage = signal<number>(1);
  readonly pageSize = signal<number>(5);
  readonly totalCount = signal<number>(0);
  readonly totalPages = signal<number>(1);

  // Modal State
  readonly isModalOpen = signal<boolean>(false);
  readonly modalMode = signal<'create' | 'edit'>('create');
  readonly selectedProvider = signal<ServiceProviderDto | null>(null);

  // Row Status Dropdown State
  readonly openStatusDropdownId = signal<number | null>(null);

  // Custom Status Filter Dropdown State
  readonly isStatusFilterDropdownOpen = signal<boolean>(false);

  get statusFilterLabel(): string {
    if (this.statusFilter === '1') return 'Active Only';
    if (this.statusFilter === '2') return 'Inactive Only';
    return 'All Status';
  }

  toggleStatusFilterDropdown(event: MouseEvent): void {
    event.stopPropagation();
    this.isStatusFilterDropdownOpen.update(v => !v);
  }

  selectStatusFilter(val: string, event: MouseEvent): void {
    event.stopPropagation();
    this.statusFilter = val;
    this.isStatusFilterDropdownOpen.set(false);
    this.onFilterChange();
  }

  searchQuery = '';
  statusFilter: string = 'all';

  ngOnInit(): void {
    this.loadProviders();
  }

  loadProviders(): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    const statusParam = this.statusFilter === 'all'
      ? undefined
      : (Number(this.statusFilter) as ProviderStatus);

    this.providerService.getProvidersPaged({
      pageNumber: this.currentPage(),
      pageSize: this.pageSize(),
      search: this.searchQuery.trim() || undefined
    }, statusParam).subscribe({
      next: (res) => {
        this.providers.set(res.items);
        this.totalCount.set(res.totalCount);
        this.totalPages.set(res.totalPages);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to load service providers.');
        this.isLoading.set(false);
      }
    });
  }

  onSearchChange(): void {
    this.currentPage.set(1);
    this.loadProviders();
  }

  onFilterChange(): void {
    this.currentPage.set(1);
    this.loadProviders();
  }

  onPageChange(page: number): void {
    this.currentPage.set(page);
    this.loadProviders();
  }

  onPageSizeChange(size: number): void {
    this.pageSize.set(size);
    this.currentPage.set(1);
    this.loadProviders();
  }

  openCreateModal(): void {
    this.modalMode.set('create');
    this.selectedProvider.set(null);
    this.isModalOpen.set(true);
  }

  openEditModal(provider: ServiceProviderDto): void {
    this.modalMode.set('edit');
    this.selectedProvider.set(provider);
    this.isModalOpen.set(true);
  }

  onModalClose(): void {
    this.isModalOpen.set(false);
    this.selectedProvider.set(null);
  }

  onProviderSaved(savedProvider: ServiceProviderDto): void {
    this.isModalOpen.set(false);
    this.selectedProvider.set(null);

    const msg = this.modalMode() === 'create'
      ? `Provider "${savedProvider.name}" added successfully!`
      : `Provider "${savedProvider.name}" updated successfully!`;

    this.successMessage.set(msg);
    this.loadProviders();

    setTimeout(() => this.successMessage.set(''), 4000);
  }

  toggleStatusDropdown(providerId: number, event: MouseEvent): void {
    event.stopPropagation();
    if (this.openStatusDropdownId() === providerId) {
      this.openStatusDropdownId.set(null);
    } else {
      this.openStatusDropdownId.set(providerId);
    }
  }

  closeStatusDropdown(): void {
    this.openStatusDropdownId.set(null);
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    this.closeStatusDropdown();
    this.isStatusFilterDropdownOpen.set(false);
  }

  async selectStatus(provider: ServiceProviderDto, newStatus: number, event: MouseEvent): Promise<void> {
    event.stopPropagation();
    this.closeStatusDropdown();
    await this.onStatusChange(provider, newStatus);
  }

  async onStatusChange(provider: ServiceProviderDto, newStatus: number | string): Promise<void> {
    const statusEnum = Number(newStatus) as ProviderStatus;
    if (provider.status === statusEnum) return;

    const actionText = statusEnum === ProviderStatus.Active ? 'Activate' : 'Deactivate';
    const confirmed = await this.confirmService.confirm({
      title: `${actionText} Provider`,
      message: `Are you sure you want to ${actionText.toLowerCase()} "${provider.name}"?`,
      confirmText: 'Yes',
      cancelText: 'No',
      variant: statusEnum === ProviderStatus.Active ? 'primary' : 'warning'
    });

    if (!confirmed) {
      return;
    }

    const previousStatus = provider.status;
    provider.status = statusEnum;

    this.providerService.updateProviderStatus(provider.id, statusEnum).subscribe({
      next: () => {
        this.successMessage.set(`Provider "${provider.name}" ${actionText.toLowerCase()}d successfully.`);
        this.loadProviders();
        setTimeout(() => this.successMessage.set(''), 3500);
      },
      error: (err) => {
        provider.status = previousStatus;
        this.confirmService.alert({
          title: 'Update Failed',
          message: err.error?.message || 'Failed to update provider status.',
          variant: 'danger'
        });
        this.loadProviders();
      }
    });
  }

  async deleteProvider(provider: ServiceProviderDto): Promise<void> {
    const confirmed = await this.confirmService.confirm({
      title: 'Delete Provider',
      message: `Are you sure you want to delete "${provider.name}"? This action cannot be undone.`,
      confirmText: 'Yes',
      cancelText: 'No',
      variant: 'danger'
    });

    if (!confirmed) {
      return;
    }

    this.providerService.deleteProvider(provider.id).subscribe({
      next: () => {
        this.successMessage.set(`Provider "${provider.name}" deleted successfully.`);
        this.loadProviders();
        setTimeout(() => this.successMessage.set(''), 4000);
      },
      error: (err) => {
        this.confirmService.alert({
          title: 'Deletion Failed',
          message: err.error?.message || 'Failed to delete provider.',
          variant: 'danger'
        });
      }
    });
  }

  readonly getAvatarGradient = getAvatarGradient;
}
