import { Component, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AppointmentService } from '../../../core/services/appointment.service';
import { AuthService } from '../../../core/services/auth.service';
import {
  AppointmentDto,
  AppointmentStatus,
  AvailableSlotDto,
  RescheduleAppointmentRequestDto,
  UpdateAppointmentStatusDto,
  ProviderDailyAvailabilityResponseDto
} from '../../../core/models/appointment.models';
import { UserRole } from '../../../core/models/auth.models';
import { ConfirmService } from '../../../shared/components/confirmation-modal/confirm.service';
import { canRescheduleAppointment } from '../../../core/constants/app.constants';
import { formatTo12Hour } from '../../../shared/utils/formatters';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { provideNativeDateAdapter } from '@angular/material/core';

export interface CalendarDay {
  date: Date;
  dateStr: string;
  dayNumber: number;
  isCurrentMonth: boolean;
  isPast: boolean;
  isToday: boolean;
  isSelected: boolean;
}

@Component({
  selector: 'app-reschedule-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDatepickerModule],
  providers: [provideNativeDateAdapter()],
  templateUrl: './reschedule-modal.component.html',
  styleUrls: ['./reschedule-modal.component.css']
})
export class RescheduleModalComponent implements OnInit {
  private readonly appointmentService = inject(AppointmentService);
  private readonly authService = inject(AuthService);
  private readonly confirmService = inject(ConfirmService);

  @Input({ required: true }) appointment!: AppointmentDto;
  @Output() close = new EventEmitter<void>();
  @Output() rescheduled = new EventEmitter<AppointmentDto>();

  get isServiceProvider(): boolean {
    return this.authService.currentUser()?.role === UserRole.ServiceProvider;
  }

  get isStaff(): boolean {
    const role = this.authService.currentUser()?.role;
    return role === UserRole.Admin || role === UserRole.SuperAdmin || role === UserRole.ServiceProvider;
  }

  readonly isLoadingSlots = signal<boolean>(false);
  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string>('');

  newDate = '';
  minDate = '';
  maxDate = '';
  availableSlots: AvailableSlotDto[] = [];
  selectedSlot: AvailableSlotDto | null = null;
  rescheduleReason = '';
  isWorkingDay = true;

  // Custom Dark Date Picker State
  isDatePickerOpen = false;
  currentMonthDate = new Date();
  weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

  get availableCount(): number {
    return this.availableSlots.filter(s => s.isAvailable).length;
  }

  get currentMonthLabel(): string {
    return this.currentMonthDate.toLocaleDateString('en-US', { month: 'long', year: 'numeric' });
  }

  get isPrevMonthDisabled(): boolean {
    const today = new Date();
    return this.currentMonthDate.getFullYear() === today.getFullYear() &&
           this.currentMonthDate.getMonth() <= today.getMonth();
  }

  // Multi-service metadata helpers
  get serviceNames(): string[] {
    if (this.appointment?.serviceNames && this.appointment.serviceNames.length > 0) {
      return this.appointment.serviceNames;
    }
    if (this.appointment?.items && this.appointment.items.length > 0) {
      return this.appointment.items.map(i => i.serviceName);
    }
    if (this.appointment?.serviceName) {
      return [this.appointment.serviceName];
    }
    return [];
  }

  get serviceNamesSummary(): string {
    const names = this.serviceNames;
    return names.length > 0 ? names.join(' + ') : 'Assigned Services';
  }

  get totalDurationMinutes(): number {
    if (this.appointment?.totalDurationMinutes && this.appointment.totalDurationMinutes > 0) {
      return this.appointment.totalDurationMinutes;
    }
    if (this.appointment?.items && this.appointment.items.length > 0) {
      return this.appointment.items.reduce((sum, item) => sum + (item.durationMinutes || 30), 0);
    }
    return this.appointment?.serviceDurationMinutes || 30;
  }

  get totalAmount(): number {
    if (this.appointment?.totalAmount && this.appointment.totalAmount > 0) {
      return this.appointment.totalAmount;
    }
    if (this.appointment?.items && this.appointment.items.length > 0) {
      return this.appointment.items.reduce((sum, item) => sum + (item.price || 0), 0);
    }
    return this.appointment?.servicePrice || 0;
  }

