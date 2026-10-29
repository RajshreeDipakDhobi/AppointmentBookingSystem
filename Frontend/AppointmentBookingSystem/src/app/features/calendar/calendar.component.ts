import { Component, OnInit, OnDestroy, inject, signal, computed, Output, EventEmitter, ViewChild, ElementRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { FullCalendarModule, FullCalendarComponent } from '@fullcalendar/angular';
import { CalendarOptions, EventInput, EventClickArg, DatesSetArg } from '@fullcalendar/core';
import dayGridPlugin from '@fullcalendar/daygrid';
import timeGridPlugin from '@fullcalendar/timegrid';
import interactionPlugin, { DateClickArg } from '@fullcalendar/interaction';

import { AppointmentService, AppointmentFilterParams } from '../../core/services/appointment.service';
import { ProviderService } from '../../core/services/provider.service';
import { AvailabilityService } from '../../core/services/availability.service';
import { AuthService } from '../../core/services/auth.service';
import { ConfirmService } from '../../shared/components/confirmation-modal/confirm.service';

import {
  AppointmentDto,
  AppointmentStatus
} from '../../core/models/appointment.models';
import { ServiceProviderDto, ProviderStatus } from '../../core/models/provider.models';
import { ProviderScheduleOverviewItemDto, DayScheduleSummaryDto } from '../../core/models/availability.models';

import { RescheduleModalComponent } from '../appointments/reschedule-modal/reschedule-modal.component';
import { CancelModalComponent } from '../appointments/cancel-modal/cancel-modal.component';
import { BookingModalComponent } from '../appointments/booking-modal/booking-modal.component';

import { AppDatePipe, AppTimePipe, AppPhonePipe } from '../../shared/pipes';
import { getAvatarGradient, getInitials, isAppointmentPast, canModifyAppointment, canCancelAppointment, APPOINTMENT_STATUS_MAP } from '../../core/constants/app.constants';
import { MatDatepickerModule, MatDatepickerInputEvent } from '@angular/material/datepicker';
import { provideNativeDateAdapter } from '@angular/material/core';

export interface ProviderTheme {
  name: string;
  dot: string;
  cardBg: string;
  cardBorder: string;
  textColor: string;
  serviceColor: string;
  timeTagBg: string;
  timeTagText: string;
}

export interface ScheduleSlot {
  raw: string;          // "08:00", "08:30", etc.
  rawEnd: string;       // "08:30", "09:00", etc.
  display: string;      // "08:00 AM", "08:30 AM", etc.
  displayEnd?: string;  // "08:30 AM", "09:00 AM", etc.
  rangeText: string;    // "08:00 – 08:30 AM"
  isHourMark: boolean;  // true on the hour (e.g. 08:00, 09:00)
  isLunchBreak: boolean; // dynamically evaluated per provider in template
}

export interface PositionedAppointment {
  appointment: AppointmentDto;
  top: number;
  height: number;
  leftPercent: number;  // 0, 50, etc. for sub-column layout
  widthPercent: number; // 100, 48.5, etc.
  zIndex: number;
  theme: ProviderTheme;
}

@Component({
  selector: 'app-calendar',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    FullCalendarModule,
    MatDatepickerModule,
    RescheduleModalComponent,
    CancelModalComponent,
    BookingModalComponent,
    AppDatePipe,
    AppTimePipe,
    AppPhonePipe
  ],
  providers: [
    provideNativeDateAdapter()
  ],
  templateUrl: './calendar.component.html',
  styleUrls: ['./calendar.component.css']
})
export class CalendarComponent implements OnInit, OnDestroy {
  @ViewChild('fullCalendar') fullCalendarComponent?: FullCalendarComponent;
  @ViewChild('gridScrollArea') gridScrollArea?: ElementRef<HTMLElement>;
  @ViewChild('providerHeadersContainer') providerHeadersContainer?: ElementRef<HTMLElement>;
  @Output() appointmentChanged = new EventEmitter<void>();
  @Output() openBooking = new EventEmitter<void>();

  private readonly appointmentService = inject(AppointmentService);
  private readonly providerService = inject(ProviderService);
  private readonly availabilityService = inject(AvailabilityService);
  private readonly authService = inject(AuthService);
  private readonly confirmService = inject(ConfirmService);

  readonly getAvatarGradient = getAvatarGradient;
  readonly getInitials = getInitials;
  readonly isAppointmentPast = isAppointmentPast;
  readonly canModify = canModifyAppointment;
  readonly canCancel = canCancelAppointment;
  readonly AppointmentStatus = AppointmentStatus;

  // View Mode: 'day' (Resource Grid) | 'week' | 'month' (FullCalendar)
  readonly activeView = signal<'day' | 'week' | 'month'>('day');

  // Indian Standard Time (Asia/Kolkata) Helpers
  getIndianToday(): string {
    const formatter = new Intl.DateTimeFormat('en-CA', {
      timeZone: 'Asia/Kolkata',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit'
    });
    return formatter.format(new Date());
  }

  getIndianCurrentMinutes(): number {
    const parts = new Intl.DateTimeFormat('en-US', {
      timeZone: 'Asia/Kolkata',
      hour: 'numeric',
      minute: 'numeric',
      hour12: false
    }).formatToParts(new Date());

    const hour = parseInt(parts.find(p => p.type === 'hour')?.value || '0', 10);
    const min = parseInt(parts.find(p => p.type === 'minute')?.value || '0', 10);
    return hour * 60 + min;
  }

  // Selected Date (defaults to Indian today)
  readonly selectedDate = signal<string>(this.getIndianToday());

  // Date Object bound to Angular Material Datepicker
  readonly selectedDateObject = computed<Date>(() => {
    const parts = this.selectedDate().split('-').map(Number);
    if (parts.length === 3) {
      return new Date(parts[0], parts[1] - 1, parts[2]);
    }
    return new Date();
  });

  onMaterialDateChange(event: MatDatepickerInputEvent<Date>): void {
    if (event.value) {
      const dateStr = this.formatDateToString(event.value);
      this.selectedDate.set(dateStr);
      this.fetchAppointments();

      if (this.fullCalendarComponent) {
        this.fullCalendarComponent.getApi().gotoDate(dateStr);
      }
    }
  }

  // Formatted button date for pill trigger: e.g. "Oct 27, 2026" matching reference screenshot
  readonly formattedButtonDate = computed(() => {
    const parts = this.selectedDate().split('-').map(Number);
    if (parts.length !== 3) return '';
    const d = new Date(parts[0], parts[1] - 1, parts[2]);
    const month = d.toLocaleDateString('en-US', { month: 'short' });
    const day = d.getDate();
    const year = d.getFullYear();
    return `${month} ${day}, ${year}`;
  });

  // Short Date Display fallback
  readonly shortDateDisplay = computed(() => {
    const parts = this.selectedDate().split('-').map(Number);
    if (parts.length !== 3) return '';
    const d = new Date(parts[0], parts[1] - 1, parts[2]);
    const day = d.getDate();
    const month = d.toLocaleDateString('en-US', { month: 'short' }).toUpperCase();
    return `${day} ${month}`;
  });

  // Header Center Date: e.g. "Tuesday, Oct 27, 2026" or "Oct 25 – 31, 2026"
  readonly headerFullDate = computed(() => {
    const parts = this.selectedDate().split('-').map(Number);
    if (parts.length !== 3) return '';
    const d = new Date(parts[0], parts[1] - 1, parts[2]);

    if (this.activeView() === 'week') {
      const dayOfWeek = d.getDay(); // 0 is Sunday
      const startOfWeek = new Date(d);
      startOfWeek.setDate(d.getDate() - dayOfWeek);
      const endOfWeek = new Date(startOfWeek);
      endOfWeek.setDate(startOfWeek.getDate() + 6);

      const startMonth = startOfWeek.toLocaleDateString('en-US', { month: 'short' });
      const endMonth = endOfWeek.toLocaleDateString('en-US', { month: 'short' });
      const startDay = startOfWeek.getDate();
      const endDay = endOfWeek.getDate();
      const year = endOfWeek.getFullYear();

      if (startMonth === endMonth) {
        return `${startMonth} ${startDay} – ${endDay}, ${year}`;
      } else {
        return `${startMonth} ${startDay} – ${endMonth} ${endDay}, ${year}`;
      }
    }

    if (this.activeView() === 'month') {
      return d.toLocaleDateString('en-US', { month: 'long', year: 'numeric' });
    }

    const weekday = d.toLocaleDateString('en-US', { weekday: 'long' });
    const month = d.toLocaleDateString('en-US', { month: 'short' });
    const day = d.getDate();
    const year = d.getFullYear();
    return `${weekday}, ${month} ${day}, ${year}`;
  });

  // Header Center Subtitle: e.g. "(Week 44)"
  readonly headerWeekNumber = computed(() => {
    const parts = this.selectedDate().split('-').map(Number);
    if (parts.length !== 3) return '';
    const d = new Date(parts[0], parts[1] - 1, parts[2]);
    const week = this.getIsoWeekNumber(d);
    return `(Week ${week})`;
  });

  // Specialist Filter Selection (Multi-Select)
  readonly selectedProviderIds = signal<number[]>([]);
  readonly selectedProviderId = signal<number | null>(null);

