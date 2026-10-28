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

  getRating(): string {
    const r = this.tenant.rating ? this.tenant.rating.toFixed(1) : '5.0';
    const rev = this.tenant.totalReviewsCount ?? 0;
    const revText = rev >= 1000 ? (rev / 1000).toFixed(1) + 'k' : rev.toString();
    return `${r} (${revText} reviews)`;
  }

  getProviderCountText(): string {
    const count = this.tenant.totalProvidersCount ?? 0;
    return `${count} Specialists`;
  }

  onCardClick(): void {
    this.viewDetails.emit(this.tenant);
  }
}