  get targetServiceIds(): number[] {
    if (this.appointment?.items && this.appointment.items.length > 0) {
      return this.appointment.items.map(i => i.serviceId).filter(id => id > 0);
    }
    if (this.appointment?.serviceId && this.appointment.serviceId > 0) {
      return [this.appointment.serviceId];
    }
    return [];
  }

  formatTime(timeStr: string): string {
    return formatTo12Hour(timeStr);
  }

  formatSlotRange(slot: AvailableSlotDto): string {
    if (!slot) return '';
    const text = slot.displayText || (slot.startTime && slot.endTime
      ? `${this.formatTime(slot.startTime)} - ${this.formatTime(slot.endTime)}`
      : this.formatTime(slot.startTime));
    return text.replace(/\s*[–—]\s*/g, ' - ');
  }

  prevMonth(event?: Event): void {
    if (event) event.stopPropagation();
    if (this.isPrevMonthDisabled) return;
    this.currentMonthDate = new Date(this.currentMonthDate.getFullYear(), this.currentMonthDate.getMonth() - 1, 1);
  }

  nextMonth(event?: Event): void {
    if (event) event.stopPropagation();
    this.currentMonthDate = new Date(this.currentMonthDate.getFullYear(), this.currentMonthDate.getMonth() + 1, 1);
  }

  get calendarDays(): CalendarDay[] {
    const year = this.currentMonthDate.getFullYear();
    const month = this.currentMonthDate.getMonth();
    const firstDayIndex = (new Date(year, month, 1).getDay() + 6) % 7;
    const daysInMonth = new Date(year, month + 1, 0).getDate();
    const daysInPrevMonth = new Date(year, month, 0).getDate();

    const days: CalendarDay[] = [];

    // Leading days from previous month
    for (let i = firstDayIndex - 1; i >= 0; i--) {
      const dayNum = daysInPrevMonth - i;
      const prevDate = new Date(year, month - 1, dayNum);
      days.push({
        date: prevDate,
        dateStr: this.formatDateToYMD(prevDate),
        dayNumber: dayNum,
        isCurrentMonth: false,
        isPast: true,
        isToday: false,
        isSelected: false
      });
    }

    // Days in current month
    for (let dayNum = 1; dayNum <= daysInMonth; dayNum++) {
      const d = new Date(year, month, dayNum);
      const dateStr = this.formatDateToYMD(d);
      const isPast = dateStr < this.minDate;
      const isToday = dateStr === this.minDate;
      const isSelected = dateStr === this.newDate;

      days.push({
        date: d,
        dateStr,
        dayNumber: dayNum,
        isCurrentMonth: true,
        isPast,
        isToday,
        isSelected
      });
    }

    // Trailing days
    const remaining = (7 - (days.length % 7)) % 7;
    for (let dayNum = 1; dayNum <= remaining; dayNum++) {
      const nextDate = new Date(year, month + 1, dayNum);
      days.push({
        date: nextDate,
        dateStr: this.formatDateToYMD(nextDate),
        dayNumber: dayNum,
        isCurrentMonth: false,
        isPast: false,
        isToday: false,
        isSelected: false
      });
    }

    return days;
  }

  // --- Material Calendar Integrations ---
  get minDateObject(): Date {
    const d = new Date();
    d.setHours(0, 0, 0, 0);
    return d;
  }

  get maxDateObject(): Date {
    const d = new Date();
    d.setDate(d.getDate() + 15);
    d.setHours(23, 59, 59, 999);
    return d;
  }

  get selectedDateObject(): Date {
    if (!this.newDate) return new Date();
    const parts = this.newDate.split('-').map(Number);
    if (parts.length === 3) {
      return new Date(parts[0], parts[1] - 1, parts[2], 12, 0, 0);
    }
    return new Date();
  }

  onMaterialDateSelected(date: Date | null): void {
    if (!date) return;
    const dateStr = this.formatDateToYMD(date);
    const maxDateStr = this.formatDateToYMD(this.maxDateObject);
    if (dateStr < this.minDate || dateStr > maxDateStr) return;
    this.newDate = dateStr;
    this.isDatePickerOpen = false;
    this.onDateChange();
  }

  toggleDatePicker(event?: Event): void {
    if (event) event.stopPropagation();
    this.isDatePickerOpen = !this.isDatePickerOpen;
    if (this.isDatePickerOpen && this.newDate) {
      const [y, m] = this.newDate.split('-').map(Number);
      this.currentMonthDate = new Date(y, m - 1, 1);
    }
  }