  // Computed: Are all providers currently selected / active?
  readonly isAllProvidersSelected = computed(() => {
    const selected = this.selectedProviderIds();
    const allActive = this.providers().filter(p => p.status === ProviderStatus.Active || (p.status as any) === 1);
    return selected.length === 0 || (allActive.length > 0 && selected.length >= allActive.length);
  });

  // Computed: Is a custom specialist subset filter applied?
  readonly hasProviderFilter = computed(() => {
    return !this.isAllProvidersSelected() && this.selectedProviderIds().length > 0;
  });

  // Single provider object if exactly 1 provider is selected (for avatar display)
  readonly singleSelectedProvider = computed(() => {
    const selected = this.selectedProviderIds();
    if (selected.length === 1) {
      return this.providers().find(p => p.id === selected[0]) || null;
    }
    const singleId = this.selectedProviderId();
    if (singleId !== null) {
      return this.providers().find(p => p.id === singleId) || null;
    }
    return null;
  });

  readonly selectedProvider = computed(() => this.singleSelectedProvider());

  readonly selectedProviderLabel = computed(() => {
    const p = this.singleSelectedProvider();
    if (p) return p.name;
    return this.selectedProvidersLabel();
  });

  // Dropdown Label for Service Providers Multi-Select Filter
  readonly selectedProvidersLabel = computed(() => {
    if (this.isAllProvidersSelected()) {
      return 'All Service Providers';
    }
    const selected = this.selectedProviderIds();
    const all = this.providers();
    if (selected.length === 1) {
      const match = all.find(item => item.id === selected[0]);
      return match ? match.name : '1 Specialist';
    }
    return `${selected.length} Specialists Selected`;
  });

  // Dropdown Specialist Search Filter
  readonly specialistSearchQuery = signal<string>('');
  readonly filteredDropdownProviders = computed(() => {
    const q = this.specialistSearchQuery().trim().toLowerCase();
    const all = this.providers();
    if (!q) return all;
    return all.filter(p =>
      p.name.toLowerCase().includes(q) ||
      (p.serviceType && p.serviceType.toLowerCase().includes(q))
    );
  });

  onSpecialistSearchInput(event: Event): void {
    const target = event.target as HTMLInputElement;
    this.specialistSearchQuery.set(target.value);
  }

  clearSpecialistSearch(event?: Event): void {
    if (event) event.stopPropagation();
    this.specialistSearchQuery.set('');
  }

  // Real-time formatted IST time label for live marker pill
  readonly currentLiveTimeDisplay = signal<string>('');

  // Data Signals
  readonly allAppointments = signal<AppointmentDto[]>([]);
  readonly providers = signal<ServiceProviderDto[]>([]);
  readonly providerSchedules = signal<Map<number, ProviderScheduleOverviewItemDto>>(new Map());
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string>('');
  readonly successMessage = signal<string>('');

  // Mobile focused provider
  readonly selectedMobileProviderId = signal<number | null>(null);

  // Column Resizing State (Option 1: Column Edge Drag)
  readonly defaultColumnWidth = 260;
  readonly minColumnWidth = 220;
  readonly maxColumnWidth = 700;
  readonly providerColumnWidths = signal<Map<number, number>>(new Map());
  isResizingColumn = false;
  private activeResizingProviderId: number | null = null;
  private resizeStartX = 0;
  private resizeStartWidth = 0;

  hasCustomColumnWidth(providerId: number): boolean {
    return this.providerColumnWidths().has(providerId);
  }

  getColumnWidth(providerId: number): number {
    return this.providerColumnWidths().get(providerId) || this.defaultColumnWidth;
  }

  onResizeStart(providerId: number, event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();

    this.isResizingColumn = true;
    this.activeResizingProviderId = providerId;
    this.resizeStartX = event.clientX;
    const targetEl = (event.target as HTMLElement).closest('.provider-col-header, .specialist-time-column') as HTMLElement;
    this.resizeStartWidth = targetEl ? targetEl.offsetWidth : this.getColumnWidth(providerId);

    document.body.classList.add('is-resizing-column');

    const onMouseMove = (moveEvent: MouseEvent) => {
      if (!this.isResizingColumn || this.activeResizingProviderId === null) return;
      const delta = moveEvent.clientX - this.resizeStartX;
      const newWidth = Math.max(this.minColumnWidth, Math.min(this.maxColumnWidth, Math.round(this.resizeStartWidth + delta)));

      const updated = new Map(this.providerColumnWidths());
      updated.set(this.activeResizingProviderId, newWidth);
      this.providerColumnWidths.set(updated);
    };

    const onMouseUp = () => {
      this.isResizingColumn = false;
      this.activeResizingProviderId = null;
      document.body.classList.remove('is-resizing-column');
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
    };

    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
  }

  resetColumnWidth(providerId: number, event?: MouseEvent): void {
    if (event) {
      event.preventDefault();
      event.stopPropagation();
    }
    const updated = new Map(this.providerColumnWidths());
    updated.delete(providerId);
    this.providerColumnWidths.set(updated);
  }

  // --- Drag-to-Scroll (Canvas/Graph Pan Scrolling) ---
  isPanningGrid = false;
  justPanned = false;
  private panStartX = 0;
  private panStartY = 0;
  private panStartScrollLeft = 0;
  private panStartScrollTop = 0;

  onGridScroll(event: Event): void {
    const target = event.target as HTMLElement;
    if (this.providerHeadersContainer?.nativeElement && Math.abs(this.providerHeadersContainer.nativeElement.scrollLeft - target.scrollLeft) > 1) {
      this.providerHeadersContainer.nativeElement.scrollLeft = target.scrollLeft;
    }
  }

  onHeaderScroll(event: Event): void {
    const target = event.target as HTMLElement;
    if (this.gridScrollArea?.nativeElement && Math.abs(this.gridScrollArea.nativeElement.scrollLeft - target.scrollLeft) > 1) {
      this.gridScrollArea.nativeElement.scrollLeft = target.scrollLeft;
    }
  }

  onGridMouseDown(event: MouseEvent): void {
    // Only primary mouse button (left-click)
    if (event.button !== 0) return;

    const target = event.target as HTMLElement;
    // Don't pan if clicking on appointment card, action buttons, inputs, or resize handle
    if (
      target.closest('.schedule-appointment-card') ||
      target.closest('.col-resize-handle') ||
      target.closest('button') ||
      target.closest('.custom-dropdown-popover')
    ) {
      return;
    }

    const grid = this.gridScrollArea?.nativeElement;
    if (!grid) return;

    this.panStartX = event.pageX;
    this.panStartY = event.pageY;
    this.panStartScrollLeft = grid.scrollLeft;
    this.panStartScrollTop = grid.scrollTop;

    let moved = false;

    const onMouseMove = (moveEvent: MouseEvent) => {
      const dx = moveEvent.pageX - this.panStartX;
      const dy = moveEvent.pageY - this.panStartY;

      if (!moved && (Math.abs(dx) > 3 || Math.abs(dy) > 3)) {
        moved = true;
        this.isPanningGrid = true;
        document.body.classList.add('is-panning-grid');
      }

      if (moved) {
        grid.scrollLeft = this.panStartScrollLeft - dx;
        grid.scrollTop = this.panStartScrollTop - dy;
        if (this.providerHeadersContainer?.nativeElement) {
          this.providerHeadersContainer.nativeElement.scrollLeft = grid.scrollLeft;
        }
      }
    };

    const onMouseUp = () => {
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mouseup', onMouseUp);
      document.body.classList.remove('is-panning-grid');

      if (moved) {
        this.isPanningGrid = false;
        this.justPanned = true;
        setTimeout(() => {
          this.justPanned = false;
        }, 120);
      }
    };

    window.addEventListener('mousemove', onMouseMove);
    window.addEventListener('mouseup', onMouseUp);
  }

  isProviderDropdownOpen = false;
  isEmployeeDropdownOpen = false;

  // Status Filter
  selectedStatus: AppointmentStatus | null = null;
  searchQuery = '';

  // Modals & Details Drawer State
  readonly selectedAppointmentForDetail = signal<AppointmentDto | null>(null);
  readonly selectedAppointmentForReschedule = signal<AppointmentDto | null>(null);
  readonly selectedAppointmentForCancel = signal<AppointmentDto | null>(null);

  // New Booking Modal
  readonly isBookingModalOpen = signal<boolean>(false);
  bookingPrefillProviderId: number | null = null;
  bookingPrefillDate: string = '';

  // Time Axis Config (Strictly 10:00 AM to 05:00 PM matching clinic availability)
  readonly startHour = signal<number>(10);
  readonly endHour = signal<number>(18);
  readonly slotHeight = 78;

