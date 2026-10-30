import { Component, EventEmitter, Input, OnInit, Output, computed, inject, signal, ChangeDetectorRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router } from '@angular/router';
import { AppointmentService } from '../../../core/services/appointment.service';
import { ProviderService } from '../../../core/services/provider.service';
import { AuthService } from '../../../core/services/auth.service';
import { ServiceProviderDto } from '../../../core/models/provider.models';
import {
  AvailableSlotDto,
  BookAppointmentRequestDto,
  AppointmentDto,
  ProviderDailyAvailabilityResponseDto
} from '../../../core/models/appointment.models';
import { UserDto } from '../../../core/models/auth.models';
import { formatDatePretty, formatTo12Hour, getInitials } from '../../../shared/utils/formatters';

import { OnlyDigitsDirective, CopyToClipboardDirective } from '../../../shared/directives';
import { isValidEmail, isValidPhone, filter10Digits } from '../../../core/constants/app.constants';
import { ConfirmService } from '../../../shared/components/confirmation-modal/confirm.service';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { provideNativeDateAdapter } from '@angular/material/core';

export interface CalendarDay {
  date: Date;
  dateStr: string;     // YYYY-MM-DD
  dayNumber: number;   // 1, 2, ... 31
  isCurrentMonth: boolean;
  isPast: boolean;     // before minDate
  isBeyondLimit: boolean; // after maxDate (15 days)
  isToday: boolean;
  isSelected: boolean;
}

@Component({
  selector: 'app-booking-modal',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterModule,
    MatDatepickerModule,
    OnlyDigitsDirective,
    CopyToClipboardDirective
  ],
  providers: [
    provideNativeDateAdapter()
  ],
  templateUrl: './booking-modal.component.html',
  styleUrls: ['./booking-modal.component.css']
})
export class BookingModalComponent implements OnInit {
  private readonly appointmentService = inject(AppointmentService);
  private readonly providerService = inject(ProviderService);
  readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly confirmService = inject(ConfirmService);
  private readonly cdr = inject(ChangeDetectorRef);

  @Input() initialProviderId: number | null = null;
  @Input() initialDate?: string;
  @Input() tenantId?: number;
  @Output() close = new EventEmitter<void>();
  @Output() booked = new EventEmitter<AppointmentDto>();

  // State Signals
  readonly providers = signal<ServiceProviderDto[]>([]);
  readonly isLoadingProviders = signal<boolean>(false);
  readonly isLoadingSlots = signal<boolean>(false);
  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string>('');
  readonly confirmedAppointment = signal<AppointmentDto | null>(null);
  readonly showAuthPrompt = signal<boolean>(false);
  copiedRef: boolean = false;
  linkedCustomer: any = null;
  readonly isLookingUpCustomer = signal<boolean>(false);
  readonly isCustomerNotFound = signal<boolean>(false);

  // Stepper State:
  // 1 = Select Specialist
  // 2 = Side-by-Side: Month Calendar Date Picker (Left) + Available Time Slots (Right)
  // 3 = Customer Info (logged-in detection & edit mode)
  // 4 = Review & Confirmation
  // 5 = Success / Confirmed Ticket
  currentStep: 1 | 2 | 3 | 4 | 5 = 1;
  returnToStep: number | null = null;

  // Selected Specialist State
  selectedProviderId: number | null = null;
  selectedProvider: ServiceProviderDto | null = null;
  providerSearchQuery = signal<string>('');

  // Selected Date & Interactive Calendar State (Monday-based week)
  selectedDate: string = ''; // YYYY-MM-DD
  minDate: string = '';
  maxDate: string = '';
  currentMonthDate: Date = new Date();
  readonly weekdays: string[] = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

  // Slots State
  availableSlots: AvailableSlotDto[] = [];
  selectedSlot: AvailableSlotDto | null = null;
  isWorkingDay = true;
  workingHours = '';

  // Customer Form Model
  customerName = '';
  customerEmail = '';
  customerPhone = '';
  customerAddress = '';
  bookingNotes = '';

  // Logged-in Customer & Staff state
  isLoggedInUser = signal<boolean>(false);
  readonly isAdmin = computed(() => this.authService.isAdmin() || this.authService.isSuperAdmin());
  readonly isStaffOrAdmin = computed(() => 
    this.authService.isAdmin() || this.authService.isSuperAdmin() || this.authService.isServiceProvider()
  );

