import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AvailabilityService } from '../../core/services/availability.service';
import {
  ProviderAvailabilityDto,
  ProviderAvailabilityOverviewResponseDto,
  ProviderScheduleOverviewItemDto,
  DayScheduleSummaryDto,
  TimeSlotDto
} from '../../core/models/availability.models';
import { AvailabilityModalComponent } from './availability-modal/availability-modal.component';
import { getAvatarGradient } from '../../core/constants/app.constants';

@Component({
  selector: 'app-availability',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, AvailabilityModalComponent],
  templateUrl: './availability.component.html',
  styleUrls: ['./availability.component.css']
})
export class AvailabilityComponent implements OnInit {
  private readonly availabilityService = inject(AvailabilityService);
  readonly getAvatarGradient = getAvatarGradient;

  // Dedicated Backend Overview State
  readonly overview = signal<ProviderAvailabilityOverviewResponseDto | null>(null);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string>('');
  readonly successMessage = signal<string>('');

  // Search & Filter State
  searchQuery = '';
  readonly expandedProviderIds = signal<Set<number>>(new Set<number>());

  // Configuration Modal State
  readonly isConfigModalOpen = signal<boolean>(false);
  selectedProviderId: number | null = null;
  selectedProviderName = '';
  selectedProviderRole = '';
  configModalInitialTab: 'weekly' | 'date-wise' = 'weekly';
  configModalInitialDayIndex: number | null = null;
  configModalInitialDate: string | null = null;

  // View Slots Modal State
  readonly isViewSlotsModalOpen = signal<boolean>(false);
  readonly viewingProvider = signal<ProviderScheduleOverviewItemDto | null>(null);
  viewingMode: 'weekly' | 'specific-date' = 'weekly';
  viewingDayIndex = 0; // selected day tab in View Slots modal
  readonly viewingSlots = signal<TimeSlotDto[]>([]);
  readonly isLoadingViewSlots = signal<boolean>(false);
  readonly viewingSpecificDates = signal<ProviderAvailabilityDto[]>([]);
  selectedViewingSpecificDate: ProviderAvailabilityDto | null = null;

  get filteredProviders(): ProviderScheduleOverviewItemDto[] {
    const list = this.overview()?.providers || [];
    const q = this.searchQuery.trim().toLowerCase();
    if (!q) return list;
    return list.filter(p =>
      p.providerName.toLowerCase().includes(q) ||
      (p.specialization && p.specialization.toLowerCase().includes(q)) ||
      (p.email && p.email.toLowerCase().includes(q)) ||
      p.activeDaysSummary.toLowerCase().includes(q)
    );
  }

  ngOnInit(): void {
    this.loadOverview();
  }