  // Computed Dynamic Slots List based on startHour & endHour
  readonly slotsList = computed<ScheduleSlot[]>(() => {
    const start = this.startHour();
    const end = this.endHour();
    const slots: ScheduleSlot[] = [];

    for (let h = start; h < end; h++) {
      for (let m = 0; m < 60; m += 30) {
        const startMinutes = h * 60 + m;
        const endMinutes = startMinutes + 30;

        const rawH = String(h).padStart(2, '0');
        const rawM = String(m).padStart(2, '0');
        const raw = `${rawH}:${rawM}`;

        const endH = String(Math.floor(endMinutes / 60)).padStart(2, '0');
        const endM = String(endMinutes % 60).padStart(2, '0');
        const rawEnd = `${endH}:${endM}`;

        const display = this.formatMinutesTo12Hour(startMinutes);
        const displayEnd = this.formatMinutesTo12Hour(endMinutes);

        slots.push({
          raw,
          rawEnd,
          display,
          displayEnd,
          rangeText: `${display} – ${displayEnd}`,
          isHourMark: m === 0,
          isLunchBreak: false
        });
      }
    }

    return slots;
  });

  // Current Time Indicator
  readonly currentTimeTop = signal<number | null>(null);
  private timeUpdateInterval?: any;

  // Luxury Glassmorphic Specialist Palettes
  readonly providerThemes: ProviderTheme[] = [
    {
      name: 'Rose',
      dot: '#e11d48',
      cardBg: '#fff1f2',
      cardBorder: '#fecdd3',
      textColor: '#183b32',
      serviceColor: '#be123c',
      timeTagBg: 'transparent',
      timeTagText: '#4c0519'
    },
    {
      name: 'Emerald',
      dot: '#1e5142',
      cardBg: '#eef7f2',
      cardBorder: '#c3ded1',
      textColor: '#183b32',
      serviceColor: '#2d6a56',
      timeTagBg: 'transparent',
      timeTagText: '#5b756c'
    },
    {
      name: 'Sky',
      dot: '#0284c7',
      cardBg: '#f0f7ff',
      cardBorder: '#bae6fd',
      textColor: '#183b32',
      serviceColor: '#0369a1',
      timeTagBg: 'transparent',
      timeTagText: '#5b756c'
    },
    {
      name: 'Purple',
      dot: '#7c3aed',
      cardBg: '#f5f3ff',
      cardBorder: '#ddd6fe',
      textColor: '#183b32',
      serviceColor: '#6d28d9',
      timeTagBg: 'transparent',
      timeTagText: '#5b756c'
    },
    {
      name: 'Amber',
      dot: '#d97706',
      cardBg: '#fffdf5',
      cardBorder: '#fde68a',
      textColor: '#183b32',
      serviceColor: '#b45309',
      timeTagBg: 'transparent',
      timeTagText: '#78350f'
    },
    {
      name: 'Teal',
      dot: '#0d9488',
      cardBg: '#f0fdfa',
      cardBorder: '#99f6e4',
      textColor: '#183b32',
      serviceColor: '#0f766e',
      timeTagBg: 'transparent',
      timeTagText: '#5b756c'
    }
  ];

  // FullCalendar Options for Month & Week Views
  calendarOptions: CalendarOptions = {
    plugins: [dayGridPlugin, timeGridPlugin, interactionPlugin],
    initialView: 'timeGridWeek',
    headerToolbar: false,
    firstDay: 0,
    weekends: true,
    dayMaxEvents: 3,
    allDaySlot: false,
    slotMinTime: '10:00:00',
    slotMaxTime: '18:00:00',
    slotDuration: '00:30:00',
    slotLabelInterval: '00:30:00',
    slotLabelFormat: {
      hour: '2-digit',
      minute: '2-digit',
      omitZeroMinute: false,
      meridiem: 'short',
      hour12: true
    },
    eventTimeFormat: {
      hour: 'numeric',
      minute: '2-digit',
      meridiem: 'short'
    },
    height: 'auto',
    contentHeight: 'auto',
    stickyHeaderDates: true,
    expandRows: true,
    nowIndicator: true,
    fixedWeekCount: false,
    showNonCurrentDates: false,
    events: [],
    datesSet: (dateInfo: DatesSetArg) => {
      // In day view, ignore datesSet from background FullCalendar instance
      if (this.activeView() === 'day') return;
      const parts = this.selectedDate().split('-').map(Number);
      if (parts.length === 3) {
        const cur = new Date(parts[0], parts[1] - 1, parts[2]);
        if (cur < dateInfo.start || cur >= dateInfo.end) {
          const mid = new Date((dateInfo.start.getTime() + dateInfo.end.getTime()) / 2);
          const newDateStr = this.formatDateToString(mid);
          this.selectedDate.set(newDateStr);
          this.fetchAppointments();
        }
      }
    },
    dateClick: (arg: DateClickArg) => {
      this.selectedDate.set(arg.dateStr.split('T')[0]);
      this.activeView.set('day');
    },
    eventClick: (arg: EventClickArg) => {
      const target = arg.jsEvent?.target as HTMLElement | undefined;
      // If user clicked the "+X more" button or drawer close button, do not open detail sheet
      if (target?.closest('.fc-week-more-btn') || target?.closest('.fc-overflow-drawer-close')) {
        return;
      }

      const aptCard = target?.closest ? (target.closest('[data-apt-id]') as HTMLElement) : null;
      if (aptCard) {
        const aptId = Number(aptCard.getAttribute('data-apt-id'));
        const found = this.allAppointments().find(a => a.id === aptId);
        if (found) {
          this.selectedAppointmentForDetail.set(found);
          return;
        }
      }

      const cluster = arg.event.extendedProps['cluster'] as AppointmentDto[];
      if (cluster && cluster.length > 0) {
        this.selectedAppointmentForDetail.set(cluster[0]);
        return;
      }

      const apt = arg.event.extendedProps['appointment'] as AppointmentDto;
      if (apt) {
        this.selectedAppointmentForDetail.set(apt);
      }
    },
    eventContent: (arg) => {
      if (arg.view.type === 'dayGridMonth') {
        const apt = arg.event.extendedProps['appointment'] as AppointmentDto;
        if (!apt) return { html: '' };

        const themeDot = arg.event.extendedProps['themeDot'] || '#6366f1';
        const customer = apt.customerName || arg.event.title || 'Client';
        const providerName = apt.serviceProviderName || 'Specialist';
        const providerAvatar = this.getProviderAvatarHtml(apt, 16);
        return {
          html: `
            <div class="fc-custom-month-event" data-apt-id="${apt.id}" style="border-left: 3px solid ${themeDot}; cursor: pointer;" title="${customer} • Specialist: ${providerName}">
              <div class="fc-cust-icon-badge fc-month-cust-icon">
                <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path>
                  <circle cx="12" cy="7" r="4"></circle>
                </svg>
              </div>
              <span class="fc-month-customer">${customer}</span>
              <div class="fc-month-provider-box" title="Specialist: ${providerName}">
                ${providerAvatar}
              </div>
            </div>
          `
        };
      }

      // Week view: check if cluster of multiple appointments (stacked ek ke niche ek)
      const cluster = arg.event.extendedProps['cluster'] as AppointmentDto[];
      if (cluster && cluster.length > 1) {
        const total = cluster.length;
        // User directive: 1-2 appointments show vertically stacked. If 3rd arrives -> show top 2 + "+X more" like month view!
        const maxVisible = 2;
        const visibleItems = total <= maxVisible ? cluster : cluster.slice(0, maxVisible);
        const hiddenCount = total - maxVisible;

        const cardsHtml = visibleItems.map(item => this.renderWeekCardHtml(item, true)).join('');
        const timeSlotLabel = visibleItems[0]?.timeSlotText || this.formatTimeTo12Hour(visibleItems[0]?.startTime) || '';

        return {
          html: `
            <div class="fc-week-stack-container ${hiddenCount > 0 ? 'has-overflow' : ''}">
              ${cardsHtml}
              ${hiddenCount > 0 ? `
                <div class="fc-week-more-btn" title="Click to view all ${total} appointments">
                  <span class="fc-week-more-text">+${hiddenCount} more</span>
                  <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                    <polyline points="6 9 12 15 18 9"></polyline>
                  </svg>
                </div>
                <div class="fc-week-overflow-drawer">
                  <div class="fc-overflow-drawer-header">
                    <span>${total} Appointments${timeSlotLabel ? ` • ${timeSlotLabel}` : ''}</span>
                    <span class="fc-overflow-drawer-close" title="Close">✕</span>
                  </div>
                  <div class="fc-overflow-drawer-body">
                    ${cluster.map(item => this.renderWeekCardHtml(item, true)).join('')}
                  </div>
                </div>
              ` : ''}
            </div>
          `
        };
      }

      // Single appointment card in Week view
      const singleApt = (arg.event.extendedProps['appointment'] as AppointmentDto)
        || (cluster && cluster.length === 1 ? cluster[0] : null);
      if (singleApt) {
        return {
          html: this.renderWeekCardHtml(singleApt, false)
        };
      }

      return { html: '' };
    }
  };

  // Filtered Providers to display (Strictly Active Service Providers only)
  readonly displayedProviders = computed(() => {
    const allActive = this.providers().filter(p => p.status === ProviderStatus.Active || (p.status as any) === 1);
    const selected = this.selectedProviderIds();
    if (selected.length === 0 || (allActive.length > 0 && selected.length >= allActive.length)) {
      return allActive;
    }
    return allActive.filter(p => selected.includes(p.id));
  });