  // Smart Customer Search State (Ideal Real-world Autocomplete for Staff & Admins)
  readonly customerSearchQuery = signal<string>('');
  readonly customerSearchResults = signal<UserDto[]>([]);
  readonly isSearchingCustomers = signal<boolean>(false);
  readonly showSearchDropdown = signal<boolean>(false);
  private searchDebounceTimer: any = null;

  // Service Type Filtering (Custom Dropdown matching Calendar)
  selectedServiceType = signal<string>('ALL');
  isServiceDropdownOpen = false;
  serviceSearchQuery = signal<string>('');

  readonly serviceTypes = computed<string[]>(() => {
    const types = new Set<string>();
    this.providers().forEach(p => {
      if (p.serviceType && p.serviceType.trim()) {
        types.add(p.serviceType.trim());
      }
    });
    return Array.from(types).sort();
  });

  readonly filteredServiceTypes = computed<string[]>(() => {
    const query = this.serviceSearchQuery().trim().toLowerCase();
    const list = this.serviceTypes();
    if (!query) return list;
    return list.filter(t => t.toLowerCase().includes(query));
  });

  readonly selectedServiceLabel = computed<string>(() => {
    const selected = this.selectedServiceType();
    return selected === 'ALL' ? 'All Services' : selected;
  });

  // Computed Filtered Providers (Search + Service Type Dropdown)
  readonly filteredProviders = computed<ServiceProviderDto[]>(() => {
    let list = this.providers();
    const typeFilter = this.selectedServiceType();
    if (typeFilter && typeFilter !== 'ALL') {
      list = list.filter(p => p.serviceType?.toLowerCase() === typeFilter.toLowerCase());
    }
    const query = this.providerSearchQuery().trim().toLowerCase();
    if (!query) return list;
    return list.filter(p =>
      p.name.toLowerCase().includes(query) ||
      (p.serviceType && p.serviceType.toLowerCase().includes(query)) ||
      (p.bio && p.bio.toLowerCase().includes(query)) ||
      (p.address && p.address.toLowerCase().includes(query))
    );
  });

  get progressPercentage(): number {
    switch (this.currentStep) {
      case 1: return 20;
      case 2: return 50;
      case 3: return 75;
      case 4: return 90;
      case 5: return 100;
      default: return 100;
    }
  }

  // --- Calendar Date Picker Getters & Methods ---
  get currentMonthLabel(): string {
    return this.currentMonthDate.toLocaleDateString('en-US', {
      month: 'long',
      year: 'numeric'
    });
  }

  get canGoPrevMonth(): boolean {
    const today = new Date();
    return (
      this.currentMonthDate.getFullYear() > today.getFullYear() ||
      (this.currentMonthDate.getFullYear() === today.getFullYear() &&
        this.currentMonthDate.getMonth() > today.getMonth())
    );
  }

  prevMonth(): void {
    if (!this.canGoPrevMonth) return;
    this.currentMonthDate = new Date(
      this.currentMonthDate.getFullYear(),
      this.currentMonthDate.getMonth() - 1,
      1
    );
  }

  get canGoNextMonth(): boolean {
    const nextMonthFirst = new Date(
      this.currentMonthDate.getFullYear(),
      this.currentMonthDate.getMonth() + 1,
      1
    );
    return this.formatDateIso(nextMonthFirst) <= this.maxDate;
  }

  nextMonth(): void {
    if (!this.canGoNextMonth) return;
    this.currentMonthDate = new Date(
      this.currentMonthDate.getFullYear(),
      this.currentMonthDate.getMonth() + 1,
      1
    );
  }