  selectCalendarDay(day: CalendarDay): void {
    if (day.isPast || !day.isCurrentMonth) return;
    this.newDate = day.dateStr;
    this.isDatePickerOpen = false;
    this.onDateChange();
  }

  selectToday(event?: Event): void {
    if (event) event.stopPropagation();
    this.newDate = this.minDate;
    const [y, m] = this.minDate.split('-').map(Number);
    this.currentMonthDate = new Date(y, m - 1, 1);
    this.isDatePickerOpen = false;
    this.onDateChange();
  }

  closeDatePicker(event?: Event): void {
    if (event) event.stopPropagation();
    this.isDatePickerOpen = false;
  }

  get currentFormattedDate(): string {
    if (!this.appointment?.appointmentDate) return '';
    return this.formatDisplayDate(this.appointment.appointmentDate);
  }

  get currentSlotDisplay(): string {
    if (!this.appointment) return '';
    if (this.appointment.startTime && this.appointment.endTime) {
      return `${this.formatTime(this.appointment.startTime)} – ${this.formatTime(this.appointment.endTime)}`;
    }
    if (this.appointment.timeSlotText) {
      const parts = this.appointment.timeSlotText.split(/[-–—]/).map(s => s.trim());
      if (parts.length === 2) {
        return `${this.formatTime(parts[0])} – ${this.formatTime(parts[1])}`;
      }
      return this.formatTime(this.appointment.timeSlotText);
    }
    return '';
  }

  formatDisplayDate(dateInput: string | Date | undefined): string {
    if (!dateInput) return '';
    try {
      if (dateInput instanceof Date) {
        return dateInput.toLocaleDateString('en-GB', { weekday: 'short', day: '2-digit', month: 'short', year: 'numeric' });
      }
      const clean = dateInput.includes('T') ? dateInput.split('T')[0] : dateInput;
      const parts = clean.split('-').map(Number);
      if (parts.length === 3 && !isNaN(parts[0]) && !isNaN(parts[1]) && !isNaN(parts[2])) {
        const date = new Date(parts[0], parts[1] - 1, parts[2]);
        return date.toLocaleDateString('en-GB', { weekday: 'short', day: '2-digit', month: 'short', year: 'numeric' });
      }
      const d = new Date(dateInput);
      if (!isNaN(d.getTime())) {
        return d.toLocaleDateString('en-GB', { weekday: 'short', day: '2-digit', month: 'short', year: 'numeric' });
      }
    } catch {
      // fallback
    }
    return String(dateInput);
  }

  ngOnInit(): void {
    const today = new Date();
    this.minDate = this.formatDateToYMD(today);
    const maxDateObj = new Date(today);
    maxDateObj.setDate(maxDateObj.getDate() + 15);
    this.maxDate = this.formatDateToYMD(maxDateObj);
    this.newDate = this.minDate;
    this.currentMonthDate = new Date(today.getFullYear(), today.getMonth(), 1);

    // Safeguard: Fetch complete appointment items and details if missing
    if ((!this.appointment?.items || this.appointment.items.length === 0) && this.appointment?.id) {
      this.appointmentService.getAppointmentById(this.appointment.id).subscribe({
        next: (fullApt) => {
          if (fullApt) {
            this.appointment = fullApt;
          }
          this.fetchSlots();
        },
        error: () => this.fetchSlots()
      });
    } else {
      this.fetchSlots();
    }
  }