  // Mobile Active Provider
  readonly currentMobileProvider = computed(() => {
    const list = this.displayedProviders();
    if (list.length === 0) return undefined;
    const selectedId = this.selectedMobileProviderId();
    if (selectedId !== null) {
      const found = list.find(p => p.id === selectedId);
      if (found) return found;
    }
    return list[0];
  });

  // Formatted Date Title: "Monday, Aug 4, 2025 (32. Week)"
  readonly formattedDateTitle = computed(() => {
    const dateStr = this.selectedDate();
    if (!dateStr) return '';
    const parts = dateStr.split('-');
    if (parts.length !== 3) return dateStr;
    const d = new Date(Number(parts[0]), Number(parts[1]) - 1, Number(parts[2]));

    const dayName = d.toLocaleDateString('en-US', { weekday: 'long' });
    const monthName = d.toLocaleDateString('en-US', { month: 'short' });
    const dayNum = d.getDate();
    const year = d.getFullYear();

    const weekNum = this.getIsoWeekNumber(d);
    return `${dayName}, ${monthName} ${dayNum}, ${year} (${weekNum}. Week)`;
  });

  readonly isCurrentDateToday = computed(() => {
    return this.selectedDate() === this.getIndianToday();
  });

  ngOnInit(): void {
    this.loadProviders();
    this.loadAvailabilityOverview();
    this.fetchAppointments();
    this.updateCurrentTimeIndicator();

    // Attach listener for week view drawer toggle & outside clicks
    document.addEventListener('click', this.handlePopoverDismiss);

    // Update live IST indicator every 60 seconds
    this.timeUpdateInterval = setInterval(() => {
      this.updateCurrentTimeIndicator();
    }, 60000);
  }

  ngOnDestroy(): void {
    document.removeEventListener('click', this.handlePopoverDismiss);
    if (this.timeUpdateInterval) {
      clearInterval(this.timeUpdateInterval);
    }
  }

  // --- Backend Data Loading ---
  loadProviders(): void {
    this.providerService.getActiveProviders().subscribe({
      next: (res) => {
        // Enforce only Active service providers are loaded
        const activeList = (res || []).filter(p => p.status === ProviderStatus.Active || (p.status as any) === 1);
        this.providers.set(activeList);
        if (activeList.length > 0 && !this.selectedMobileProviderId()) {
          this.selectedMobileProviderId.set(activeList[0].id);
        }
      },
      error: () => this.providers.set([])
    });
  }

  loadAvailabilityOverview(): void {
    this.availabilityService.getAvailabilityOverview().subscribe({
      next: (res) => {
        if (!res || !res.providers) return;

        const scheduleMap = new Map<number, ProviderScheduleOverviewItemDto>();
        let minStartMins = 10 * 60;  // 10:00 AM
        let maxEndMins = 17 * 60;   // 05:00 PM

        for (const prov of res.providers) {
          scheduleMap.set(prov.providerId, prov);

          for (const day of prov.days) {
            if (day.isAvailable && day.startTime && day.endTime) {
              const sMins = this.parseTimeToMinutes(day.startTime);
              const eMins = this.parseTimeToMinutes(day.endTime);

              if (sMins < minStartMins && sMins >= 6 * 60) {
                minStartMins = Math.floor(sMins / 60) * 60;
              }
              if (eMins > maxEndMins && eMins <= 23 * 60) {
                maxEndMins = Math.ceil(eMins / 60) * 60;
              }
            }
          }
        }

        this.providerSchedules.set(scheduleMap);
        this.startHour.set(10);
        this.endHour.set(18);

        // Update FullCalendar slot bounds to match backend range
        this.calendarOptions = {
          ...this.calendarOptions,
          slotMinTime: '10:00:00',
          slotMaxTime: '18:00:00'
        };

        this.updateCurrentTimeIndicator();
      },
      error: (err) => {
        console.warn('Availability overview not loaded, using fallback timetable.', err);
      }
    });
  }

  fetchAppointments(): void {
    if (this.isLoading()) return;
    this.isLoading.set(true);

    const curDate = new Date(this.selectedDate());
    const start = new Date(curDate);
    start.setDate(start.getDate() - 7);
    const end = new Date(curDate);
    end.setDate(end.getDate() + 14);

    const filters: AppointmentFilterParams = {
      pageNumber: 1,
      pageSize: 150,
      startDate: this.formatDateToString(start),
      endDate: this.formatDateToString(end)
    };

    if (this.selectedStatus !== null) {
      filters.status = this.selectedStatus;
    }

    if (this.searchQuery.trim()) {
      filters.search = this.searchQuery.trim();
    }

    this.appointmentService.getAppointments(filters).subscribe({
      next: (res) => {
        const items = res?.items || [];
        this.allAppointments.set(items);
        this.mapAppointmentsToCalendar(items);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.errorMessage.set(err.error?.message || 'Failed to sync schedule.');
        setTimeout(() => this.errorMessage.set(''), 4000);
      }
    });
  }

  formatIsoDateTime(dateStr: string, timeStr?: string | null): string {
    if (!dateStr) return '';
    const datePart = dateStr.split('T')[0];
    if (!timeStr) return `${datePart}T10:00:00`;

    const cleanTime = timeStr.trim();
    if (cleanTime.includes('AM') || cleanTime.includes('PM')) {
      const isPm = cleanTime.toUpperCase().includes('PM');
      const timeWithoutMeridiem = cleanTime.replace(/AM|PM/gi, '').trim();
      const parts = timeWithoutMeridiem.split(':').map(Number);
      let hours = parts[0] || 0;
      const minutes = parts[1] || 0;
      if (isPm && hours < 12) hours += 12;
      if (!isPm && hours === 12) hours = 0;
      return `${datePart}T${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:00`;
    }

    const parts = cleanTime.split(':');
    const h = (parts[0] || '0').padStart(2, '0');
    const m = (parts[1] || '0').padStart(2, '0');
    const s = (parts[2] || '00').substring(0, 2).padStart(2, '0');
    return `${datePart}T${h}:${m}:${s}`;
  }

  getProviderAvatarHtml(apt: AppointmentDto, size: number = 18): string {
    const providerName = apt.serviceProviderName || 'Specialist';
    const picUrl = apt.serviceProviderProfilePictureUrl;
    const initials = getInitials(providerName);
    const gradient = getAvatarGradient(providerName);

    if (picUrl && picUrl.trim().length > 0) {
      return `
        <div class="fc-provider-avatar-box" style="width: ${size}px; height: ${size}px; min-width: ${size}px; border-radius: 50%; overflow: hidden; position: relative; flex-shrink: 0; box-shadow: 0 1px 3px rgba(0,0,0,0.35); border: 1.5px solid rgba(255,255,255,0.25);">
          <img src="${picUrl}" alt="${providerName}" style="width: 100%; height: 100%; object-fit: cover;" onerror="this.style.display='none'; if(this.nextElementSibling) this.nextElementSibling.style.display='flex';" />
          <div style="display: none; width: 100%; height: 100%; align-items: center; justify-content: center; background: ${gradient}; color: #ffffff; font-size: ${Math.max(8, Math.round(size * 0.42))}px; font-weight: 700;">${initials}</div>
        </div>
      `;
    }

    return `
      <div class="fc-provider-avatar-box" style="width: ${size}px; height: ${size}px; min-width: ${size}px; border-radius: 50%; display: flex; align-items: center; justify-content: center; background: ${gradient}; color: #ffffff; font-size: ${Math.max(8, Math.round(size * 0.42))}px; font-weight: 700; flex-shrink: 0; box-shadow: 0 1px 3px rgba(0,0,0,0.35); border: 1.5px solid rgba(255,255,255,0.25);">
        ${initials}
      </div>
    `;
  }

  renderWeekCardHtml(apt: AppointmentDto, isStacked: boolean = false): string {
    const theme = this.getAppointmentTheme(apt);
    const themeDot = theme.dot || '#1e5142';
    const cardBg = theme.cardBg || '#ffffff';
    const cardBorder = theme.cardBorder || '#dce8e1';
    const customer = apt.customerName || 'Client';
    const providerName = apt.serviceProviderName || 'Specialist';
    const service = apt.serviceName || apt.reason || apt.serviceProviderType || 'Consultation';
    const statusLabel = this.getAppointmentStatusLabel(apt);
    const startTimeFormatted = this.formatTimeTo12Hour(apt.startTime);
    const endTimeFormatted = this.formatTimeTo12Hour(apt.endTime);
    const timeDisplay = (startTimeFormatted && endTimeFormatted)
      ? `${startTimeFormatted} – ${endTimeFormatted}`
      : `${apt.startTime || ''} – ${apt.endTime || ''}`;

    const providerAvatar = this.getProviderAvatarHtml(apt, 16);

    return `
      <div class="fc-week-clean-card ${isStacked ? 'is-stacked-card' : ''}" data-apt-id="${apt.id}"
        style="background: ${cardBg}; border-color: ${cardBorder}; border-left: 3.5px solid ${themeDot};"
        title="${customer} • Specialist: ${providerName} • ${service} • ${timeDisplay} • ${statusLabel}">
        <!-- Line 1: Customer Name with Customer Icon -->
        <div class="fc-clean-row fc-customer-row">
          <div class="fc-cust-icon-badge">
            <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path>
              <circle cx="12" cy="7" r="4"></circle>
            </svg>
          </div>
          <span class="fc-clean-customer-name" title="${customer}">${customer}</span>
        </div>

        <!-- Line 2: Service Provider with Avatar + Service Title -->
        <div class="fc-clean-row fc-provider-row">
          ${providerAvatar}
          <span class="fc-clean-provider-name" title="${providerName}">${providerName}</span>
          <span class="fc-clean-service-tag" title="${service}">• ${service}</span>
        </div>
      </div>
    `;
  }