  // Primary API: Get dedicated overview from backend
  loadOverview(): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    this.availabilityService.getAvailabilityOverview().subscribe({
      next: (res) => {
        this.overview.set(res);
        this.expandedProviderIds.set(new Set<number>());
        this.isLoading.set(false);
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to load provider availability overview.');
        this.isLoading.set(false);
      }
    });
  }

  // Row Expand/Collapse Helpers
  toggleExpand(providerId: number, event?: MouseEvent): void {
    if (event) event.stopPropagation();
    const current = new Set(this.expandedProviderIds());
    if (current.has(providerId)) {
      current.delete(providerId);
    } else {
      current.add(providerId);
    }
    this.expandedProviderIds.set(current);
  }

  isExpanded(providerId: number): boolean {
    return this.expandedProviderIds().has(providerId);
  }

  expandAll(): void {
    const allIds = new Set((this.overview()?.providers || []).map(p => p.providerId));
    this.expandedProviderIds.set(allIds);
  }

  collapseAll(): void {
    this.expandedProviderIds.set(new Set<number>());
  }

  getActiveDays(days: DayScheduleSummaryDto[]): DayScheduleSummaryDto[] {
    return (days || []).filter(d => d.isAvailable);
  }

  get allProvidersList(): ProviderScheduleOverviewItemDto[] {
    return this.overview()?.providers || [];
  }

  // Open Separate Configuration Modal
  openConfigureModal(
    provider?: ProviderScheduleOverviewItemDto,
    initialTabOrEvent?: 'weekly' | 'date-wise' | MouseEvent,
    initialDayIndex: number | null = null,
    initialDate: string | null = null,
    event?: MouseEvent
  ): void {
    let initialTab: 'weekly' | 'date-wise' = 'weekly';
    if (initialTabOrEvent instanceof MouseEvent) {
      initialTabOrEvent.stopPropagation();
    } else if (initialTabOrEvent === 'weekly' || initialTabOrEvent === 'date-wise') {
      initialTab = initialTabOrEvent;
    }
    if (event) event.stopPropagation();

    const target = provider || (this.filteredProviders.length > 0 ? this.filteredProviders[0] : null);
    if (!target) return;

    this.selectedProviderId = target.providerId;
    this.selectedProviderName = target.providerName;
    this.selectedProviderRole = target.specialization || 'Specialist';
    this.configModalInitialTab = initialTab;
    this.configModalInitialDayIndex = initialDayIndex;
    this.configModalInitialDate = initialDate;
    this.isConfigModalOpen.set(true);
  }

  closeConfigureModal(): void {
    this.isConfigModalOpen.set(false);
  }

  // Callback when child modal saves
  onScheduleSaved(event: { providerId: number; providerName: string }): void {
    this.successMessage.set(`Schedule for "${event.providerName}" saved successfully!`);
    this.loadOverview();
    setTimeout(() => this.successMessage.set(''), 4500);
  }

  // View Slots Modal (optionally jumping directly to clicked day or specific date)
  openViewSlotsModal(
    provider: ProviderScheduleOverviewItemDto,
    dayIndex?: number,
    specificDateOrEvent?: ProviderAvailabilityDto | MouseEvent,
    event?: MouseEvent
  ): void {
    let specificDate: ProviderAvailabilityDto | undefined;
    if (specificDateOrEvent instanceof MouseEvent) {
      specificDateOrEvent.stopPropagation();
    } else if (specificDateOrEvent && 'isAvailable' in specificDateOrEvent) {
      specificDate = specificDateOrEvent as ProviderAvailabilityDto;
    }
    if (event) event.stopPropagation();

    this.viewingProvider.set(provider);
    this.isViewSlotsModalOpen.set(true);

    // Fetch upcoming specific date overrides & holidays for this provider
    this.availabilityService.getProviderSpecificDates(provider.providerId).subscribe({
      next: (dates) => {
        this.viewingSpecificDates.set(dates || []);
      },
      error: () => {
        this.viewingSpecificDates.set([]);
      }
    });

    if (specificDate) {
      this.viewingMode = 'specific-date';
      this.selectedViewingSpecificDate = specificDate;
      this.loadSlotsForViewingSpecificDate(specificDate);
    } else {
      this.viewingMode = 'weekly';
      this.selectedViewingSpecificDate = null;
      if (dayIndex !== undefined && dayIndex >= 0 && dayIndex < provider.days.length) {
        this.viewingDayIndex = dayIndex;
      } else {
        const firstActiveIndex = provider.days.findIndex(d => d.isAvailable);
        this.viewingDayIndex = firstActiveIndex >= 0 ? firstActiveIndex : 0;
      }
      this.loadSlotsForViewingDay();
    }
  }

  closeViewSlotsModal(): void {
    this.isViewSlotsModalOpen.set(false);
    this.viewingProvider.set(null);
    this.viewingSlots.set([]);
    this.viewingSpecificDates.set([]);
    this.selectedViewingSpecificDate = null;
    this.viewingMode = 'weekly';
  }

  selectViewingDay(index: number): void {
    this.viewingMode = 'weekly';
    this.selectedViewingSpecificDate = null;
    this.viewingDayIndex = index;
    this.loadSlotsForViewingDay();
  }

  selectViewingSpecificDate(sDate: ProviderAvailabilityDto): void {
    this.viewingMode = 'specific-date';
    this.selectedViewingSpecificDate = sDate;
    this.loadSlotsForViewingSpecificDate(sDate);
  }

  get currentViewingDay(): DayScheduleSummaryDto | null {
    const p = this.viewingProvider();
    if (!p || !p.days) return null;
    return p.days[this.viewingDayIndex] || null;
  }

  loadSlotsForViewingDay(): void {
    const day = this.currentViewingDay;
    if (!day || !day.isAvailable || !day.startTime || !day.endTime) {
      this.viewingSlots.set([]);
      return;
    }

    const blockedList = day.blockedSlotList || (day.blockedSlots ? day.blockedSlots.split(',') : undefined);

    this.isLoadingViewSlots.set(true);
    this.availabilityService.getPreviewSlots(
      day.startTime,
      day.endTime,
      day.slotDurationMinutes || 30,
      day.breakStartTime,
      day.breakEndTime,
      blockedList
    ).subscribe({
      next: (slots) => {
        this.viewingSlots.set((slots || []).filter(s => !s.isBreak));
        this.isLoadingViewSlots.set(false);
      },
      error: () => {
        this.viewingSlots.set([]);
        this.isLoadingViewSlots.set(false);
      }
    });
  }

  loadSlotsForViewingSpecificDate(sDate: ProviderAvailabilityDto): void {
    if (!sDate.isAvailable) {
      this.viewingSlots.set([]);
      this.isLoadingViewSlots.set(false);
      return;
    }

    const sTime = sDate.startTime || '10:00';
    const eTime = sDate.endTime || '17:00';
    const dur = sDate.slotDurationMinutes || 30;
    const bStart = sDate.breakStartTime || null;
    const bEnd = sDate.breakEndTime || null;
    const blockedList = sDate.blockedSlotList || (sDate.blockedSlots ? sDate.blockedSlots.split(',').map(s => s.trim()) : undefined);

    this.isLoadingViewSlots.set(true);
    this.availabilityService.getPreviewSlots(sTime, eTime, dur, bStart, bEnd, blockedList).subscribe({
      next: (slots) => {
        this.viewingSlots.set((slots || []).filter(s => !s.isBreak));
        this.isLoadingViewSlots.set(false);
      },
      error: () => {
        this.viewingSlots.set([]);
        this.isLoadingViewSlots.set(false);
      }
    });
  }

  modifyCurrentScheduleFromViewSlots(): void {
    const provider = this.viewingProvider();
    if (!provider) return;

    if (this.viewingMode === 'weekly') {
      const dayIndex = this.viewingDayIndex;
      this.closeViewSlotsModal();
      this.openConfigureModal(provider, 'weekly', dayIndex, null);
    } else {
      const dateStr = this.selectedViewingSpecificDate?.specificDate ? this.selectedViewingSpecificDate.specificDate.split('T')[0] : null;
      this.closeViewSlotsModal();
      this.openConfigureModal(provider, 'date-wise', null, dateStr);
    }
  }

  getInitials(name?: string): string {
    if (!name) return 'SP';
    const parts = name.trim().split(/\s+/);
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return name.substring(0, 2).toUpperCase();
  }

  formatTo12Hour(time24?: string | null): string {
    if (!time24) return '';
    const [hStr, mStr] = time24.split(':');
    let hour = parseInt(hStr, 10);
    if (isNaN(hour)) return time24;
    const minute = mStr !== undefined ? mStr.padStart(2, '0') : '00';
    const period = hour >= 12 ? 'PM' : 'AM';
    hour = hour % 12;
    if (hour === 0) hour = 12;
    const hourStr = hour < 10 ? `0${hour}` : `${hour}`;
    return `${hourStr}:${minute} ${period}`;
  }

  formatSlotDisplay(slot: TimeSlotDto): string {
    let sTime = slot.startTime;
    let eTime = slot.endTime;
    if ((!sTime || !eTime) && slot.displayText && slot.displayText.includes('-')) {
      const parts = slot.displayText.split('-').map(s => s.trim());
      sTime = parts[0];
      eTime = parts[1];
    }
    if (!sTime || !eTime) return slot.displayText || '';

    const sFmt = this.formatTo12Hour(sTime);
    const eFmt = this.formatTo12Hour(eTime);
    return `${sFmt} – ${eFmt}`;
  }

  formatSpecificDateShort(dateStr?: string | null): string {
    if (!dateStr) return '';
    const clean = dateStr.split('T')[0];
    const parts = clean.split('-');
    if (parts.length === 3) {
      const y = parseInt(parts[0], 10);
      const m = parseInt(parts[1], 10) - 1;
      const d = parseInt(parts[2], 10);
      const dt = new Date(y, m, d);
      const day = dt.toLocaleDateString('en-US', { weekday: 'short' });
      const month = dt.toLocaleDateString('en-US', { month: 'short' });
      return `${month} ${d} (${day})`;
    }
    return dateStr;
  }

  formatSpecificDateLong(dateStr?: string | null): string {
    if (!dateStr) return '';
    const clean = dateStr.split('T')[0];
    const parts = clean.split('-');
    if (parts.length === 3) {
      const y = parseInt(parts[0], 10);
      const m = parseInt(parts[1], 10) - 1;
      const d = parseInt(parts[2], 10);
      const dt = new Date(y, m, d);
      return dt.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' });
    }
    return dateStr;
  }
}
