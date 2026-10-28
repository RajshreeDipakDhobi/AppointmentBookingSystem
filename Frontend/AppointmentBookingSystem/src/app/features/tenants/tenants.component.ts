import { Component, OnInit, inject, signal, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TenantService } from '../../core/services/tenant.service';
import { TenantDto, TenantStatus } from '../../core/models/tenant.models';
import { TenantModalComponent } from './tenant-modal/tenant-modal.component';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { AppDatePipe, AppPhonePipe } from '../../shared/pipes';
import { ConfirmService } from '../../shared/components/confirmation-modal/confirm.service';

@Component({
  selector: 'app-tenants',
  standalone: true,
  imports: [CommonModule, FormsModule, TenantModalComponent, PaginationComponent, AppDatePipe, AppPhonePipe],
  templateUrl: './tenants.component.html',
  styleUrls: ['./tenants.component.css']
})
export class TenantsComponent implements OnInit {
  private readonly tenantService = inject(TenantService);
  private readonly confirmService = inject(ConfirmService);

  readonly tenants = signal<TenantDto[]>([]);
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
  readonly selectedTenant = signal<TenantDto | null>(null);

  searchQuery = '';

  ngOnInit(): void {
    this.loadTenants();
  }

  loadTenants(): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    this.tenantService.getAllTenants({
      pageNumber: this.currentPage(),
      pageSize: this.pageSize(),
      search: this.searchQuery.trim() || undefined
    }).subscribe({
      next: (res) => {
        this.tenants.set(res.items);
        this.totalCount.set(res.totalCount);
        this.totalPages.set(res.totalPages);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to load tenants. Ensure backend is running.');
        this.isLoading.set(false);
      }
    });
  }

  onSearchChange(): void {
    this.currentPage.set(1);
    this.loadTenants();
  }

  onPageChange(page: number): void {
    this.currentPage.set(page);
    this.loadTenants();
  }

  onPageSizeChange(size: number): void {
    this.pageSize.set(size);
    this.currentPage.set(1);
    this.loadTenants();
  }

  openCreateModal(): void {
    this.modalMode.set('create');
    this.selectedTenant.set(null);
    this.isModalOpen.set(true);
  }

  openEditModal(tenant: TenantDto): void {
    this.modalMode.set('edit');
    this.selectedTenant.set(tenant);
    this.isModalOpen.set(true);
  }

  onModalClose(): void {
    this.isModalOpen.set(false);
    this.selectedTenant.set(null);
  }

  onTenantSaved(savedTenant: TenantDto): void {
    this.isModalOpen.set(false);
    this.selectedTenant.set(null);

    const msg = this.modalMode() === 'create'
      ? `Organization "${savedTenant.name}" registered successfully!`
      : `Organization "${savedTenant.name}" updated successfully!`;

    this.successMessage.set(msg);
    this.loadTenants();

    setTimeout(() => this.successMessage.set(''), 4000);
  }

  readonly updatingStatusTenantId = signal<number | null>(null);
  readonly activeStatusMenuId = signal<number | null>(null);

  @HostListener('document:click')
  onDocumentClick(): void {
    this.activeStatusMenuId.set(null);
  }

  isUpdatingStatus(tenantId: number): boolean {
    return this.updatingStatusTenantId() === tenantId;
  }

  toggleStatusMenu(tenantId: number, event: MouseEvent): void {
    event.stopPropagation();
    if (this.activeStatusMenuId() === tenantId) {
      this.activeStatusMenuId.set(null);
    } else {
      this.activeStatusMenuId.set(tenantId);
    }
  }

  getStatusClass(status: number): string {
    switch (Number(status)) {
      case TenantStatus.Active:
        return 'status-active';
      case TenantStatus.Inactive:
        return 'status-inactive';
      case TenantStatus.Suspended:
        return 'status-suspended';
      default:
        return 'status-active';
    }
  }

  getStatusLabel(status: number): string {
    switch (Number(status)) {
      case TenantStatus.Active:
        return 'Active';
      case TenantStatus.Inactive:
        return 'Inactive';
      case TenantStatus.Suspended:
        return 'Suspended';
      default:
        return 'Active';
    }
  }

  async changeStatus(tenant: TenantDto, newStatus: TenantStatus, event: MouseEvent): Promise<void> {
    event.stopPropagation();
    this.activeStatusMenuId.set(null);

    if (tenant.status === newStatus) return;

    const label = this.getStatusLabel(newStatus);
    const isDanger = newStatus === TenantStatus.Suspended;
    const confirmed = await this.confirmService.confirm({
      title: 'Update Organization Status',
      message: `Are you sure you want to change "${tenant.name}" status to "${label}"?`,
      confirmText: `Set ${label}`,
      variant: isDanger ? 'danger' : 'warning'
    });

    if (!confirmed) {
      return;
    }

    this.updatingStatusTenantId.set(tenant.id);

    this.tenantService.updateTenantStatus(tenant.id, newStatus).subscribe({
      next: () => {
        tenant.status = newStatus;
        this.updatingStatusTenantId.set(null);
        this.successMessage.set(`Organization "${tenant.name}" status updated to ${label}.`);
        setTimeout(() => this.successMessage.set(''), 3000);
      },
      error: (err) => {
        this.updatingStatusTenantId.set(null);
        this.confirmService.alert({
          title: 'Update Failed',
          message: err.error?.message || 'Failed to update tenant status.',
          variant: 'danger'
        });
      }
    });
  }
}
