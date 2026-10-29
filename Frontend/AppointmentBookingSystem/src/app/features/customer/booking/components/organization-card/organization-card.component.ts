import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TenantDto } from '../../../../../core/models/tenant.models';

@Component({
  selector: 'app-organization-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './organization-card.component.html',
  styleUrls: ['./organization-card.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class OrganizationCardComponent {
  @Input({ required: true }) tenant!: TenantDto;
  @Input() isSelected: boolean = false;
  @Output() viewDetails = new EventEmitter<TenantDto>();

  get imageUrl(): string {
    return this.tenant.logoUrl || '';
  }

  getTags(): string[] {
    return this.tenant.serviceTypes || [];
  }

  getProviderCountText(): string {
    const count = this.tenant.totalProvidersCount ?? 0;
    return `${count} Specialists`;
  }

  onCardClick(): void {
    this.viewDetails.emit(this.tenant);
  }
}
