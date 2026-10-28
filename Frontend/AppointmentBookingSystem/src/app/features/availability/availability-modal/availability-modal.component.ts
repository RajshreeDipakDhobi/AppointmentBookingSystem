import {
  Component,
  EventEmitter,
  HostListener,
  Input,
  OnChanges,
  Output,
  SimpleChanges,
  inject,
  signal
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AvailabilityService } from '../../../core/services/availability.service';
import { ConfirmService } from '../../../shared/components/confirmation-modal/confirm.service';
import { getAvatarGradient } from '../../../core/constants/app.constants';
import {
  ProviderAvailabilityDto,
  ProviderScheduleOverviewItemDto,
  SaveWeeklyAvailabilityRequestDto,
  SaveDateSpecificAvailabilityRequestDto,
  TimeSlotDto
} from '../../../core/models/availability.models';

export interface DayConfig {
  dayOfWeek: number; // 1=Mon, 2=Tue, 3=Wed, 4=Thu, 5=Fri, 6=Sat, 0=Sun
  shortName: string;
  fullName: string;
  isActive: boolean;
  startTime: string;
  endTime: string;
  hasBreak: boolean;
  breakStartTime: string;
  breakEndTime: string;
  slotDurationMinutes: number;
  calculatedSlotsCount: number;
  blockedSlots: string[]; // start times e.g. ["10:30", "14:00"]
  slots: TimeSlotDto[];
}

export interface HourOption {
  h24: number;        // 0 to 23
  h12: number;        // 1 to 12
  period: 'AM' | 'PM';
  display: string;    // "10 AM", "01 PM", etc.
}

@Component({
  selector: 'app-availability-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './availability-modal.component.html',
  styleUrls: ['./availability-modal.component.css']
})
export class AvailabilityModalComponent implements OnChanges {
  private readonly availabilityService = inject(AvailabilityService);
  private readonly confirmService = inject(ConfirmService);
  readonly getAvatarGradient = getAvatarGradient;

  get activeProviderPhoto(): string | null {
    const id = this.selectedProviderId || this.providerId;
    if (id && this.providers.length > 0) {
      const found = this.providers.find(p => p.providerId === id);
      return found?.profilePictureUrl || null;
    }
    return null;
  }

  onActivePhotoError(): void {
    const id = this.selectedProviderId || this.providerId;
    const found = this.providers.find(p => p.providerId === id);
    if (found) {
      found.profilePictureUrl = null;
    }
  }

  @Input() isOpen = false;
  @Input() providers: ProviderScheduleOverviewItemDto[] = [];
  @Input() providerId: number | null = null;
  @Input() providerName = '';
  @Input() providerRole = '';
  @Input() initialTab: 'weekly' | 'date-wise' = 'weekly';
  @Input() initialDayIndex: number | null = null;
  @Input() initialDate: string | null = null;

  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<{ providerId: number; providerName: string }>();

  selectedProviderId: number | null = null;
  isProviderMenuOpen = false;

  // Modern Navigation: 'weekly' (Standard Weekly Hours) vs 'date-wise' (Special Dates & Holidays)
  activeTab: 'weekly' | 'date-wise' = 'weekly';

  readonly isLoading = signal<boolean>(false);
  readonly isSaving = signal<boolean>(false);
  readonly errorMessage = signal<string>('');
  readonly successMessage = signal<string>('');

  selectedDayIndex = 0; // index in this.days (0 = Mon, 1 = Tue, etc.)

  // 2-Column Time Dropdown State (Scrollable HR and MM)
  openTimeDropdown: 'startTime' | 'endTime' | 'breakStart' | 'breakEnd' | null = null;

  // Track database record IDs for each dayOfWeek (to delete unselected records from DB)
  private dayRecordIds = new Map<number, number>();

  // Full 24-hour range (00:00 to 23:00 / 12 AM to 11 PM) - No arbitrary hardcoded limits
  readonly hoursList: HourOption[] = Array.from({ length: 24 }, (_, i) => {
    const h12 = i % 12 === 0 ? 12 : i % 12;
    const period: 'AM' | 'PM' = i >= 12 ? 'PM' : 'AM';
    const display = `${h12.toString().padStart(2, '0')} ${period}`;
    return { h24: i, h12, period, display };
  });