  private groupAppointmentsForWeek(items: AppointmentDto[]): AppointmentDto[][] {
    if (items.length <= 1) return items.map(a => [a]);

    const sorted = [...items].sort((a, b) => {
      if (a.appointmentDate !== b.appointmentDate) {
        return a.appointmentDate.localeCompare(b.appointmentDate);
      }
      return this.parseTimeToMinutes(a.startTime) - this.parseTimeToMinutes(b.startTime);
    });

    const clusters: AppointmentDto[][] = [];
    let currentCluster: AppointmentDto[] = [sorted[0]];
    let currentEnd = this.parseTimeToMinutes(sorted[0].endTime);
    let currentDate = sorted[0].appointmentDate;

    for (let i = 1; i < sorted.length; i++) {
      const apt = sorted[i];
      const start = this.parseTimeToMinutes(apt.startTime);
      const end = this.parseTimeToMinutes(apt.endTime);

      if (apt.appointmentDate === currentDate && start < currentEnd) {
        currentCluster.push(apt);
        if (end > currentEnd) {
          currentEnd = end;
        }
      } else {
        clusters.push(currentCluster);
        currentCluster = [apt];
        currentEnd = end;
        currentDate = apt.appointmentDate;
      }
    }

    if (currentCluster.length > 0) {
      clusters.push(currentCluster);
    }

    return clusters;
  }

  private mapAppointmentsToCalendar(items: AppointmentDto[]): void {
    // Only map appointments for active service providers
    const activeIds = new Set(this.displayedProviders().map(p => p.id));
    const activeItems = activeIds.size > 0
      ? items.filter(a => activeIds.has(a.serviceProviderId))
      : items;

    // Month view: individual events so each displays on its line in day cell (only for the active month)
    if (this.activeView() === 'month') {
      const parts = this.selectedDate().split('-').map(Number);
      const selYear = parts[0];
      const selMonth = parts[1];
      const monthItems = activeItems.filter(apt => {
        if (!apt.appointmentDate) return false;
        const aptParts = apt.appointmentDate.split('T')[0].split('-').map(Number);
        return aptParts[0] === selYear && aptParts[1] === selMonth;
      });

      const events: EventInput[] = monthItems.map(apt => {
        const theme = this.getProviderTheme(apt.serviceProviderId);
        const startIso = this.formatIsoDateTime(apt.appointmentDate, apt.startTime);
        return {
          id: apt.id.toString(),
          title: apt.customerName,
          start: startIso,
          backgroundColor: 'transparent',
          borderColor: 'transparent',
          textColor: '#ffffff',
          extendedProps: {
            appointment: apt,
            themeDot: theme.dot
          }
        };
      });

      this.calendarOptions = {
        ...this.calendarOptions,
        events
      };
      return;
    }

    // Week view: group overlapping/same-slot appointments so they stack vertically (ek ke niche ek)
    const clusters = this.groupAppointmentsForWeek(activeItems);
    const events: EventInput[] = clusters.map(cluster => {
      const first = cluster[0];
      const theme = this.getProviderTheme(first.serviceProviderId);
      const startIso = this.formatIsoDateTime(first.appointmentDate, first.startTime);
      let endIso = this.formatIsoDateTime(first.appointmentDate, first.endTime);

      const startDate = new Date(startIso);
      let endDate = new Date(endIso);
      if (isNaN(endDate.getTime()) || endDate.getTime() <= startDate.getTime()) {
        endDate = new Date(startDate.getTime() + 30 * 60 * 1000);
        endIso = endDate.toISOString();
      }

      if (cluster.length === 1) {
        return {
          id: first.id.toString(),
          title: first.customerName,
          start: startIso,
          end: endIso,
          backgroundColor: 'transparent',
          borderColor: 'transparent',
          textColor: '#ffffff',
          extendedProps: {
            appointment: first,
            themeDot: theme.dot
          }
        };
      }

      // Multiple appointments clustered together -> takes 100% column width, cards stack vertically
      return {
        id: `cluster_${first.id}_${cluster.length}`,
        title: `${cluster.length} Appointments`,
        start: startIso,
        end: endIso,
        backgroundColor: 'transparent',
        borderColor: 'transparent',
        textColor: '#ffffff',
        extendedProps: {
          cluster: cluster,
          themeDot: theme.dot
        }
      };
    });

    this.calendarOptions = {
      ...this.calendarOptions,
      events
    };
  }

  // --- Dynamic Provider Schedule Helpers ---
  getSelectedDayOfWeek(): number {
    const parts = this.selectedDate().split('-').map(Number);
    if (parts.length !== 3) return new Date().getDay();
    return new Date(parts[0], parts[1] - 1, parts[2]).getDay();
  }

  getSelectedDayName(): string {
    const parts = this.selectedDate().split('-').map(Number);
    if (parts.length !== 3) return '';
    const d = new Date(parts[0], parts[1] - 1, parts[2]);
    return d.toLocaleDateString('en-US', { weekday: 'long' });
  }

  // Precomputed lookup for current day schedule per provider (O(1) lookups)
  readonly currentDaySchedules = computed(() => {
    const schedules = this.providerSchedules();
    const dayOfWeek = this.getSelectedDayOfWeek();
    const map = new Map<number, {
      isAvailable: boolean;
      startMins: number;
      endMins: number;
      breakStartMins: number;
      breakEndMins: number;
      workingHoursText: string;
    }>();

    for (const [pId, prov] of schedules.entries()) {
      const day = prov.days?.find(d => d.dayOfWeek === dayOfWeek);
      if (!day || !day.isAvailable) {
        map.set(pId, {
          isAvailable: false,
          startMins: 0,
          endMins: 0,
          breakStartMins: -1,
          breakEndMins: -1,
          workingHoursText: '—'
        });
      } else {
        const sMins = day.startTime ? this.parseTimeToMinutes(day.startTime) : 10 * 60;
        const eMins = day.endTime ? this.parseTimeToMinutes(day.endTime) : 17 * 60;
        const bStart = (day.breakStartTime && day.breakEndTime) ? this.parseTimeToMinutes(day.breakStartTime) : -1;
        const bEnd = (day.breakStartTime && day.breakEndTime) ? this.parseTimeToMinutes(day.breakEndTime) : -1;
        const sText = day.startTime ? this.formatTimeTo12Hour(day.startTime) : '10:00 AM';
        const eText = day.endTime ? this.formatTimeTo12Hour(day.endTime) : '05:00 PM';
        map.set(pId, {
          isAvailable: true,
          startMins: sMins,
          endMins: eMins,
          breakStartMins: bStart,
          breakEndMins: bEnd,
          workingHoursText: `${sText} – ${eText}`
        });
      }
    }
    return map;
  });

  isProviderAvailableToday(providerId: number): boolean {
    const sched = this.currentDaySchedules().get(providerId);
    return sched ? sched.isAvailable : false;
  }

  isSlotBreakForProvider(providerId: number, slotRaw: string): boolean {
    const sched = this.currentDaySchedules().get(providerId);
    if (!sched || sched.breakStartMins < 0) return false;
    const slotMins = this.parseTimeToMinutes(slotRaw);
    return slotMins >= sched.breakStartMins && slotMins < sched.breakEndMins;
  }

  isSlotOutsideWorkingHours(providerId: number, slotRaw: string): boolean {
    const sched = this.currentDaySchedules().get(providerId);
    if (!sched || !sched.isAvailable) return false;
    const slotMins = this.parseTimeToMinutes(slotRaw);
    return slotMins < sched.startMins || slotMins >= sched.endMins;
  }

  getProviderWorkingHoursText(providerId: number): string {
    const sched = this.currentDaySchedules().get(providerId);
    return sched ? sched.workingHoursText : '—';
  }

  // Check if a date string is within the booking window (Today to Today + 15 days)
  isDateWithinBookingWindow(dateStr: string): boolean {
    if (!dateStr) return false;
    const todayStr = this.getIndianToday();
    const [y1, m1, d1] = todayStr.split('-').map(Number);
    const [y2, m2, d2] = dateStr.split('-').map(Number);
    const todayMidnight = new Date(y1, m1 - 1, d1).getTime();
    const targetMidnight = new Date(y2, m2 - 1, d2).getTime();
    const diffDays = Math.round((targetMidnight - todayMidnight) / (1000 * 60 * 60 * 24));
    return diffDays >= 0 && diffDays <= 15;
  }