  // Monday-based calendar days (Mon=0, Tue=1, ..., Sun=6)
  get calendarDays(): CalendarDay[] {
    const year = this.currentMonthDate.getFullYear();
    const month = this.currentMonthDate.getMonth();

    // In JS: Sun=0, Mon=1, ..., Sat=6. Convert to Mon=0, ..., Sun=6
    const firstDayIndex = (new Date(year, month, 1).getDay() + 6) % 7;
    const daysInMonth = new Date(year, month + 1, 0).getDate();
    const daysInPrevMonth = new Date(year, month, 0).getDate();

    const days: CalendarDay[] = [];

    // Leading days from previous month
    for (let i = firstDayIndex - 1; i >= 0; i--) {
      const dayNum = daysInPrevMonth - i;
      const prevDate = new Date(year, month - 1, dayNum);
      const dateStr = this.formatDateIso(prevDate);
      days.push({
        date: prevDate,
        dateStr,
        dayNumber: dayNum,
        isCurrentMonth: false,
        isPast: true,
        isBeyondLimit: false,
        isToday: false,
        isSelected: false
      });
    }

    // Days in current month
    for (let dayNum = 1; dayNum <= daysInMonth; dayNum++) {
      const d = new Date(year, month, dayNum);
      const dateStr = this.formatDateIso(d);
      const isPast = dateStr < this.minDate;
      const isBeyondLimit = dateStr > this.maxDate;
      const isToday = dateStr === this.minDate;
      const isSelected = dateStr === this.selectedDate;

      days.push({
        date: d,
        dateStr,
        dayNumber: dayNum,
        isCurrentMonth: true,
        isPast,
        isBeyondLimit,
        isToday,
        isSelected
      });
    }

    // Trailing days to fill the row (multiple of 7)
    const remaining = (7 - (days.length % 7)) % 7;
    for (let dayNum = 1; dayNum <= remaining; dayNum++) {
      const nextDate = new Date(year, month + 1, dayNum);
      const dateStr = this.formatDateIso(nextDate);
      days.push({
        date: nextDate,
        dateStr,
        dayNumber: dayNum,
        isCurrentMonth: false,
        isPast: false,
        isBeyondLimit: true,
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
    if (!this.selectedDate) return new Date();
    const parts = this.selectedDate.split('-').map(Number);
    if (parts.length === 3) {
      return new Date(parts[0], parts[1] - 1, parts[2], 12, 0, 0);
    }
    return new Date();
  }

  onMaterialDateSelected(date: Date | null): void {
    if (!date) return;
    const dateStr = this.formatDateIso(date);
    if (dateStr < this.minDate || dateStr > this.maxDate) return;
    this.selectedDate = dateStr;
    this.selectedSlot = null;
    this.errorMessage.set('');
    this.fetchSlots();
  }

  formatDateIso(d: Date): string {
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  selectCalendarDay(day: CalendarDay): void {
    if (day.isPast || day.isBeyondLimit || !day.isCurrentMonth) return;
    this.selectedDate = day.dateStr;
    this.selectedSlot = null;
    this.errorMessage.set('');
    this.fetchSlots();
  }

  // --- Component Lifecycle ---
  ngOnInit(): void {
    const today = new Date();
    this.minDate = this.formatDateIso(today);
    const maxDateObj = new Date(today);
    maxDateObj.setDate(maxDateObj.getDate() + 15);
    this.maxDate = this.formatDateIso(maxDateObj);

    if (this.initialDate && this.initialDate >= this.minDate && this.initialDate <= this.maxDate) {
      this.selectedDate = this.initialDate;
    } else {
      this.selectedDate = this.minDate;
    }
    const dateObj = this.initialDate ? new Date(this.initialDate) : today;
    this.currentMonthDate = new Date(dateObj.getFullYear(), dateObj.getMonth(), 1);

    if (!this.authService.isInitialized()) {
      this.authService.checkAuth().subscribe(() => {
        this.checkUserAuth();
        this.checkAndResumeDraft();
      });
    } else {
      this.checkUserAuth();
      this.checkAndResumeDraft();
    }

    // Load active providers
    this.loadProviders();
  }

  checkAndResumeDraft(): void {
    // Only resume draft if user has completed authentication!
    if (!this.authService.isAuthenticated()) {
      return;
    }

    const draftRaw = sessionStorage.getItem('pending_booking_draft');
    if (!draftRaw) return;

    try {
      const draft = JSON.parse(draftRaw);
      if (draft && draft.providerId && draft.slot) {
        this.selectedProviderId = draft.providerId;
        this.selectedProvider = draft.provider || null;
        this.selectedDate = draft.selectedDate || this.minDate;
        this.selectedSlot = draft.slot;
        this.currentStep = 3;
      }
    } catch {
      sessionStorage.removeItem('pending_booking_draft');
    }
  }

  checkUserAuth(): void {
    const user = this.authService.currentUser();
    // Only autofill customer fields if the logged-in user is a Customer booking for themselves!
    if (user && this.authService.isCustomer()) {
      this.isLoggedInUser.set(true);
      this.customerName = user.fullName || `${user.firstName || ''} ${user.lastName || ''}`.trim();
      this.customerEmail = user.email || '';
      this.customerPhone = filter10Digits(user.phoneNumber || '');
      this.customerAddress = user.address || '';
    } else {
      // If Admin, SuperAdmin, ServiceProvider, or Guest: DO NOT autofill staff's own details!
      this.isLoggedInUser.set(false);
      this.customerName = '';
      this.customerEmail = '';
      this.customerPhone = '';
      this.customerAddress = '';
    }
  }

  // --- Smart Customer Search (Ideal Real-world Autocomplete for Staff & Admins) ---
  onCustomerSearchInput(query: string): void {
    this.customerSearchQuery.set(query);
    if (this.searchDebounceTimer) {
      clearTimeout(this.searchDebounceTimer);
    }
    const trimmed = query.trim();
    if (!trimmed || trimmed.length < 2) {
      this.customerSearchResults.set([]);
      this.showSearchDropdown.set(false);
      this.isSearchingCustomers.set(false);
      return;
    }

    this.isSearchingCustomers.set(true);
    this.showSearchDropdown.set(true);

    this.searchDebounceTimer = setTimeout(() => {
      this.appointmentService.searchCustomers(trimmed, this.tenantId).subscribe({
        next: (results) => {
          this.isSearchingCustomers.set(false);
          this.customerSearchResults.set(results || []);
          this.showSearchDropdown.set(true);
          this.cdr.markForCheck();
        },
        error: () => {
          this.isSearchingCustomers.set(false);
          this.customerSearchResults.set([]);
          this.cdr.markForCheck();
        }
      });
    }, 280);
  }

  selectCustomerFromSearch(cust: UserDto): void {
    this.linkedCustomer = cust;
    this.isCustomerNotFound.set(false);
    this.customerName = (cust.fullName || `${cust.firstName || ''} ${cust.lastName || ''}`).trim();
    if (cust.email) {
      this.customerEmail = cust.email;
    }
    if (cust.phoneNumber) {
      this.customerPhone = filter10Digits(cust.phoneNumber);
    }
    if (cust.address) {
      this.customerAddress = cust.address;
    } else if (cust.city || cust.state) {
      this.customerAddress = [cust.city, cust.state].filter(Boolean).join(', ');
    } else {
      this.customerAddress = '';
    }
    this.customerSearchQuery.set('');
    this.customerSearchResults.set([]);
    this.showSearchDropdown.set(false);
    this.cdr.markForCheck();
  }

  closeSearchDropdown(): void {
    setTimeout(() => {
      this.showSearchDropdown.set(false);
    }, 250);
  }

  onPhoneInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input) {
      this.customerPhone = filter10Digits(input.value);
      if (this.isStaffOrAdmin() && this.customerPhone.length === 10) {
        this.lookupCustomerByQuery(this.customerPhone);
      } else if (this.linkedCustomer && this.customerPhone.length < 10) {
        this.linkedCustomer = null;
        this.isCustomerNotFound.set(false);
      }
    }
  }

  onEmailBlur(): void {
    if (this.isStaffOrAdmin() && !this.linkedCustomer && isValidEmail(this.customerEmail)) {
      this.lookupCustomerByQuery(this.customerEmail);
    }
  }

  lookupCustomerByQuery(query: string): void {
    if (!query || query.trim().length < 3) return;
    this.isLookingUpCustomer.set(true);
    this.isCustomerNotFound.set(false);

    this.appointmentService.lookupCustomer(query.trim()).subscribe({
      next: (response) => {
        this.isLookingUpCustomer.set(false);
        // Robust extraction: support both unwrapped object and { success, message, data }
        const cust = (response && response.data !== undefined) ? response.data : response;
        if (cust && (cust.id || cust.email || cust.phoneNumber || cust.fullName || cust.firstName)) {
          this.linkedCustomer = cust;
          this.isCustomerNotFound.set(false);
          this.customerName = (cust.fullName || `${cust.firstName || ''} ${cust.lastName || ''}`).trim();
          if (cust.email) {
            this.customerEmail = cust.email;
          }
          if (cust.phoneNumber) {
            this.customerPhone = filter10Digits(cust.phoneNumber);
          }
          if (cust.address) {
            this.customerAddress = cust.address;
          } else if (cust.city || cust.state) {
            this.customerAddress = [cust.city, cust.state].filter(Boolean).join(', ');
          }
        } else {
          this.linkedCustomer = null;
          this.isCustomerNotFound.set(true);
        }
        this.cdr.markForCheck();
      },
      error: () => {
        this.isLookingUpCustomer.set(false);
        this.linkedCustomer = null;
        this.isCustomerNotFound.set(true);
        this.cdr.markForCheck();
      }
    });
  }

  clearLinkedCustomer(): void {
    this.linkedCustomer = null;
    this.isCustomerNotFound.set(false);
    this.customerName = '';
    this.customerEmail = '';
    this.customerPhone = '';
    this.customerAddress = '';
    this.customerSearchQuery.set('');
    this.customerSearchResults.set([]);
    this.showSearchDropdown.set(false);
    this.cdr.markForCheck();
  }

  loadProviders(): void {
    this.isLoadingProviders.set(true);
    // Public active providers endpoint filtered by tenant if available
    this.providerService.getActiveProviders(this.tenantId).subscribe({
      next: (activeProviders) => {
        const filtered = this.tenantId 
          ? (activeProviders || []).filter(p => p.tenantId === this.tenantId)
          : (activeProviders || []);
        this.providers.set(filtered);
        this.isLoadingProviders.set(false);

        const targetId = this.selectedProviderId || this.initialProviderId;
        if (targetId) {
          const match = filtered.find(p => p.id === Number(targetId));
          if (match) {
            this.selectedProviderId = match.id;
            this.selectedProvider = match;
            if (this.currentStep < 2) {
              this.currentStep = 2; // Jump directly to Calendar + Time Slots!
              this.fetchSlots();
            }
          }
        } else if (this.authService.isServiceProvider()) {
          const currentUser = this.authService.currentUser();
          if (currentUser && currentUser.email) {
            const myProvider = filtered.find(p => p.email.toLowerCase() === currentUser.email.toLowerCase());
            if (myProvider) {
              this.selectedProviderId = myProvider.id;
              this.selectedProvider = myProvider;
              if (this.currentStep < 2) {
                this.currentStep = 2; // Jump directly to Calendar for Service Provider
                this.fetchSlots();
              }
            }
          }
        }
      },
      error: () => {
        this.isLoadingProviders.set(false);
        this.errorMessage.set('Failed to load active service providers.');
      }
    });
  }

  // --- Step 1: Select a Service Provider ---
  selectProviderCard(p: ServiceProviderDto): void {
    this.selectedProviderId = p.id;
    this.selectedProvider = p;
    this.selectedSlot = null;
    this.errorMessage.set('');
  }

  clearProviderSearch(): void {
    this.providerSearchQuery.set('');
    this.selectedServiceType.set('ALL');
    this.serviceSearchQuery.set('');
    this.isServiceDropdownOpen = false;
  }

  toggleServiceDropdown(event: Event): void {
    event.stopPropagation();
    this.isServiceDropdownOpen = !this.isServiceDropdownOpen;
  }

  closeServiceDropdown(): void {
    this.isServiceDropdownOpen = false;
  }

  selectService(st: string, event?: Event): void {
    if (event) event.stopPropagation();
    this.selectedServiceType.set(st);
    this.isServiceDropdownOpen = false;
  }

  clearServiceSelection(event: Event): void {
    event.stopPropagation();
    this.selectService('ALL', event);
  }

  onServiceSearchInput(event: Event): void {
    const val = (event.target as HTMLInputElement).value || '';
    this.serviceSearchQuery.set(val);
  }

  clearServiceSearch(event: Event): void {
    event.stopPropagation();
    this.serviceSearchQuery.set('');
  }

  getProviderCountForService(st: string): number {
    return this.providers().filter(p => p.serviceType?.toLowerCase() === st.toLowerCase()).length;
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    this.isServiceDropdownOpen = false;
    this.showSearchDropdown.set(false);
  }

  goToStep2(): void {
    if (!this.selectedProviderId || !this.selectedProvider) {
      this.errorMessage.set('Please select a service provider to proceed.');
      return;
    }
    this.errorMessage.set('');
    this.fetchSlots();
    this.currentStep = 2;
  }

  // --- Step 2: Available Time Slots (Hide booked/unavailable and past time slots) ---
  fetchSlots(): void {
    if (!this.selectedProviderId || !this.selectedDate) return;

    this.isLoadingSlots.set(true);
    this.errorMessage.set('');

    this.appointmentService.getAvailableSlots(this.selectedProviderId, this.selectedDate).subscribe({
      next: (res: ProviderDailyAvailabilityResponseDto) => {
        if (!res) {
          this.isWorkingDay = false;
          this.workingHours = '';
          this.availableSlots = [];
          this.isLoadingSlots.set(false);
          return;
        }
        this.isWorkingDay = res.isWorkingDay;
        this.workingHours = res.workingHours;

        // Current time threshold to prevent booking past time slots on today's date
        const today = new Date();
        const todayStr = this.formatDateIso(today);
        const currentHours = today.getHours().toString().padStart(2, '0');
        const currentMinutes = today.getMinutes().toString().padStart(2, '0');
        const currentTimeStr = `${currentHours}:${currentMinutes}`;

        // 1. Filter: Hide slots that are already booked, breaks, or unavailable
        let filtered = (res.slots || []).filter(s => s.isAvailable && s.status !== 'Booked' && s.status !== 'Break');

        // 2. Filter: Do not allow past time slots if date is today
        if (this.selectedDate === todayStr) {
          filtered = filtered.filter(s => s.startTime > currentTimeStr);
        }

        this.availableSlots = filtered;
        this.isLoadingSlots.set(false);
      },
      error: (err) => {
        this.isLoadingSlots.set(false);
        this.errorMessage.set(err.error?.message || 'Failed to fetch available time slots for the chosen date.');
      }
    });
  }

  selectSlot(slot: AvailableSlotDto): void {
    if (!slot.isAvailable) return;
    this.selectedSlot = slot;
  }

  goToStep3(): void {
    if (!this.selectedSlot) {
      this.errorMessage.set('Please select an available time slot to continue.');
      return;
    }
    this.errorMessage.set('');

    // If user is neither logged in as a customer nor admin, require authentication first!
    if (!this.authService.isAuthenticated() && !this.isAdmin()) {
      this.showAuthPrompt.set(true);
      return;
    }

    this.returnToStep = null;
    this.currentStep = 3;
  }

  redirectToAuth(mode: 'login' | 'register'): void {
    const draft = {
      providerId: this.selectedProviderId,
      provider: this.selectedProvider,
      selectedDate: this.selectedDate,
      slot: this.selectedSlot,
      tenantId: this.tenantId,
      returnUrl: this.router.url
    };
    sessionStorage.setItem('pending_booking_draft', JSON.stringify(draft));
    this.close.emit();
    this.router.navigate(['/login'], {
      queryParams: {
        mode: mode,
        resumeBooking: 'true',
        returnUrl: this.router.url
      }
    });
  }

  // --- Step 3: Customer Info ---
  goToStep4(): void {
    if (!this.customerName.trim()) {
      this.errorMessage.set(this.isStaffOrAdmin() ? 'Please enter customer\'s full name.' : 'Please enter your full name.');
      return;
    }
    if (!this.customerEmail.trim()) {
      this.errorMessage.set(this.isStaffOrAdmin() ? 'Please enter customer\'s email address.' : 'Please enter your email address.');
      return;
    }
    if (!isValidEmail(this.customerEmail)) {
      this.errorMessage.set('Please enter a valid email address (e.g. name@example.com).');
      return;
    }
    if (!this.customerPhone.trim()) {
      this.errorMessage.set(this.isStaffOrAdmin() ? 'Please enter customer\'s 10-digit mobile number.' : 'Please enter your 10-digit mobile number.');
      return;
    }
    if (!isValidPhone(this.customerPhone)) {
      this.errorMessage.set('Mobile number must be exactly 10 digits.');
      return;
    }
    if (!this.customerAddress.trim()) {
      this.errorMessage.set(this.isStaffOrAdmin() ? 'Please enter customer\'s complete address.' : 'Please enter your complete address.');
      return;
    }
    if (!this.bookingNotes.trim()) {
      this.errorMessage.set('Please enter the reason for visit / appointment purpose.');
      return;
    }
    this.errorMessage.set('');
    this.currentStep = 4;
  }

  changeSlot(): void {
    this.returnToStep = 3;
    this.errorMessage.set('');
    this.currentStep = 2;
  }

  // --- Back Navigation ---
  goBack(): void {
    if (this.currentStep === 2) {
      if (this.returnToStep) {
        const dest = this.returnToStep;
        this.returnToStep = null;
        this.currentStep = dest as (1 | 2 | 3 | 4 | 5);
      } else {
        this.backToStep1();
      }
    }
    else if (this.currentStep === 3) this.backToStep2();
    else if (this.currentStep === 4) this.backToStep3();
  }

  backToStep1(): void {
    this.errorMessage.set('');
    this.returnToStep = null;
    this.currentStep = 1;
  }

  backToStep2(): void {
    this.errorMessage.set('');
    this.currentStep = 2;
  }

  backToStep3(): void {
    this.errorMessage.set('');
    this.currentStep = 3;
  }

  // --- Step 4: Confirm Booking Submission ---
  confirmBooking(): void {
    if (!this.selectedProviderId || !this.selectedSlot) return;

    this.isSubmitting.set(true);
    this.errorMessage.set('');

    const req: BookAppointmentRequestDto = {
      serviceProviderId: this.selectedProviderId,
      appointmentDate: this.selectedDate,
      startTime: this.selectedSlot.startTime,
      endTime: this.selectedSlot.endTime,
      customerName: this.customerName.trim(),
      customerEmail: this.customerEmail.trim(),
      customerPhone: this.customerPhone.trim(),
      customerAddress: this.customerAddress.trim(),
      reason: this.bookingNotes.trim() || undefined
    };

    this.appointmentService.bookAppointment(req).subscribe({
      next: (res: AppointmentDto) => {
        sessionStorage.removeItem('pending_booking_draft');
        this.isSubmitting.set(false);
        this.confirmedAppointment.set(res);
        this.booked.emit(res);
        this.close.emit();
      },
      error: (err) => {
        this.isSubmitting.set(false);
        const errorMsg = err.error?.message || 'Failed to confirm booking. Please try again.';
        this.errorMessage.set(errorMsg);

        if (err.status === 409) {
          // Concurrency Conflict: slot just got booked
          this.fetchSlots();
          this.currentStep = 2;
          this.confirmService.alert({
            title: 'Slot Already Booked',
            message: errorMsg,
            variant: 'warning',
            buttonText: 'Choose Another Slot'
          });
        } else {
          this.confirmService.alert({
            title: 'Booking Notice',
            message: errorMsg,
            variant: 'danger',
            buttonText: 'OK'
          });
        }
      }
    });
  }

  onRescheduleAppointment(app: AppointmentDto): void {
    this.currentStep = 2;
  }

  startNewBooking(): void {
    sessionStorage.removeItem('pending_booking_draft');
    this.selectedSlot = null;
    this.confirmedAppointment.set(null);
    this.bookingNotes = '';
    this.copiedRef = false;
    this.linkedCustomer = null;
    this.isCustomerNotFound.set(false);
    this.checkUserAuth();
    this.currentStep = 1;
    this.scrollToModalTop();
  }

  scrollToModalTop(): void {
    setTimeout(() => {
      const modalBody = document.querySelector('.modal-body');
      if (modalBody) {
        modalBody.scrollTop = 0;
      }
    }, 40);
  }

  printTicket(): void {
    window.print();
  }

  onModalClose(): void {
    if (!this.authService.isAuthenticated()) {
      sessionStorage.removeItem('pending_booking_draft');
    }
    this.close.emit();
  }

  // Formatters & Helpers
  formatDate(dateStr: string): string {
    return formatDatePretty(dateStr);
  }

  formatTime(timeStr: string): string {
    return formatTo12Hour(timeStr);
  }

  formatSlotRange(slot: AvailableSlotDto): string {
    if (!slot) return '';
    const text = slot.displayText || (slot.startTime && slot.endTime ? `${this.formatTime(slot.startTime)} - ${this.formatTime(slot.endTime)}` : this.formatTime(slot.startTime));
    return text.replace(/\s*[–—]\s*/g, ' - ');
  }

  getInitials(name: string): string {
    return getInitials(name);
  }

  getAvatarGradient(name: string): string {
    const gradients = [
      'linear-gradient(135deg, #6366f1 0%, #8b5cf6 100%)',
      'linear-gradient(135deg, #0ea5e9 0%, #3b82f6 100%)',
      'linear-gradient(135deg, #10b981 0%, #059669 100%)',
      'linear-gradient(135deg, #f59e0b 0%, #d97706 100%)',
      'linear-gradient(135deg, #ec4899 0%, #8b5cf6 100%)',
      'linear-gradient(135deg, #06b6d4 0%, #6366f1 100%)'
    ];
    let hash = 0;
    for (let i = 0; i < (name || '').length; i++) {
      hash = name.charCodeAt(i) + ((hash << 5) - hash);
    }
    const index = Math.abs(hash) % gradients.length;
    return gradients[index];
  }
}