  readonly minutesList: string[] = ['00', '15', '30', '45'];
  readonly durationOptions = [
    { value: 15, label: '15 Minutes' },
    { value: 30, label: '30 Minutes' },
    { value: 45, label: '45 Minutes' },
    { value: 60, label: '60 Minutes' },
    { value: 90, label: '90 Minutes' },
    { value: 120, label: '120 Minutes' }
  ];

  // Clean uniform initial defaults (Saturday is NOT hardcoded to 14:00)
  days: DayConfig[] = [
    { dayOfWeek: 1, shortName: 'Mon', fullName: 'Monday', isActive: true, startTime: '10:00', endTime: '17:00', hasBreak: false, breakStartTime: '13:00', breakEndTime: '14:00', slotDurationMinutes: 30, calculatedSlotsCount: 0, blockedSlots: [], slots: [] },
    { dayOfWeek: 2, shortName: 'Tue', fullName: 'Tuesday', isActive: true, startTime: '10:00', endTime: '17:00', hasBreak: false, breakStartTime: '13:00', breakEndTime: '14:00', slotDurationMinutes: 30, calculatedSlotsCount: 0, blockedSlots: [], slots: [] },
    { dayOfWeek: 3, shortName: 'Wed', fullName: 'Wednesday', isActive: true, startTime: '10:00', endTime: '17:00', hasBreak: false, breakStartTime: '13:00', breakEndTime: '14:00', slotDurationMinutes: 30, calculatedSlotsCount: 0, blockedSlots: [], slots: [] },
    { dayOfWeek: 4, shortName: 'Thu', fullName: 'Thursday', isActive: true, startTime: '10:00', endTime: '17:00', hasBreak: false, breakStartTime: '13:00', breakEndTime: '14:00', slotDurationMinutes: 30, calculatedSlotsCount: 0, blockedSlots: [], slots: [] },
    { dayOfWeek: 5, shortName: 'Fri', fullName: 'Friday', isActive: true, startTime: '10:00', endTime: '17:00', hasBreak: false, breakStartTime: '13:00', breakEndTime: '14:00', slotDurationMinutes: 30, calculatedSlotsCount: 0, blockedSlots: [], slots: [] },
    { dayOfWeek: 6, shortName: 'Sat', fullName: 'Saturday', isActive: true, startTime: '10:00', endTime: '17:00', hasBreak: false, breakStartTime: '13:00', breakEndTime: '14:00', slotDurationMinutes: 30, calculatedSlotsCount: 0, blockedSlots: [], slots: [] },
    { dayOfWeek: 0, shortName: 'Sun', fullName: 'Sunday', isActive: false, startTime: '10:00', endTime: '17:00', hasBreak: false, breakStartTime: '13:00', breakEndTime: '14:00', slotDurationMinutes: 30, calculatedSlotsCount: 0, blockedSlots: [], slots: [] },
  ];

  // ==========================================
  // SPECIAL DATES & HOLIDAYS STATE
  // ==========================================
  selectedDate = '';
  minDate = '';
  readonly isDateLoading = signal<boolean>(false);
  readonly isSavingDate = signal<boolean>(false);
  readonly specificDatesList = signal<ProviderAvailabilityDto[]>([]);

  dateSchedule = {
    id: 0,
    specificDate: '',
    dayName: '',
    isAvailable: true,
    isOverride: false,
    startTime: '10:00',
    endTime: '17:00',
    hasBreak: false,
    breakStartTime: '13:00',
    breakEndTime: '14:00',
    slotDurationMinutes: 30,
    blockedSlots: [] as string[],
    slots: [] as TimeSlotDto[],
    calculatedSlotsCount: 0
  };

  get currentProvider(): ProviderScheduleOverviewItemDto | null {
    if (!this.selectedProviderId) return null;
    return this.providers.find(p => p.providerId === this.selectedProviderId) || null;
  }

  get currentEditingDay(): DayConfig {
    return this.days[this.selectedDayIndex] || this.days[0];
  }

  get activeDaysCount(): number {
    return this.days.filter(d => d.isActive).length;
  }

  get totalWeeklyCalculatedSlots(): number {
    return this.days.filter(d => d.isActive).reduce((sum, d) => sum + d.calculatedSlotsCount, 0);
  }