  // Check if a time slot is in the past (past date OR earlier today than current IST time)
  isSlotInPast(slotRaw: string): boolean {
    const todayStr = this.getIndianToday();
    const selected = this.selectedDate();

    if (selected < todayStr) {
      return true;
    }
    if (selected > todayStr) {
      return false;
    }

    // On today's date: compare slot start time with current Indian time in minutes
    const currentMins = this.getIndianCurrentMinutes();
    const parts = slotRaw.split(':').map(Number);
    const slotStartMins = parts[0] * 60 + parts[1];

    return slotStartMins <= currentMins;
  }

  getSlotTooltip(provider: ServiceProviderDto, slot: ScheduleSlot): string {
    if (this.isSlotInPast(slot.raw)) {
      return `${slot.rangeText} – Past time slot (Booking closed)`;
    }
    if (!this.isDateWithinBookingWindow(this.selectedDate())) {
      const today = this.getIndianToday();
      if (this.selectedDate() < today) {
        return `Booking closed (past date)`;
      } else {
        return `Booking only available up to 15 days in advance`;
      }
    }
    if (!this.isProviderAvailableToday(provider.id)) {
      return `${provider.name} is on Day Off today (${this.getSelectedDayName()}) - Booking unavailable`;
    }
    if (this.isSlotBreakForProvider(provider.id, slot.raw)) {
      return `${slot.rangeText} – ${provider.name}'s Break / Lunch (Not bookable)`;
    }
    if (this.isSlotOutsideWorkingHours(provider.id, slot.raw)) {
      return `${slot.rangeText} – Outside ${provider.name}'s regular working hours (${this.getProviderWorkingHoursText(provider.id)})`;
    }
    return `Click to book ${slot.rangeText} with ${provider.name}`;
  }

  // --- TrackBy Performance Helpers for Templates ---
  trackByProviderId(_index: number, provider: ServiceProviderDto): number {
    return provider.id;
  }

  trackBySlotRaw(_index: number, slot: ScheduleSlot): string {
    return slot.raw;
  }

  trackByAppointmentId(_index: number, item: PositionedAppointment): number {
    return item.appointment.id;
  }

  // Precomputed Map of Provider ID -> Positioned Appointments (Calculated ONLY when data or filters change!)
  readonly providerPositionedAppointments = computed<Map<number, PositionedAppointment[]>>(() => {
    const selDate = this.selectedDate();
    const query = this.searchQuery.trim().toLowerCase();
    const allApts = this.allAppointments();
    const activeProviders = this.displayedProviders();
    const statusFilter = this.selectedStatus;

    const map = new Map<number, PositionedAppointment[]>();
    for (const provider of activeProviders) {
      map.set(provider.id, this.computePositionedAppointmentsForProvider(provider.id, selDate, query, statusFilter, allApts));
    }
    return map;
  });

  getAppointmentsForProvider(providerId: number): PositionedAppointment[] {
    return this.providerPositionedAppointments().get(providerId) || [];
  }

  private computePositionedAppointmentsForProvider(
    providerId: number,
    selDate: string,
    query: string,
    statusFilter: AppointmentStatus | null,
    allApts: AppointmentDto[]
  ): PositionedAppointment[] {
    const theme = this.getProviderTheme(providerId);
    const startHourVal = this.startHour();

    const list = allApts.filter(a => {
      const aptDate = a.appointmentDate.split('T')[0];
      return a.serviceProviderId === providerId && aptDate === selDate;
    });

    let filtered = list;
    if (this.selectedStatus !== null) {
      filtered = filtered.filter(a => a.status === this.selectedStatus);
    }
    if (query) {
      filtered = filtered.filter(a =>
        a.customerName.toLowerCase().includes(query) ||
        a.appointmentNumber.toLowerCase().includes(query) ||
        (a.customerPhone && a.customerPhone.includes(query))
      );
    }

    if (filtered.length === 0) return [];

    interface AptInterval {
      apt: AppointmentDto;
      startMins: number;
      endMins: number;
    }

    const intervals: AptInterval[] = filtered.map(apt => {
      const startMins = this.parseTimeToMinutes(apt.startTime);
      let endMins = this.parseTimeToMinutes(apt.endTime);
      if (endMins <= startMins) {
        endMins = startMins + 30;
      }
      return { apt, startMins, endMins };
    });

    // Sort by start time ascending, then longer duration first
    intervals.sort((a, b) => {
      if (a.startMins !== b.startMins) return a.startMins - b.startMins;
      return (b.endMins - b.startMins) - (a.endMins - a.startMins);
    });

    // Group into overlapping clusters
    const clusters: AptInterval[][] = [];
    let currentCluster: AptInterval[] = [];
    let clusterMaxEnd = -1;

    for (const item of intervals) {
      if (currentCluster.length === 0) {
        currentCluster.push(item);
        clusterMaxEnd = item.endMins;
      } else if (item.startMins < clusterMaxEnd) {
        currentCluster.push(item);
        if (item.endMins > clusterMaxEnd) {
          clusterMaxEnd = item.endMins;
        }
      } else {
        clusters.push(currentCluster);
        currentCluster = [item];
        clusterMaxEnd = item.endMins;
      }
    }
    if (currentCluster.length > 0) {
      clusters.push(currentCluster);
    }

    // Allocate sub-columns per cluster
    const results: PositionedAppointment[] = [];

    for (const cluster of clusters) {
      const colEndMins: number[] = [];
      const colAssignments: { item: AptInterval; colIndex: number }[] = [];

      for (const item of cluster) {
        let placedCol = -1;
        for (let c = 0; c < colEndMins.length; c++) {
          if (colEndMins[c] <= item.startMins) {
            placedCol = c;
            colEndMins[c] = item.endMins;
            break;
          }
        }
        if (placedCol === -1) {
          placedCol = colEndMins.length;
          colEndMins.push(item.endMins);
        }
        colAssignments.push({ item, colIndex: placedCol });
      }

      const totalCols = Math.max(1, colEndMins.length);

      for (const { item, colIndex } of colAssignments) {
        const offsetMins = item.startMins - (startHourVal * 60);
        const durationMins = Math.max(30, item.endMins - item.startMins);

        // Option 3: Symmetric vertical gutter for floating centered alignment in the time slot(s)
        const verticalGutter = 5;
        const rawTop = (offsetMins / 30) * this.slotHeight;
        const rawHeight = (durationMins / 30) * this.slotHeight;

        const top = rawTop + verticalGutter;
        const height = rawHeight - (verticalGutter * 2);

        let leftPercent = 0;
        let widthPercent = 100;

        if (totalCols > 1) {
          const colWidth = 100 / totalCols;
          leftPercent = colIndex * colWidth;
          widthPercent = colWidth - 1.5;
        }

        const cardTheme = this.getAppointmentTheme(item.apt);

        results.push({
          appointment: item.apt,
          top: Math.max(0, top),
          height: Math.max(38, height),
          leftPercent,
          widthPercent,
          zIndex: 10 + colIndex,
          theme: cardTheme
        });
      }
    }

    return results;
  }

  // Appointment Themes: Organic Sage, Emerald, Ocean Teal, Warm Amber, Soft Rose
  getAppointmentTheme(apt: AppointmentDto): ProviderTheme {
    const themes: ProviderTheme[] = [
      {
        name: 'Forest Emerald',
        dot: '#1e5142',
        cardBg: '#eef7f2',
        cardBorder: '#c3ded1',
        textColor: '#183b32',
        serviceColor: '#2d6a56',
        timeTagBg: 'transparent',
        timeTagText: '#5b756c'
      },
      {
        name: 'Ocean Teal',
        dot: '#0d9488',
        cardBg: '#f0fdfa',
        cardBorder: '#99f6e4',
        textColor: '#134e4a',
        serviceColor: '#0f766e',
        timeTagBg: 'transparent',
        timeTagText: '#4a7b75'
      },
      {
        name: 'Sky Azure',
        dot: '#0284c7',
        cardBg: '#f0f7ff',
        cardBorder: '#bae6fd',
        textColor: '#0c4a6e',
        serviceColor: '#0369a1',
        timeTagBg: 'transparent',
        timeTagText: '#537894'
      },
      {
        name: 'Warm Amber',
        dot: '#d97706',
        cardBg: '#fffdf5',
        cardBorder: '#fde68a',
        textColor: '#183b32',
        serviceColor: '#b45309',
        timeTagBg: 'transparent',
        timeTagText: '#78350f'
      },
      {
        name: 'Soft Sage',
        dot: '#10b981',
        cardBg: '#f0f9f5',
        cardBorder: '#bee3d2',
        textColor: '#183b32',
        serviceColor: '#059669',
        timeTagBg: 'transparent',
        timeTagText: '#5b756c'
      },
      {
        name: 'Soft Rose',
        dot: '#e11d48',
        cardBg: '#fff1f2',
        cardBorder: '#fecdd3',
        textColor: '#183b32',
        serviceColor: '#be123c',
        timeTagBg: 'transparent',
        timeTagText: '#7e4b58'
      }
    ];
    const index = Math.abs((apt.id * 31 + apt.serviceProviderId * 17) % themes.length);
    return themes[index];
  }

  isStatusConfirmed(status: AppointmentStatus): boolean {
    return status === AppointmentStatus.Approved || status === AppointmentStatus.Completed || (status as any) === 2 || (status as any) === 3;
  }

