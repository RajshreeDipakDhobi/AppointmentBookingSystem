import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { TenantDto, TenantStatus } from '../../../core/models/tenant.models';
import { AppDatePipe, AppPhonePipe } from '../../../shared/pipes';

@Component({
  selector: 'app-tenant-profile-modal',
  standalone: true,
  imports: [CommonModule, RouterModule, AppDatePipe, AppPhonePipe],
  templateUrl: './tenant-profile-modal.component.html',
  styleUrls: ['./tenant-profile-modal.component.css']
})
export class TenantProfileModalComponent {
  @Input() isOpen = false;
  @Input() tenant: TenantDto | null = null;
  @Output() close = new EventEmitter<void>();

  readonly TenantStatus = TenantStatus;

  get adminName(): string {
    if (!this.tenant) return 'Not configured';
    if (this.tenant.adminFullName) return this.tenant.adminFullName;
    if (this.tenant.adminFirstName) {
      return `${this.tenant.adminFirstName} ${this.tenant.adminLastName || ''}`.trim();
    }
    return 'Primary Administrator';
  }

  get initials(): string {
    if (!this.tenant?.name) return 'OR';
    const parts = this.tenant.name.trim().split(/\s+/);
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return this.tenant.name.slice(0, 2).toUpperCase();
  }

  onClose(): void {
    this.close.emit();
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('modal-backdrop')) {
      this.onClose();
    }
  }

  getStatusClass(status?: TenantStatus): string {
    switch (status) {
      case TenantStatus.Active:
        return 'status-active';
      case TenantStatus.Inactive:
        return 'status-inactive';
      case TenantStatus.Suspended:
        return 'status-suspended';
      default:
        return 'status-inactive';
    }
  }

  getStatusLabel(status?: TenantStatus): string {
    switch (status) {
      case TenantStatus.Active:
        return 'ACTIVE';
      case TenantStatus.Inactive:
        return 'INACTIVE';
      case TenantStatus.Suspended:
        return 'SUSPENDED';
      default:
        return 'UNKNOWN';
    }
  }
}