  get currentPreset(): 'weekdays' | 'mon-sat' | 'all' | 'custom' {
    const active = this.days.filter(d => d.isActive).map(d => d.dayOfWeek);
    if (active.length === 7) return 'all';
    if (active.length === 6 && !active.includes(0)) return 'mon-sat';
    if (active.length === 5 && !active.includes(0) && !active.includes(6)) return 'weekdays';
    return 'custom';
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isOpen'] && this.isOpen) {
      this.initMinDate();
      this.isProviderMenuOpen = false;

      // Apply initialTab if provided
      if (this.initialTab) {
        this.activeTab = this.initialTab;
      } else {
        this.activeTab = 'weekly';
      }

      if (this.providerId) {
        this.selectedProviderId = this.providerId;
      } else if (this.providers && this.providers.length > 0) {
        this.selectedProviderId = this.providers[0].providerId;
      }

      this.syncCurrentProviderInfo();

      if (this.selectedProviderId) {
        this.loadSchedule(this.selectedProviderId);
        this.loadSpecificDatesList();

        if (this.initialDate) {
          this.selectedDate = this.initialDate;
        } else if (!this.selectedDate) {
          this.selectedDate = this.minDate;
        }

        if (this.activeTab === 'date-wise') {
          this.loadDateSchedule(this.selectedDate);
        }
      }
    }
  }

  private initMinDate(): void {
    const now = new Date();
    const yyyy = now.getFullYear();
    const mm = String(now.getMonth() + 1).padStart(2, '0');
    const dd = String(now.getDate()).padStart(2, '0');
    this.minDate = `${yyyy}-${mm}-${dd}`;
  }

  private syncCurrentProviderInfo(): void {
    if (!this.selectedProviderId) return;
    const match = this.providers.find(p => Number(p.providerId) === Number(this.selectedProviderId));
    if (match) {
      this.providerName = match.providerName;
      this.providerRole = match.specialization || 'Specialist';
    }
  }

  toggleProviderMenu(event: MouseEvent): void {
    event.stopPropagation();
    this.isProviderMenuOpen = !this.isProviderMenuOpen;
  }

  selectProvider(p: ProviderScheduleOverviewItemDto): void {
    if (this.selectedProviderId === p.providerId) {
      this.isProviderMenuOpen = false;
      return;
    }

    this.selectedProviderId = p.providerId;
    this.providerName = p.providerName;
    this.providerRole = p.specialization || 'Specialist';
    this.isProviderMenuOpen = false;

    // Reset initial overrides when switching provider inside modal
    this.initialDayIndex = null;
    this.initialDate = null;

    this.loadSchedule(p.providerId);
    this.loadSpecificDatesList();
    if (this.selectedDate) {
      this.loadDateSchedule(this.selectedDate);
    }
  }

  switchTab(tab: 'weekly' | 'date-wise'): void {
    this.activeTab = tab;
    this.openTimeDropdown = null;
    this.isProviderMenuOpen = false;
    this.errorMessage.set('');
    this.successMessage.set('');
    if (tab === 'date-wise') {
      this.loadSpecificDatesList();
      if (!this.selectedDate) {
        this.selectedDate = this.minDate;
      }
      this.loadDateSchedule(this.selectedDate);
    }
  }

  // ==========================================
  // WEEKLY SCHEDULE METHODS
  // ==========================================
  private loadSchedule(providerId: number): void {
    this.isLoading.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');

    this.availabilityService.getProviderWeeklyAvailability(providerId).subscribe({
      next: (schedules) => {
        this.dayRecordIds.clear();
        (schedules || []).forEach(s => {
          if (s.id > 0) {
            this.dayRecordIds.set(s.dayOfWeek, s.id);
          }
        });

        this.days.forEach(day => {
          const matched = (schedules || []).find(s => s.dayOfWeek === day.dayOfWeek);
          if (matched && matched.id > 0 && matched.isAvailable) {
            day.isActive = true;
            day.startTime = this.normalizeTime(matched.startTime) || '10:00';
            day.endTime = this.normalizeTime(matched.endTime) || '17:00';
            day.slotDurationMinutes = matched.slotDurationMinutes || 30;
            day.blockedSlots = matched.blockedSlotList || (matched.blockedSlots ? matched.blockedSlots.split(',').map(s => s.trim()) : []);

            if (matched.breakStartTime && matched.breakEndTime) {
              day.hasBreak = true;
              day.breakStartTime = this.normalizeTime(matched.breakStartTime);
              day.breakEndTime = this.normalizeTime(matched.breakEndTime);
            } else {
              day.hasBreak = false;
              day.breakStartTime = '13:00';
              day.breakEndTime = '14:00';
            }
          } else {
            day.isActive = false;
            day.blockedSlots = [];
            if (matched && matched.startTime && matched.endTime) {
              day.startTime = this.normalizeTime(matched.startTime) || '10:00';
              day.endTime = this.normalizeTime(matched.endTime) || '17:00';
            }
          }
        });

        // Set active editing day (respecting initialDayIndex if requested)
        if (this.initialDayIndex !== null && this.initialDayIndex !== undefined && this.initialDayIndex >= 0 && this.initialDayIndex < this.days.length) {
          this.selectedDayIndex = this.initialDayIndex;
        } else {
          const firstActiveIdx = this.days.findIndex(d => d.isActive);
          this.selectedDayIndex = firstActiveIdx >= 0 ? firstActiveIdx : 0;
        }

        // Recalculate slots for all active days
        this.days.forEach(d => {
          if (d.isActive) {
            this.recalculateSlotsForDay(d);
          } else {
            d.calculatedSlotsCount = 0;
            d.slots = [];
          }
        });

        this.isLoading.set(false);
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to load availability schedule.');
        this.isLoading.set(false);
      }
    });
  }

  selectEditingDay(index: number): void {
    this.selectedDayIndex = index;
    const current = this.currentEditingDay;
    if (current.isActive && current.slots.length === 0) {
      this.recalculateSlotsForDay(current);
    }
  }

  toggleDayActive(day: DayConfig, event?: MouseEvent): void {
    if (event) event.stopPropagation();
    day.isActive = !day.isActive;
    if (day.isActive) {
      this.recalculateSlotsForDay(day);
    } else {
      day.calculatedSlotsCount = 0;
      day.slots = [];
    }
  }

  setPreset(type: 'weekdays' | 'all' | 'mon-sat'): void {
    this.days.forEach(day => {
      if (type === 'weekdays') {
        day.isActive = day.dayOfWeek >= 1 && day.dayOfWeek <= 5;
      } else if (type === 'all') {
        day.isActive = true;
      } else if (type === 'mon-sat') {
        day.isActive = day.dayOfWeek !== 0;
      }
      if (day.isActive) {
        this.recalculateSlotsForDay(day);
      } else {
        day.calculatedSlotsCount = 0;
        day.slots = [];
      }
    });
  }

  applyCurrentDayHoursToAllActiveDays(): void {
    const src = this.currentEditingDay;
    this.days.forEach(d => {
      if (d.isActive && d !== src) {
        d.startTime = src.startTime;
        d.endTime = src.endTime;
        d.hasBreak = src.hasBreak;
        d.breakStartTime = src.breakStartTime;
        d.breakEndTime = src.breakEndTime;
        d.slotDurationMinutes = src.slotDurationMinutes;
        d.blockedSlots = [...src.blockedSlots];
        this.recalculateSlotsForDay(d);
      }
    });
    this.successMessage.set(`Applied ${src.fullName}'s working hours and settings to all active days.`);
    setTimeout(() => this.successMessage.set(''), 3000);
  }

  selectDuration(mins: number | string): void {
    const val = Number(mins) || 30;
    if (this.activeTab === 'date-wise') {
      this.dateSchedule.slotDurationMinutes = val;
      this.dateSchedule.blockedSlots = [];
      this.recalculateDateSlots();
      return;
    }
    this.currentEditingDay.slotDurationMinutes = val;
    this.currentEditingDay.blockedSlots = [];
    this.recalculateSlotsForDay(this.currentEditingDay);
  }

  toggleBreak(): void {
    if (this.activeTab === 'date-wise') {
      this.dateSchedule.hasBreak = !this.dateSchedule.hasBreak;
      this.recalculateDateSlots();
      return;
    }
    this.currentEditingDay.hasBreak = !this.currentEditingDay.hasBreak;
    this.recalculateSlotsForDay(this.currentEditingDay);
  }

  onTimingChange(): void {
    if (this.activeTab === 'date-wise') {
      this.recalculateDateSlots();
      return;
    }
    this.recalculateSlotsForDay(this.currentEditingDay);
  }

  recalculateSlotsForDay(day: DayConfig): void {
    if (!day.isActive || !day.startTime || !day.endTime || day.slotDurationMinutes <= 0) {
      day.calculatedSlotsCount = 0;
      day.slots = [];
      return;
    }

    const bStart = day.hasBreak ? day.breakStartTime : null;
    const bEnd = day.hasBreak ? day.breakEndTime : null;

    this.availabilityService.getPreviewSlots(
      day.startTime,
      day.endTime,
      day.slotDurationMinutes,
      bStart,
      bEnd,
      day.blockedSlots
    ).subscribe({
      next: (slots) => {
        day.slots = (slots || []).filter(s => !s.isBreak);
        day.calculatedSlotsCount = day.slots.filter(s => !s.isBlocked).length;
      },
      error: () => {
        day.slots = [];
        day.calculatedSlotsCount = 0;
      }
    });
  }

  // Click-to-Block Slot Toggle for Day
  toggleDaySlotBlocked(slot: TimeSlotDto, day: DayConfig): void {
    const time = slot.startTime;
    if (!day.blockedSlots) day.blockedSlots = [];
    const idx = day.blockedSlots.indexOf(time);
    if (idx >= 0) {
      day.blockedSlots.splice(idx, 1);
    } else {
      day.blockedSlots.push(time);
    }
    this.recalculateSlotsForDay(day);
  }

  blockAllDaySlots(day: DayConfig): void {
    day.blockedSlots = day.slots.map(s => s.startTime);
    this.recalculateSlotsForDay(day);
  }

  clearDayBlockedSlots(day: DayConfig): void {
    day.blockedSlots = [];
    this.recalculateSlotsForDay(day);
  }

  getDayBlockedCount(day: DayConfig): number {
    return day.slots.filter(s => s.isBlocked).length;
  }

  getDayAvailableCount(day: DayConfig): number {
    return day.slots.filter(s => !s.isBlocked).length;
  }

  onSave(): void {
    const effectiveId = this.selectedProviderId || this.providerId;
    if (!effectiveId) return;

    this.isSaving.set(true);
    this.errorMessage.set('');

    const activeDays = this.days.filter(d => d.isActive);
    const dto: SaveWeeklyAvailabilityRequestDto = {
      serviceProviderId: effectiveId,
      schedules: activeDays.map(d => ({
        dayOfWeek: d.dayOfWeek,
        startTime: d.startTime,
        endTime: d.endTime,
        breakStartTime: d.hasBreak ? d.breakStartTime : null,
        breakEndTime: d.hasBreak ? d.breakEndTime : null,
        slotDurationMinutes: Number(d.slotDurationMinutes),
        blockedSlots: d.blockedSlots || [],
        isAvailable: true
      }))
    };

    const inactiveDaysToDelete = this.days
      .filter(d => !d.isActive && this.dayRecordIds.has(d.dayOfWeek))
      .map(d => this.dayRecordIds.get(d.dayOfWeek)!);

    this.availabilityService.saveWeeklyAvailability(effectiveId, dto).subscribe({
      next: () => {
        if (inactiveDaysToDelete.length > 0) {
          inactiveDaysToDelete.forEach(id => {
            this.availabilityService.deleteAvailability(id).subscribe({
              next: () => { },
              error: () => { }
            });
          });
        }

        this.isSaving.set(false);
        this.saved.emit({ providerId: effectiveId, providerName: this.providerName });
        this.onClose();
      },
      error: (err) => {
        this.isSaving.set(false);
        this.errorMessage.set(err.error?.message || 'Failed to save availability settings.');
      }
    });
  }

  // ==========================================
  // SPECIAL DATES & HOLIDAYS METHODS
  // ==========================================
  loadSpecificDatesList(): void {
    const effectiveId = this.selectedProviderId || this.providerId;
    if (!effectiveId) return;

    this.availabilityService.getProviderSpecificDates(effectiveId).subscribe({
      next: (list) => {
        this.specificDatesList.set(list || []);
      },
      error: () => {
        this.specificDatesList.set([]);
      }
    });
  }

  onDatePicked(newDate: string): void {
    this.selectedDate = newDate;
    this.loadDateSchedule(newDate);
  }

  onEditOverride(item: ProviderAvailabilityDto): void {
    const dateStr = item.specificDate ? item.specificDate.split('T')[0] : '';
    if (dateStr) {
      this.onDatePicked(dateStr);
    }
  }

  getBlockedSlotsCount(item: ProviderAvailabilityDto): number {
    if (item.blockedSlotList && item.blockedSlotList.length > 0) return item.blockedSlotList.length;
    if (item.blockedSlots) return item.blockedSlots.split(',').filter(Boolean).length;
    return 0;
  }

  setQuickDate(offsetDays: number): void {
    const d = new Date();
    d.setDate(d.getDate() + offsetDays);
    const yyyy = d.getFullYear();
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    this.selectedDate = `${yyyy}-${mm}-${dd}`;
    this.loadDateSchedule(this.selectedDate);
  }

  loadDateSchedule(dateStr: string): void {
    const effectiveId = this.selectedProviderId || this.providerId;
    if (!effectiveId || !dateStr) return;

    this.isDateLoading.set(true);
    this.errorMessage.set('');

    this.availabilityService.getAvailabilityForDate(effectiveId, dateStr).subscribe({
      next: (res) => {
        this.dateSchedule.id = res.id || 0;
        this.dateSchedule.specificDate = dateStr;
        this.dateSchedule.dayName = res.dayName || '';
        this.dateSchedule.isAvailable = res.isAvailable !== false;
        this.dateSchedule.isOverride = !!(res.id && res.specificDate);
        this.dateSchedule.startTime = this.normalizeTime(res.startTime) || '10:00';
        this.dateSchedule.endTime = this.normalizeTime(res.endTime) || '17:00';
        this.dateSchedule.slotDurationMinutes = res.slotDurationMinutes || 30;

        if (res.breakStartTime && res.breakEndTime) {
          this.dateSchedule.hasBreak = true;
          this.dateSchedule.breakStartTime = this.normalizeTime(res.breakStartTime);
          this.dateSchedule.breakEndTime = this.normalizeTime(res.breakEndTime);
        } else {
          this.dateSchedule.hasBreak = false;
          this.dateSchedule.breakStartTime = '13:00';
          this.dateSchedule.breakEndTime = '14:00';
        }

        this.dateSchedule.blockedSlots = res.blockedSlotList || (res.blockedSlots ? res.blockedSlots.split(',').map(s => s.trim()) : []);
        this.recalculateDateSlots();
        this.isDateLoading.set(false);
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to load date schedule.');
        this.isDateLoading.set(false);
      }
    });
  }

  toggleDateAvailable(): void {
    this.dateSchedule.isAvailable = !this.dateSchedule.isAvailable;
    if (this.dateSchedule.isAvailable) {
      this.recalculateDateSlots();
    } else {
      this.dateSchedule.slots = [];
      this.dateSchedule.calculatedSlotsCount = 0;
    }
  }

  recalculateDateSlots(): void {
    if (!this.dateSchedule.isAvailable || !this.dateSchedule.startTime || !this.dateSchedule.endTime) {
      this.dateSchedule.slots = [];
      this.dateSchedule.calculatedSlotsCount = 0;
      return;
    }

    const bStart = this.dateSchedule.hasBreak ? this.dateSchedule.breakStartTime : null;
    const bEnd = this.dateSchedule.hasBreak ? this.dateSchedule.breakEndTime : null;

    this.availabilityService.getPreviewSlots(
      this.dateSchedule.startTime,
      this.dateSchedule.endTime,
      this.dateSchedule.slotDurationMinutes,
      bStart,
      bEnd,
      this.dateSchedule.blockedSlots
    ).subscribe({
      next: (slots) => {
        this.dateSchedule.slots = (slots || []).filter(s => !s.isBreak);
        this.dateSchedule.calculatedSlotsCount = this.dateSchedule.slots.filter(s => !s.isBlocked).length;
      },
      error: () => {
        this.dateSchedule.slots = [];
        this.dateSchedule.calculatedSlotsCount = 0;
      }
    });
  }

  toggleDateSlotBlocked(slot: TimeSlotDto): void {
    const time = slot.startTime;
    if (!this.dateSchedule.blockedSlots) this.dateSchedule.blockedSlots = [];
    const idx = this.dateSchedule.blockedSlots.indexOf(time);
    if (idx >= 0) {
      this.dateSchedule.blockedSlots.splice(idx, 1);
    } else {
      this.dateSchedule.blockedSlots.push(time);
    }
    this.recalculateDateSlots();
  }

  blockAllDateSlots(): void {
    this.dateSchedule.blockedSlots = this.dateSchedule.slots.map(s => s.startTime);
    this.recalculateDateSlots();
  }

  clearDateBlockedSlots(): void {
    this.dateSchedule.blockedSlots = [];
    this.recalculateDateSlots();
  }

  getDateBlockedCount(): number {
    return this.dateSchedule.slots.filter(s => s.isBlocked).length;
  }

  getDateAvailableCount(): number {
    return this.dateSchedule.slots.filter(s => !s.isBlocked).length;
  }

  saveDateSchedule(): void {
    const effectiveId = this.selectedProviderId || this.providerId;
    if (!effectiveId || !this.selectedDate) return;

    this.isSavingDate.set(true);
    this.errorMessage.set('');

    const req: SaveDateSpecificAvailabilityRequestDto = {
      serviceProviderId: effectiveId,
      date: this.selectedDate,
      startTime: this.dateSchedule.startTime,
      endTime: this.dateSchedule.endTime,
      breakStartTime: this.dateSchedule.hasBreak ? this.dateSchedule.breakStartTime : null,
      breakEndTime: this.dateSchedule.hasBreak ? this.dateSchedule.breakEndTime : null,
      slotDurationMinutes: Number(this.dateSchedule.slotDurationMinutes),
      blockedSlots: this.dateSchedule.blockedSlots,
      isAvailable: this.dateSchedule.isAvailable
    };

    this.availabilityService.saveDateSpecificAvailability(effectiveId, req).subscribe({
      next: () => {
        this.isSavingDate.set(false);
        this.successMessage.set(`Schedule for ${this.formatDisplayDate(this.selectedDate)} saved successfully!`);
        this.loadSpecificDatesList();
        this.loadDateSchedule(this.selectedDate);
        this.saved.emit({ providerId: effectiveId, providerName: this.providerName });
        setTimeout(() => this.successMessage.set(''), 3500);
      },
      error: (err) => {
        this.isSavingDate.set(false);
        this.errorMessage.set(err.error?.message || 'Failed to save date schedule.');
      }
    });
  }

  async deleteDateOverride(id: number, dateStr?: string | null): Promise<void> {
    const formatted = dateStr ? this.formatDisplayDate(dateStr) : 'this date';
    const confirmed = await this.confirmService.confirm({
      title: 'Reset Date Schedule',
      message: `Are you sure you want to delete special schedule for ${formatted}? The provider will revert to the standard weekly schedule.`,
      confirmText: 'Reset Schedule',
      variant: 'warning'
    });

    if (!confirmed) {
      return;
    }

    const effectiveId = this.selectedProviderId || this.providerId;
    this.availabilityService.deleteAvailability(id).subscribe({
      next: () => {
        this.successMessage.set(`Schedule for ${formatted} reset to weekly default.`);
        this.loadSpecificDatesList();
        if (this.selectedDate) {
          this.loadDateSchedule(this.selectedDate);
        }
        if (effectiveId) {
          this.saved.emit({ providerId: effectiveId, providerName: this.providerName });
        }
        setTimeout(() => this.successMessage.set(''), 3500);
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to delete date override.');
      }
    });
  }

  // ==========================================
  // SHARED TIME PICKER HELPERS
  // ==========================================
  normalizeTime(timeStr?: string | null): string {
    if (!timeStr) return '';
    const parts = timeStr.split(':');
    if (parts.length >= 2) {
      return `${parts[0].padStart(2, '0')}:${parts[1].padStart(2, '0')}`;
    }
    return timeStr;
  }

  getHour24(timeStr?: string | null): number {
    if (!timeStr) return 10;
    const [h] = timeStr.split(':');
    return parseInt(h, 10) || 0;
  }

  getMinuteStr(timeStr?: string | null): string {
    if (!timeStr) return '00';
    const parts = timeStr.split(':');
    return (parts[1] || '00').padStart(2, '0');
  }

  getPeriodFromTime(timeStr?: string | null): 'AM' | 'PM' {
    return this.getHour24(timeStr) >= 12 ? 'PM' : 'AM';
  }

  getTimeVal(field: 'startTime' | 'endTime' | 'breakStart' | 'breakEnd'): string {
    if (this.activeTab === 'date-wise') {
      if (field === 'startTime') return this.dateSchedule.startTime;
      if (field === 'endTime') return this.dateSchedule.endTime;
      if (field === 'breakStart') return this.dateSchedule.breakStartTime;
      return this.dateSchedule.breakEndTime;
    }
    const cur = this.currentEditingDay;
    if (field === 'startTime') return cur.startTime;
    if (field === 'endTime') return cur.endTime;
    if (field === 'breakStart') return cur.breakStartTime;
    return cur.breakEndTime;
  }

  setTimeVal(field: 'startTime' | 'endTime' | 'breakStart' | 'breakEnd', val: string): void {
    if (this.activeTab === 'date-wise') {
      if (field === 'startTime') this.dateSchedule.startTime = val;
      else if (field === 'endTime') this.dateSchedule.endTime = val;
      else if (field === 'breakStart') this.dateSchedule.breakStartTime = val;
      else if (field === 'breakEnd') this.dateSchedule.breakEndTime = val;
      this.recalculateDateSlots();
      return;
    }
    const cur = this.currentEditingDay;
    if (field === 'startTime') cur.startTime = val;
    else if (field === 'endTime') cur.endTime = val;
    else if (field === 'breakStart') cur.breakStartTime = val;
    else if (field === 'breakEnd') cur.breakEndTime = val;
    this.onTimingChange();
  }

  selectHour(field: 'startTime' | 'endTime' | 'breakStart' | 'breakEnd', hour24: number): void {
    const min = this.getMinuteStr(this.getTimeVal(field));
    const newTime = `${hour24.toString().padStart(2, '0')}:${min}`;
    this.setTimeVal(field, newTime);
  }

  selectMinute(field: 'startTime' | 'endTime' | 'breakStart' | 'breakEnd', minStr: string): void {
    const h = this.getHour24(this.getTimeVal(field));
    const newTime = `${h.toString().padStart(2, '0')}:${minStr}`;
    this.setTimeVal(field, newTime);
  }

  setPeriod(field: 'startTime' | 'endTime' | 'breakStart' | 'breakEnd', targetPeriod: 'AM' | 'PM'): void {
    let h = this.getHour24(this.getTimeVal(field));
    const currentPeriod = h >= 12 ? 'PM' : 'AM';
    if (currentPeriod === targetPeriod) return;

    if (targetPeriod === 'AM') {
      h = h >= 12 ? h - 12 : h;
    } else {
      h = h < 12 ? h + 12 : h;
    }
    const min = this.getMinuteStr(this.getTimeVal(field));
    this.setTimeVal(field, `${h.toString().padStart(2, '0')}:${min}`);
  }

  toggleTimeDropdown(field: 'startTime' | 'endTime' | 'breakStart' | 'breakEnd', event: MouseEvent): void {
    event.stopPropagation();
    if (this.openTimeDropdown === field) {
      this.openTimeDropdown = null;
      return;
    }
    this.openTimeDropdown = field;
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.openTimeDropdown) {
      this.openTimeDropdown = null;
    }
    if (this.isProviderMenuOpen) {
      this.isProviderMenuOpen = false;
    }
  }

  @HostListener('document:keydown.escape')
  onEscapeKey(): void {
    this.openTimeDropdown = null;
    this.isProviderMenuOpen = false;
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('modal-backdrop')) {
      this.onClose();
    }
  }

  onClose(): void {
    this.openTimeDropdown = null;
    this.isProviderMenuOpen = false;
    this.close.emit();
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

  formatDisplayDate(dateStr?: string | null): string {
    if (!dateStr) return '';
    const cleanStr = dateStr.includes('T') ? dateStr.split('T')[0] : dateStr;
    const parts = cleanStr.split('-');
    if (parts.length === 3) {
      const y = parseInt(parts[0], 10);
      const m = parseInt(parts[1], 10) - 1;
      const d = parseInt(parts[2], 10);
      const dateObj = new Date(y, m, d);
      return dateObj.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' });
    }
    return cleanStr;
  }

  calculateMinutesDiff(startTime?: string | null, endTime?: string | null): number {
    if (!startTime || !endTime) return 0;
    const [startH, startM] = startTime.split(':').map(Number);
    const [endH, endM] = endTime.split(':').map(Number);
    return (endH * 60 + (endM || 0)) - (startH * 60 + (startM || 0));
  }

  formatDurationText(totalMins: number): string {
    if (totalMins <= 0) return '';
    const hours = Math.floor(totalMins / 60);
    const mins = totalMins % 60;
    if (mins === 0) return `${hours} hrs`;
    return `${hours}h ${mins}m`;
  }
}