  isStatusBooked(status: AppointmentStatus): boolean {
    return status === AppointmentStatus.Booked || (status as any) === 1;
  }

  isStatusPending(status: AppointmentStatus): boolean {
    return status === AppointmentStatus.Pending || (status as any) === 0;
  }

  isStatusCancelled(status: AppointmentStatus): boolean {
    return status === AppointmentStatus.Cancelled || (status as any) === 4;
  }

  getAppointmentStatusDotColor(status: AppointmentStatus): string {
    switch (status) {
      case AppointmentStatus.Approved:
      case AppointmentStatus.Completed:
        return '#10b981'; // vibrant emerald green
      case AppointmentStatus.Pending:
        return '#f59e0b'; // amber / warm golden yellow
      case AppointmentStatus.Booked:
        return '#3b82f6'; // blue
      case AppointmentStatus.Cancelled:
        return '#ef4444'; // red
      default:
        return '#10b981';
    }
  }

  formatAppointmentTimeRange(startTime: string, endTime: string): string {
    const startFormatted = this.formatTimeTo12Hour(startTime);
    const endFormatted = this.formatTimeTo12Hour(endTime);
    return `${startFormatted} – ${endFormatted}`;
  }

  formatTimeTo12Hour(timeStr?: string | null): string {
    if (!timeStr) return '';
    if (timeStr.includes('AM') || timeStr.includes('PM')) return timeStr;
    const parts = timeStr.split(':').map(Number);
    const h24 = parts[0] || 0;
    const m = parts[1] || 0;
    const period = h24 >= 12 ? 'PM' : 'AM';
    let h12 = h24 % 12;
    if (h12 === 0) h12 = 12;
    const mStr = String(m).padStart(2, '0');
    const hStr = String(h12).padStart(2, '0');
    return `${hStr}:${mStr} ${period}`;
  }

  getProviderTheme(providerId: number): ProviderTheme {
    const index = Math.abs(providerId) % this.providerThemes.length;
    return this.providerThemes[index];
  }

  private parseTimeToMinutes(timeStr: string): number {
    if (!timeStr) return this.startHour() * 60;
    const parts = timeStr.split(':').map(Number);
    return (parts[0] || 0) * 60 + (parts[1] || 0);
  }

  private formatMinutesTo12Hour(totalMins: number): string {
    const h24 = Math.floor(totalMins / 60) % 24;
    const m = totalMins % 60;
    const period = h24 >= 12 ? 'PM' : 'AM';
    let h12 = h24 % 12;
    if (h12 === 0) h12 = 12;
    const mStr = String(m).padStart(2, '0');
    const hStr = String(h12).padStart(2, '0');
    return `${hStr}:${mStr} ${period}`;
  }

  private updateCurrentTimeIndicator(): void {
    const currentMins = this.getIndianCurrentMinutes();
    const startMins = this.startHour() * 60;
    const endMins = this.endHour() * 60;

    this.currentLiveTimeDisplay.set(this.formatMinutesTo12Hour(currentMins));

    if (currentMins >= startMins && currentMins <= endMins) {
      const offsetMins = currentMins - startMins;
      const top = (offsetMins / 30) * this.slotHeight;
      this.currentTimeTop.set(top);
    } else {
      this.currentTimeTop.set(null);
    }
  }

