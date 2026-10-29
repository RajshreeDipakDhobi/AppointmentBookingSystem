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

  @Input() set searchQuery(val: string) {
    if (val !== undefined && val !== null) {
      this.searchTerm.set(val);
    }
  }

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

  readonly searchTerm = signal<string>('');

  readonly filteredTenants = computed(() => {
    const q = this.searchTerm().trim().toLowerCase();
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

  onSearchChange(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.searchTerm.set(input?.value || '');
  }

  clearSearch(): void {
    this.searchTerm.set('');
  }

  getInitials(name: string): string {
    if (!name) return 'O';
    const trimmed = name.trim();
    return trimmed.charAt(0).toUpperCase();
  }

  isSalon(name: string): boolean {
    if (!name) return false;
    const n = name.toLowerCase();
    return n.includes('salon') || n.includes('hair') || n.includes('apex') || n.includes('beauty') || n.includes('barber');
  }

  isDental(name: string): boolean {
    if (!name) return false;
    const n = name.toLowerCase();
    return n.includes('dental') || n.includes('dent') || n.includes('appollo') || n.includes('tooth') || n.includes('teeth');
  }

  isWellness(name: string): boolean {
    if (!name) return false;
    const n = name.toLowerCase();
    return n.includes('wellness') || n.includes('spa') || n.includes('yoga') || n.includes('massage') || n.includes('lotus');
  }

  isClinic(name: string): boolean {
    if (!name) return false;
    const n = name.toLowerCase();
    return n.includes('clinic') || n.includes('health') || n.includes('hospital') || n.includes('medical');
  }

  getAvatarThemeClass(name: string): string {
    if (this.isDental(name)) return 'avatar-sage';
    if (this.isWellness(name)) return 'avatar-wellness';
    if (this.isClinic(name)) return 'avatar-clinic';
    if (this.isSalon(name)) return 'avatar-emerald';
    return 'avatar-dark-emerald';
  }
}
