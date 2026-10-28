import { Component, EventEmitter, Input, OnInit, Output, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TenantDto } from '../../../../../core/models/tenant.models';
import { TenantService } from '../../../../../core/services/tenant.service';

import { AppPhonePipe } from '../../../../../shared/pipes';

@Component({
  selector: 'app-organizations',
  standalone: true,
  imports: [CommonModule, AppPhonePipe],
  templateUrl: './organizations.component.html',
  styleUrls: ['./organizations.component.css']
})
export class OrganizationsComponent implements OnInit {
  private readonly tenantService = inject(TenantService);

  @Input() searchQuery: string = '';
  
  @Input() set organizations(list: TenantDto[] | null) {
    if (list && list.length > 0) {
      this.tenants.set(list);
      this.isLoading.set(false);
    }
  }

  @Output() selectOrganization = new EventEmitter<TenantDto>();

  readonly tenants = signal<TenantDto[]>([]);
  readonly isLoading = signal<boolean>(true);
  readonly errorMessage = signal<string | null>(null);

  readonly filteredTenants = computed(() => {
    const q = (this.searchQuery || '').trim().toLowerCase();
    const list = this.tenants();
    if (!q) return list;
    return list.filter(t =>
      t.name.toLowerCase().includes(q) ||
      (t.address && t.address.toLowerCase().includes(q)) ||
      (t.tenantCode && t.tenantCode.toLowerCase().includes(q)) ||
      (t.slug && t.slug.toLowerCase().includes(q)) ||
      (t.contactEmail && t.contactEmail.toLowerCase().includes(q))
    );
  });

  ngOnInit(): void {
    if (this.tenants().length === 0) {
      this.loadOrganizations();
    }
  }

  loadOrganizations(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    this.tenantService.getActiveTenants().subscribe({
      next: (list) => {
        this.tenants.set(list || []);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.errorMessage.set('Unable to load organizations. Please try again.');
      }
    });
  }

  onSelect(tenant: TenantDto): void {
    this.selectOrganization.emit(tenant);
  }

  getInitials(name: string): string {
    if (!name) return 'ORG';
    const parts = name.trim().split(/\s+/);
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return name.slice(0, 2).toUpperCase();
  }
}