  private getIsoWeekNumber(d: Date): number {
    const date = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()));
    const dayNum = date.getUTCDay() || 7;
    date.setUTCDate(date.getUTCDate() + 4 - dayNum);
    const yearStart = new Date(Date.UTC(date.getUTCFullYear(), 0, 1));
    return Math.ceil((((date.getTime() - yearStart.getTime()) / 86400000) + 1) / 7);
  }

  // --- Navigation Controls ---
  prevDate(): void {
    const parts = this.selectedDate().split('-').map(Number);
    const d = new Date(parts[0], parts[1] - 1, parts[2]);
    if (this.activeView() === 'month') {
      d.setMonth(d.getMonth() - 1);
    } else if (this.activeView() === 'week') {
      d.setDate(d.getDate() - 7);
    } else {
      d.setDate(d.getDate() - 1);
    }
    const newDateStr = this.formatDateToString(d);
    this.selectedDate.set(newDateStr);
    this.fetchAppointments();

    if (this.fullCalendarComponent) {
      this.fullCalendarComponent.getApi().gotoDate(newDateStr);
    }
  }

  nextDate(): void {
    const parts = this.selectedDate().split('-').map(Number);
    const d = new Date(parts[0], parts[1] - 1, parts[2]);
    if (this.activeView() === 'month') {
      d.setMonth(d.getMonth() + 1);
    } else if (this.activeView() === 'week') {
      d.setDate(d.getDate() + 7);
    } else {
      d.setDate(d.getDate() + 1);
    }
    const newDateStr = this.formatDateToString(d);
    this.selectedDate.set(newDateStr);
    this.fetchAppointments();

    if (this.fullCalendarComponent) {
      this.fullCalendarComponent.getApi().gotoDate(newDateStr);
    }
  }

  goToToday(): void {
    const today = this.getIndianToday();
    this.selectedDate.set(today);
    this.fetchAppointments();

    if (this.fullCalendarComponent) {
      this.fullCalendarComponent.getApi().gotoDate(today);
    }
  }

  setView(view: 'day' | 'week' | 'month'): void {
    const popoverClose = document.querySelector('.fc-popover-close') as HTMLElement | null;
    if (popoverClose) {
      popoverClose.click();
    }
    this.activeView.set(view);
    if (view === 'week' || view === 'month') {
      this.mapAppointmentsToCalendar(this.allAppointments());
      const fcView = view === 'week' ? 'timeGridWeek' : 'dayGridMonth';
      this.calendarOptions = {
        ...this.calendarOptions,
        initialView: fcView
      };

      const triggerCalendarSync = () => {
        if (this.fullCalendarComponent) {
          const api = this.fullCalendarComponent.getApi();
          if (api) {
            if (api.view.type !== fcView) {
              api.changeView(fcView);
            }
            api.gotoDate(this.selectedDate());
            api.updateSize();
          }
        }
      };

      // Staggered triggers to handle DOM mounting and CSS layout completion
      setTimeout(triggerCalendarSync, 20);
      setTimeout(triggerCalendarSync, 100);
      setTimeout(triggerCalendarSync, 250);
    }
  }

  formatDateToString(d: Date): string {
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (!target.closest('.provider-select-anchor') && !target.closest('.employees-filter-wrapper')) {
      this.isProviderDropdownOpen = false;
      this.isEmployeeDropdownOpen = false;
      this.specialistSearchQuery.set('');
    }
  }

  @HostListener('document:keydown.escape', ['$event'])
  onEscapeKey(event: KeyboardEvent): void {
    const popoverClose = document.querySelector('.fc-popover-close') as HTMLElement | null;
    if (popoverClose) {
      event.preventDefault();
      popoverClose.click();
    }

    const openDrawer = document.querySelector('.fc-week-stack-container.drawer-open') as HTMLElement | null;
    if (openDrawer) {
      const drawer = openDrawer.querySelector('.fc-week-overflow-drawer') as HTMLElement | null;
      if (drawer) {
        drawer.classList.add('is-closing');
        setTimeout(() => {
          drawer.classList.remove('is-closing');
          openDrawer.classList.remove('drawer-open');
        }, 210);
      } else {
        openDrawer.classList.remove('drawer-open');
      }
    }
  }

  private handlePopoverDismiss = (event: MouseEvent): void => {
    const target = event.target as HTMLElement | null;
    if (!target) return;

    // --- Week View: Clicked "+X more" button ---
    const moreBtn = target.closest('.fc-week-more-btn');
    if (moreBtn) {
      event.preventDefault();
      event.stopPropagation();
      const container = moreBtn.closest('.fc-week-stack-container') as HTMLElement | null;
      if (container) {
        const isOpen = container.classList.contains('drawer-open');
        // Close other open drawers first
        document.querySelectorAll('.fc-week-stack-container.drawer-open').forEach(el => {
          el.classList.remove('drawer-open');
        });
        if (!isOpen) {
          container.classList.add('drawer-open');
        }
      }
      return;
    }

    // --- Week View: Clicked drawer close '✕' button ---
    const drawerClose = target.closest('.fc-overflow-drawer-close');
    if (drawerClose) {
      event.preventDefault();
      event.stopPropagation();
      const drawer = drawerClose.closest('.fc-week-overflow-drawer') as HTMLElement | null;
      const container = drawerClose.closest('.fc-week-stack-container') as HTMLElement | null;
      if (drawer && container) {
        drawer.classList.add('is-closing');
        setTimeout(() => {
          drawer.classList.remove('is-closing');
          container.classList.remove('drawer-open');
        }, 210);
      }
      return;
    }

    // --- Week View: Clicked outside open drawer ---
    const openDrawer = document.querySelector('.fc-week-stack-container.drawer-open') as HTMLElement | null;
    if (openDrawer && !openDrawer.contains(target)) {
      const drawer = openDrawer.querySelector('.fc-week-overflow-drawer') as HTMLElement | null;
      if (drawer) {
        drawer.classList.add('is-closing');
        setTimeout(() => {
          drawer.classList.remove('is-closing');
          openDrawer.classList.remove('drawer-open');
        }, 210);
      } else {
        openDrawer.classList.remove('drawer-open');
      }
    }
  };

  // Provider Dropdown Controls (Multi-Select)
  toggleProviderDropdown(event: Event): void {
    event.stopPropagation();
    this.isProviderDropdownOpen = !this.isProviderDropdownOpen;
    if (!this.isProviderDropdownOpen) {
      this.specialistSearchQuery.set('');
    }
  }

  isProviderSelected(providerId: number): boolean {
    return this.selectedProviderIds().includes(providerId);
  }

  toggleProvider(providerId: number, event: Event): void {
    event.stopPropagation();
    const allActive = this.providers().filter(p => p.status === ProviderStatus.Active || (p.status as any) === 1);
    const allActiveIds = allActive.map(p => p.id);
    const current = [...this.selectedProviderIds()];

    if (current.length === 0) {
      // Transition from "All" to isolating one specialist
      this.selectedProviderIds.set([providerId]);
      this.selectedProviderId.set(providerId);
    } else {
      const idx = current.indexOf(providerId);
      if (idx >= 0) {
        current.splice(idx, 1);
      } else {
        current.push(providerId);
      }

      if (current.length === 0 || current.length >= allActiveIds.length) {
        this.selectedProviderIds.set([]);
        this.selectedProviderId.set(null);
      } else {
        this.selectedProviderIds.set(current);
        this.selectedProviderId.set(current.length === 1 ? current[0] : null);
      }
    }

    this.mapAppointmentsToCalendar(this.allAppointments());
  }

  selectAllProviders(event?: Event): void {
    if (event) event.stopPropagation();
    this.selectedProviderIds.set([]);
    this.selectedProviderId.set(null);
    this.mapAppointmentsToCalendar(this.allAppointments());
  }

  clearProviderSelection(event: Event): void {
    event.stopPropagation();
    this.selectedProviderIds.set([]);
    this.selectedProviderId.set(null);
    this.mapAppointmentsToCalendar(this.allAppointments());
  }

  selectProvider(id: number | null, event: Event): void {
    if (id === null) {
      this.selectAllProviders(event);
    } else {
      this.toggleProvider(id, event);
    }
  }

  // Legacy employee filter fallback
  toggleEmployeeFilter(providerId: number): void {
    this.toggleProvider(providerId, new CustomEvent('toggle'));
  }

  clearProviderFilter(): void {
    this.selectedProviderIds.set([]);
    this.selectedProviderId.set(null);
    this.mapAppointmentsToCalendar(this.allAppointments());
  }

  setMobileProvider(providerId: number): void {
    this.selectedMobileProviderId.set(providerId);
  }

  // --- Slot Booking Action ---
  onSlotClick(provider: ServiceProviderDto, hourStr: string, event: MouseEvent): void {
    event.stopPropagation();
    if (this.justPanned || this.isPanningGrid || this.isResizingColumn) {
      return;
    }

    // 0. Guard: Slot is in the past
    if (this.isSlotInPast(hourStr)) {
      this.errorMessage.set('Cannot book appointments for past time slots.');
      setTimeout(() => this.errorMessage.set(''), 4000);
      return;
    }

    // 1. Guard: Provider is on Day Off / Not Available
    if (!this.isProviderAvailableToday(provider.id)) {
      this.errorMessage.set(`${provider.name} is on Day Off today (${this.getSelectedDayName()}). Booking is not available.`);
      setTimeout(() => this.errorMessage.set(''), 4000);
      return;
    }

    // 2. Guard: Slot is during lunch / break
    if (this.isSlotBreakForProvider(provider.id, hourStr)) {
      this.errorMessage.set(`${provider.name} is on break/lunch during this time slot.`);
      setTimeout(() => this.errorMessage.set(''), 4000);
      return;
    }

    // 3. Guard: Slot is outside working hours
    if (this.isSlotOutsideWorkingHours(provider.id, hourStr)) {
      this.errorMessage.set(`This slot is outside ${provider.name}'s working hours (${this.getProviderWorkingHoursText(provider.id)}).`);
      setTimeout(() => this.errorMessage.set(''), 4000);
      return;
    }

    // 4. Date restriction: Only allow booking from Today up to 15 days in advance
    if (!this.isDateWithinBookingWindow(this.selectedDate())) {
      const today = this.getIndianToday();
      if (this.selectedDate() < today) {
        this.errorMessage.set('Cannot book appointments for past dates.');
      } else {
        this.errorMessage.set('Appointments can only be scheduled from today up to 15 days in advance.');
      }
      setTimeout(() => this.errorMessage.set(''), 4500);
      return;
    }
    this.bookingPrefillProviderId = provider.id;
    this.bookingPrefillDate = this.selectedDate();
    this.isBookingModalOpen.set(true);
  }

  onDayOffClick(provider: ServiceProviderDto, event: MouseEvent): void {
    event.stopPropagation();
    this.errorMessage.set(`${provider.name} is on Day Off today (${this.getSelectedDayName()}). Booking is not available.`);
    setTimeout(() => this.errorMessage.set(''), 4000);
  }

  openNewBooking(): void {
    if (!this.isDateWithinBookingWindow(this.selectedDate())) {
      const today = this.getIndianToday();
      if (this.selectedDate() < today) {
        this.errorMessage.set('Cannot book appointments for past dates. Defaulted to today.');
      } else {
        this.errorMessage.set('Appointments can only be scheduled up to 15 days in advance. Defaulted to today.');
      }
      setTimeout(() => this.errorMessage.set(''), 4500);
      this.bookingPrefillDate = this.getIndianToday();
    } else {
      this.bookingPrefillDate = this.selectedDate();
    }
    this.bookingPrefillProviderId = null;
    this.isBookingModalOpen.set(true);
  }

  onBookingClosed(): void {
    this.isBookingModalOpen.set(false);
  }

  onAppointmentBooked(newApt: AppointmentDto): void {
    this.isBookingModalOpen.set(false);
    this.showSuccessNotification(`Appointment #${newApt.appointmentNumber} created!`);
    this.fetchAppointments();
    this.appointmentChanged.emit();
  }

  // --- Appointment Detail & Action Modals ---
  viewAppointment(apt: AppointmentDto, event?: MouseEvent): void {
    if (event) {
      event.preventDefault();
      event.stopPropagation();
    }
    this.selectedAppointmentForDetail.set(apt);
  }

  closeDetails(): void {
    this.selectedAppointmentForDetail.set(null);
  }

  openRescheduleModal(apt: AppointmentDto): void {
    this.selectedAppointmentForReschedule.set(apt);
  }

  closeRescheduleModal(): void {
    this.selectedAppointmentForReschedule.set(null);
  }

  onRescheduled(updatedApt: AppointmentDto): void {
    this.closeRescheduleModal();
    this.selectedAppointmentForDetail.set(null);
    this.showSuccessNotification(`Appointment #${updatedApt.appointmentNumber} rescheduled successfully.`);
    this.fetchAppointments();
    this.appointmentChanged.emit();
  }

  openCancelModal(apt: AppointmentDto): void {
    this.selectedAppointmentForCancel.set(apt);
  }

  closeCancelModal(): void {
    this.selectedAppointmentForCancel.set(null);
  }

  onCancelled(cancelledApt: AppointmentDto): void {
    this.closeCancelModal();
    this.selectedAppointmentForDetail.set(null);
    this.showSuccessNotification(`Appointment #${cancelledApt.appointmentNumber} cancelled.`);
    this.fetchAppointments();
    this.appointmentChanged.emit();
  }

  approveAppointment(apt: AppointmentDto): void {
    this.appointmentService.updateStatus(apt.id, { status: AppointmentStatus.Approved }).subscribe({
      next: () => {
        this.showSuccessNotification(`Appointment #${apt.appointmentNumber} approved.`);
        this.selectedAppointmentForDetail.set(null);
        this.fetchAppointments();
        this.appointmentChanged.emit();
      },
      error: (err) => {
        this.errorMessage.set(err.error?.message || 'Failed to approve appointment.');
        setTimeout(() => this.errorMessage.set(''), 4000);
      }
    });
  }

  getStatusLabel(status: AppointmentStatus): string {
    return APPOINTMENT_STATUS_MAP[status]?.label || 'Booked';
  }

  getAppointmentStatusLabel(apt: AppointmentDto): string {
    if (apt.statusText) {
      const lower = apt.statusText.toLowerCase();
      if (lower === 'approved' || lower === 'completed') return 'Confirmed';
      return apt.statusText;
    }
    if (this.isStatusConfirmed(apt.status)) return 'Confirmed';
    if (this.isStatusPending(apt.status)) return 'Pending';
    return 'Booked';
  }

  getStatusBadgeClass(status: AppointmentStatus): string {
    return APPOINTMENT_STATUS_MAP[status]?.badgeClass || 'badge-booked';
  }

  private showSuccessNotification(msg: string): void {
    this.successMessage.set(msg);
    setTimeout(() => this.successMessage.set(''), 4500);
  }
}