  private formatDateToYMD(date: Date): string {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  onDateChange(): void {
    this.selectedSlot = null;
    this.fetchSlots();
  }

  fetchSlots(): void {
    if (!this.newDate) return;

    this.isLoadingSlots.set(true);
    this.errorMessage.set('');

    const sIds = this.targetServiceIds;
    const primaryServiceId = sIds.length > 0 ? sIds[0] : (this.appointment?.serviceId || 0);

    const slots$ = this.appointment?.serviceProviderId
      ? this.appointmentService.getAvailableSlots(this.appointment.serviceProviderId, this.newDate, sIds.length > 0 ? sIds : undefined)
      : this.appointmentService.getAvailableSlotsByService(primaryServiceId, this.newDate, this.appointment?.tenantId, sIds.length > 0 ? sIds : undefined);

    slots$.subscribe({
      next: (res: ProviderDailyAvailabilityResponseDto) => {
        if (!res) {
          this.isWorkingDay = false;
          this.availableSlots = [];
          this.isLoadingSlots.set(false);
          return;
        }
        this.isWorkingDay = res.isWorkingDay;

        // Current time threshold to prevent past slots today
        const today = new Date();
        const todayStr = this.formatDateToYMD(today);
        const currentHours = today.getHours().toString().padStart(2, '0');
        const currentMinutes = today.getMinutes().toString().padStart(2, '0');
        const currentTimeStr = `${currentHours}:${currentMinutes}`;

        let filtered = (res.slots || []).filter(s => s.isAvailable && s.status !== 'Booked' && s.status !== 'Break');
        if (this.newDate === todayStr) {
          filtered = filtered.filter(s => s.startTime > currentTimeStr);
        }

        this.availableSlots = filtered;
        this.isLoadingSlots.set(false);
      },
      error: (err) => {
        this.isLoadingSlots.set(false);
        this.errorMessage.set(err.error?.message || 'Failed to fetch available time slots.');
      }
    });
  }

  selectSlot(slot: AvailableSlotDto): void {
    if (!slot.isAvailable) return;
    this.selectedSlot = slot;
  }

  confirmReschedule(): void {
    if (!this.isStaff && !canRescheduleAppointment(this.appointment)) {
      this.errorMessage.set('Appointments can only be rescheduled at least 1 hour prior to the scheduled start time.');
      return;
    }

    if (!this.selectedSlot) {
      this.errorMessage.set('Please select an available time slot.');
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set('');

    // FLOW A: Service Provider proposes a new slot -> Customer receives Accept / Decline decision
    if (this.isServiceProvider) {
      const updateReq: UpdateAppointmentStatusDto = {
        status: AppointmentStatus.RescheduleProposed,
        proposedDate: this.newDate,
        proposedStartTime: this.selectedSlot.startTime,
        proposedEndTime: this.selectedSlot.endTime,
        notes: this.rescheduleReason?.trim() || 'Specialist proposed alternative schedule'
      };

      this.appointmentService.updateStatus(this.appointment.id, updateReq).subscribe({
        next: (res: AppointmentDto) => {
          this.isSubmitting.set(false);
          this.confirmService.alert({
            title: 'Proposal Sent',
            message: `Reschedule proposal for slot ${this.formatDisplayDate(this.newDate)} (${this.formatSlotRange(this.selectedSlot!)}) has been sent to ${this.appointment.customerName}. They can accept to confirm or decline to cancel.`,
            variant: 'primary',
            buttonText: 'Done'
          });
          this.rescheduled.emit(res);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          const errorMsg = err.error?.message || 'Failed to send reschedule proposal to customer.';
          this.errorMessage.set(errorMsg);
          this.confirmService.alert({
            title: 'Proposal Failed',
            message: errorMsg,
            variant: 'danger',
            buttonText: 'OK'
          });
        }
      });
      return;
    }

    // FLOW B: Customer (or Admin) directly rescheduling
    const req: RescheduleAppointmentRequestDto = {
      newDate: this.newDate,
      newStartTime: this.selectedSlot.startTime,
      newEndTime: this.selectedSlot.endTime,
      reason: this.rescheduleReason?.trim() || 'Schedule adjustment by staff',
      customerContact: this.appointment?.customerPhone || this.appointment?.customerEmail
    };

    this.appointmentService.rescheduleAppointment(this.appointment.id, req).subscribe({
      next: (res: AppointmentDto) => {
        this.isSubmitting.set(false);
        this.rescheduled.emit(res);
      },
      error: (err) => {
        this.isSubmitting.set(false);
        const errorMsg = err.error?.message || (err.status === 409
          ? 'The chosen time slot was just booked by another customer. Please choose a different slot.'
          : 'Failed to reschedule appointment.');
        this.errorMessage.set(errorMsg);

        if (err.status === 409) {
          this.fetchSlots();
          this.confirmService.alert({
            title: 'Slot Unavailable',
            message: errorMsg,
            variant: 'warning',
            buttonText: 'Choose Another Slot'
          });
        } else {
          this.confirmService.alert({
            title: 'Reschedule Notice',
            message: errorMsg,
            variant: 'danger',
            buttonText: 'OK'
          });
        }
      }
    });
  }

  onClose(): void {
    this.close.emit();
  }
}
