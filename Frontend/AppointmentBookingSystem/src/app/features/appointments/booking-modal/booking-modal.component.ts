import { Component, EventEmitter, Input, OnInit, Output, computed, inject, signal, ChangeDetectorRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, Router } from '@angular/router';
import { AppointmentService } from '../../../core/services/appointment.service';
import { ProviderService } from '../../../core/services/provider.service';
import { ServiceCatalogService } from '../../../core/services/service-catalog.service';
import { AuthService } from '../../../core/services/auth.service';
import { ServiceProviderDto, ProviderStatus } from '../../../core/models/provider.models';
import { ServiceDto } from '../../../core/models/service.models';
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
import { CartModalComponent, CartModalItem } from '../../../shared/components/cart-modal/cart-modal.component';

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
    OnlyDigitsDirective,
    CopyToClipboardDirective,
    CartModalComponent
  ],
  templateUrl: './booking-modal.component.html',
  styleUrls: ['./booking-modal.component.css']
})
export class BookingModalComponent implements OnInit {
  private readonly appointmentService = inject(AppointmentService);
  private readonly providerService = inject(ProviderService);
  private readonly serviceCatalogService = inject(ServiceCatalogService);
  readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly confirmService = inject(ConfirmService);
  private readonly cdr = inject(ChangeDetectorRef);

  @Input() initialProviderId: number | null = null;
  @Input() initialServiceId: number | null = null;
  @Input() initialServiceIds: number[] = [];
  @Input() initialDate?: string;
  @Input() tenantId?: number;
  @Output() close = new EventEmitter<void>();
  @Output() booked = new EventEmitter<AppointmentDto>();

  // Booking Flow Mode: 'broadcast' (Urban Company Auto-Assign) vs 'direct' (Specific Specialist)
  bookingMode: 'broadcast' | 'direct' = 'broadcast';
  selectedServiceId: number | null = null;
  selectedService: ServiceDto | null = null;
  readonly selectedServiceIds = signal<number[]>([]);

  readonly selectedServices = computed<ServiceDto[]>(() => {
    const ids = this.selectedServiceIds();
    const catalog = this.catalogServices();
    const list: ServiceDto[] = [];
    for (const id of ids) {
      const match = catalog.find(s => s.id === id);
      if (match) {
        list.push(match);
      } else if (this.selectedService && this.selectedService.id === id) {
        list.push(this.selectedService);
      }
    }
    return list;
  });

  readonly cartModalItems = computed<CartModalItem[]>(() => {
    const ids = this.selectedServiceIds();
    const catalog = this.catalogServices();
    const countsMap = new Map<number, number>();
    for (const id of ids) {
      countsMap.set(id, (countsMap.get(id) || 0) + 1);
    }

    const items: CartModalItem[] = [];
    for (const [id, qty] of countsMap.entries()) {
      const match = catalog.find(s => s.id === id) || (this.selectedService?.id === id ? this.selectedService : null);
      if (match) {
        items.push({
          id: match.id,
          name: match.name,
          price: match.price,
          durationMinutes: match.durationMinutes,
          description: match.description,
          quantity: qty
        });
      }
    }
    return items;
  });

  readonly totalCartItemsCount = computed<number>(() => {
    return this.selectedServiceIds().length;
  });

  readonly totalServicesPrice = computed<number>(() => {
    const items = this.cartModalItems();
    if (items.length === 0) return this.selectedService?.price || 0;
    return items.reduce((sum, s) => sum + (s.price || 0) * (s.quantity || 1), 0);
  });

  readonly totalServicesDuration = computed<number>(() => {
    const items = this.cartModalItems();
    if (items.length === 0) return this.selectedService?.durationMinutes || 30;
    return items.reduce((sum, s) => sum + (s.durationMinutes || 30) * (s.quantity || 1), 0);
  });

  readonly selectedServicesNamesSummary = computed<string>(() => {
    const items = this.cartModalItems();
    if (items.length === 0) return this.selectedService?.name || '';
    return items.map(s => s.quantity && s.quantity > 1 ? `${s.name} (×${s.quantity})` : s.name).join(' + ');
  });

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
  readonly isCartModalOpen = signal<boolean>(false);

  // Stepper State:
  // 1 = Select Service / Specialist
  // 2 = Side-by-Side: Month Calendar Date Picker (Left) + Available Time Slots (Right)
  // 3 = Customer Info (logged-in detection & edit mode)
  // 4 = Review & Confirmation
  // 5 = Success / Confirmed Ticket
  currentStep: 1 | 2 | 3 | 4 | 5 = 1;
  returnToStep: number | null = null;

  // Selected Specialist State
  selectedProviderId: number | null = null;
  readonly selectedProviderSignal = signal<ServiceProviderDto | null>(null);

  get selectedProvider(): ServiceProviderDto | null {
    return this.selectedProviderSignal();
  }
  set selectedProvider(value: ServiceProviderDto | null) {
    this.selectedProviderSignal.set(value);
  }

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
  isEmailTouched = false;

  get isEmailValid(): boolean {
    if (!this.customerEmail || !this.customerEmail.trim()) return false;
    return isValidEmail(this.customerEmail);
  }

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

  // Service Catalog API & Service Filtering
  readonly catalogServices = signal<ServiceDto[]>([]);
  readonly isLoadingServices = signal<boolean>(false);
  serviceSearchQuery = signal<string>('');

  readonly filteredCatalogServices = computed<ServiceDto[]>(() => {
    const query = this.serviceSearchQuery().trim().toLowerCase();
    const list = this.catalogServices();
    if (!query) return list;
    return list.filter(s =>
      s.name.toLowerCase().includes(query) ||
      (s.description && s.description.toLowerCase().includes(query))
    );
  });

  findCurrentServiceProvider(): ServiceProviderDto | null {
    if (!this.authService.isServiceProvider()) return null;
    const currentUser = this.authService.currentUser();
    if (!currentUser) return null;
    const list = this.providers();
    const myProvider = list.find(p =>
      (p.userId && currentUser.id && Number(p.userId) === Number(currentUser.id)) ||
      (p.email && currentUser.email && p.email.trim().toLowerCase() === currentUser.email.toLowerCase().trim()) ||
      (p.phoneNumber && currentUser.phoneNumber && p.phoneNumber.replace(/\D/g, '') === currentUser.phoneNumber.replace(/\D/g, '')) ||
      (p.name && currentUser.fullName && p.name.trim().toLowerCase() === currentUser.fullName.toLowerCase().trim())
    ) || null;
    if (myProvider && !this.selectedProviderId) {
      this.selectedProviderId = myProvider.id;
      this.selectedProvider = myProvider;
    }
    return myProvider;
  }

  // Services available for selection (Filtered to assigned services when Service Provider is booking)
  readonly providerAvailableServices = computed<ServiceDto[]>(() => {
    const all = this.filteredCatalogServices();
    if (!this.authService.isServiceProvider()) {
      return all;
    }
    const p = this.selectedProviderSignal() || this.findCurrentServiceProvider();
    if (!p) {
      return [];
    }
    const assignedIds = this.getProviderServiceIds(p);
    if (assignedIds.size === 0) {
      return [];
    }
    return all.filter(s => assignedIds.has(s.id));
  });

  // Step 1 Sub-State: 'services' -> 'specialist' (Admin/Customer flow: Service Select first, then Specialist)
  readonly step1SubState = signal<'services' | 'specialist'>('services');

  getProviderServiceIds(p: ServiceProviderDto): Set<number> {
    const ids = new Set<number>();
    if (p.serviceId) ids.add(Number(p.serviceId));
    if (p.serviceIds && Array.isArray(p.serviceIds)) {
      p.serviceIds.forEach(id => ids.add(Number(id)));
    }
    if (p.assignedServices && Array.isArray(p.assignedServices)) {
      p.assignedServices.forEach(as => ids.add(Number(as.id)));
    }
    return ids;
  }

  getProviderDisplayServices(p: ServiceProviderDto): { id: number; name: string }[] {
    if (!p) return [];
    if (p.assignedServices && Array.isArray(p.assignedServices) && p.assignedServices.length > 0) {
      return p.assignedServices.map(as => ({ id: as.id, name: as.name }));
    }
    const ids = this.getProviderServiceIds(p);
    if (ids.size > 0) {
      const catalog = this.catalogServices();
      const list: { id: number; name: string }[] = [];
      ids.forEach(id => {
        const found = catalog.find(s => s.id === id);
        if (found) {
          list.push({ id: found.id, name: found.name });
        }
      });
      if (list.length > 0) return list;
    }
    if (p.serviceName) {
      return [{ id: p.serviceId || 0, name: p.serviceName }];
    }
    return [];
  }

  // Filter specialists qualified to perform all selected services (Strictly for Admin flow)
  readonly qualifiedProvidersForSelectedServices = computed<ServiceProviderDto[]>(() => {
    const sIds = this.selectedServiceIds();
    const allProviders = this.providers().filter(p => p.status === ProviderStatus.Active);
    const query = this.providerSearchQuery().trim().toLowerCase();

    let list = allProviders;
    if (sIds.length > 0) {
      const distinctIds = Array.from(new Set(sIds));
      list = allProviders.filter(p => {
        const pServiceIds = this.getProviderServiceIds(p);
        return distinctIds.every(id => pServiceIds.has(id));
      });
    }

    if (!query) return list;
    return list.filter(p =>
      p.name.toLowerCase().includes(query) ||
      (p.email && p.email.toLowerCase().includes(query)) ||
      (p.phoneNumber && p.phoneNumber.toLowerCase().includes(query))
    );
  });

  goToChooseSpecialist(): void {
    if (this.selectedServiceIds().length === 0 && !this.selectedServiceId) {
      this.errorMessage.set('Please select at least one service from the catalog to proceed.');
      return;
    }
    this.errorMessage.set('');
    this.step1SubState.set('specialist');
  }

  backToServiceSelection(): void {
    this.errorMessage.set('');
    this.step1SubState.set('services');
  }

  selectSpecialistForBooking(p: ServiceProviderDto): void {
    this.bookingMode = 'direct';
    this.selectedProviderId = p.id;
    this.selectedProvider = p;
    this.errorMessage.set('');
  }

  autoSelectProviderServices(provider: ServiceProviderDto): void {
    if (!provider) return;

    let targetServiceId: number | null = null;
    if (provider.assignedServices && provider.assignedServices.length > 0) {
      targetServiceId = provider.assignedServices[0].id;
    } else if (provider.serviceIds && provider.serviceIds.length > 0) {
      targetServiceId = provider.serviceIds[0];
    } else if (provider.serviceId) {
      targetServiceId = provider.serviceId;
    }

    if (this.selectedServiceIds().length === 0 && targetServiceId) {
      this.selectedServiceId = targetServiceId;
      this.selectedServiceIds.set([targetServiceId]);
      const match = this.catalogServices().find(s => s.id === targetServiceId);
      if (match) {
        this.selectedService = match;
      }
    } else if (this.selectedServiceIds().length > 0) {
      const firstId = this.selectedServiceIds()[0];
      const match = this.catalogServices().find(s => s.id === firstId);
      if (match) this.selectedService = match;
    }
  }

  isServiceSelected(serviceId: number): boolean {
    return this.selectedServiceIds().includes(serviceId);
  }

  getServiceCount(serviceId: number): number {
    return this.selectedServiceIds().filter(id => id === serviceId).length;
  }

  incrementServiceSelection(serviceId: number, event?: Event): void {
    if (event) event.stopPropagation();
    const updated = [...this.selectedServiceIds(), serviceId];
    this.selectedServiceIds.set(updated);
    if (!this.selectedServiceId) {
      this.selectedServiceId = serviceId;
      this.selectedService = this.catalogServices().find(s => s.id === serviceId) || null;
    }
    this.selectedSlot = null;
    if (this.currentStep === 2) {
      this.fetchSlots();
    }
  }

  decrementServiceSelection(serviceId: number, event?: Event): void {
    if (event) event.stopPropagation();
    const ids = this.selectedServiceIds();
    const idx = ids.lastIndexOf(serviceId);
    if (idx !== -1) {
      const updated = [...ids];
      updated.splice(idx, 1);
      this.selectedServiceIds.set(updated);
      if (updated.length > 0) {
        this.selectedServiceId = updated[0];
        this.selectedService = this.catalogServices().find(s => s.id === updated[0]) || null;
      } else {
        this.selectedServiceId = null;
        this.selectedService = null;
        this.isCartModalOpen.set(false);
      }
      this.selectedSlot = null;
      if (this.currentStep === 2) {
        this.fetchSlots();
      }
    }
  }

  toggleServiceSelection(service: ServiceDto): void {
    const current = this.selectedServiceIds();
    let updated: number[];
    if (current.includes(service.id)) {
      updated = current.filter(id => id !== service.id);
    } else {
      updated = [...current, service.id];
    }
    this.selectedServiceIds.set(updated);

    // Sync legacy fields
    if (updated.length > 0) {
      this.selectedServiceId = updated[0];
      this.selectedService = this.catalogServices().find(s => s.id === updated[0]) || service;
    } else {
      this.selectedServiceId = null;
      this.selectedService = null;
    }

    if (this.isAdmin()) {
      this.selectedProviderId = null;
      this.selectedProvider = null;
    }
    this.selectedSlot = null;
    this.errorMessage.set('');
  }

  removeServiceFromSelection(serviceId: number, event?: Event): void {
    if (event) event.stopPropagation();
    const updated = this.selectedServiceIds().filter(id => id !== serviceId);
    this.selectedServiceIds.set(updated);

    if (updated.length > 0) {
      this.selectedServiceId = updated[0];
      this.selectedService = this.catalogServices().find(s => s.id === updated[0]) || null;
    } else {
      this.selectedServiceId = null;
      this.selectedService = null;
    }
    this.selectedSlot = null;
    if (this.currentStep === 2) {
      this.fetchSlots();
    }
  }

  selectServiceCard(service: ServiceDto): void {
    this.toggleServiceSelection(service);
  }

  openCartModal(): void {
    if (this.selectedServices().length > 0) {
      this.isCartModalOpen.set(true);
    }
  }

  closeCartModal(): void {
    this.isCartModalOpen.set(false);
  }

  onProceedFromCartModal(): void {
    this.isCartModalOpen.set(false);
    if (this.currentStep === 1) {
      if (this.isAdmin() && this.step1SubState() === 'services') {
        this.goToChooseSpecialist();
      } else {
        this.goToStep2();
      }
    }
  }

  clearCartInBookingModal(): void {
    this.selectedServiceIds.set([]);
    this.selectedServiceId = null;
    this.selectedService = null;
    this.isCartModalOpen.set(false);
    if (this.isAdmin()) {
      this.selectedProviderId = null;
      this.selectedProvider = null;
    }
    if (this.currentStep > 1) {
      this.currentStep = 1;
    }
  }

  get canGoBack(): boolean {
    if (this.currentStep === 1) {
      return this.isAdmin() && this.step1SubState() === 'specialist';
    }
    if (this.currentStep <= 1 || this.currentStep >= 5) return false;
    return true;
  }

  get providerDisplayName(): string {
    if (this.selectedProvider) return this.selectedProvider.name;
    if (this.authService.isServiceProvider()) {
      const u = this.authService.currentUser();
      return u?.fullName || 'Your Schedule';
    }
    return '';
  }

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

  initRoleBookingFlow(): void {
    if (this.initialProviderId) {
      this.bookingMode = 'direct';
      this.selectedProviderId = Number(this.initialProviderId);
      this.step1SubState.set('services');
      this.currentStep = 1;
      return;
    }
    if (this.initialServiceIds && this.initialServiceIds.length > 0) {
      const cleanIds = this.initialServiceIds.map(Number);
      this.selectedServiceIds.set(cleanIds);
      this.selectedServiceId = cleanIds[0];
      if (this.isAdmin()) {
        this.step1SubState.set('specialist');
        this.currentStep = 1;
      } else {
        this.bookingMode = 'broadcast';
        this.currentStep = 2;
      }
      return;
    }
    if (this.initialServiceId) {
      const id = Number(this.initialServiceId);
      this.selectedServiceIds.set([id]);
      this.selectedServiceId = id;
      if (this.isAdmin()) {
        this.step1SubState.set('specialist');
        this.currentStep = 1;
      } else {
        this.bookingMode = 'broadcast';
        this.currentStep = 2;
      }
      return;
    }

    if (this.authService.isServiceProvider()) {
      this.bookingMode = 'direct';
      this.step1SubState.set('services');
      this.currentStep = 1;
      this.selectedServiceIds.set([]);
      this.selectedServiceId = null;
      this.selectedService = null;
      const currentUser = this.authService.currentUser();
      if (currentUser && this.providers().length > 0) {
        const myProvider = this.providers().find(p =>
          (p.userId && currentUser.id && Number(p.userId) === Number(currentUser.id)) ||
          (p.email && currentUser.email && p.email.trim().toLowerCase() === currentUser.email.toLowerCase().trim()) ||
          (p.phoneNumber && currentUser.phoneNumber && p.phoneNumber.replace(/\D/g, '') === currentUser.phoneNumber.replace(/\D/g, '')) ||
          (p.name && currentUser.fullName && p.name.trim().toLowerCase() === currentUser.fullName.toLowerCase().trim())
        );
        if (myProvider) {
          this.selectedProviderId = myProvider.id;
          this.selectedProvider = myProvider;
        }
      }
    } else {
      this.bookingMode = 'broadcast';
      this.step1SubState.set('services');
      this.currentStep = 1;
      this.selectedServiceIds.set([]);
      this.selectedServiceId = null;
      this.selectedService = null;
      this.selectedProviderId = null;
      this.selectedProvider = null;
    }
  }

  private resolveTenantId(): void {
    if (this.tenantId && this.tenantId > 0) {
      return;
    }
    const u = this.authService.currentUser();
    if (u?.tenantId && u.tenantId > 0) {
      this.tenantId = u.tenantId;
      return;
    }
    if (this.authService.isAuthenticated()) {
      this.tenantId = 1;
    }
  }

  // --- Component Lifecycle ---
  ngOnInit(): void {
    this.resolveTenantId();

    const today = new Date();
    today.setHours(0, 0, 0, 0);
    this.minDate = this.formatDateIso(today);

    const maxDateObj = new Date(today);
    maxDateObj.setDate(maxDateObj.getDate() + 15);
    maxDateObj.setHours(23, 59, 59, 999);
    this.maxDate = this.formatDateIso(maxDateObj);

    if (this.initialDate && this.initialDate >= this.minDate && this.initialDate <= this.maxDate) {
      this.selectedDate = this.initialDate;
    } else {
      this.selectedDate = this.minDate;
    }
    const dateObj = this.initialDate ? new Date(this.initialDate) : today;
    this.currentMonthDate = new Date(dateObj.getFullYear(), dateObj.getMonth(), 1);

    this.initRoleBookingFlow();

    if (!this.authService.isInitialized()) {
      this.authService.checkAuth().subscribe(() => {
        this.resolveTenantId();
        this.checkUserAuth();
        this.initRoleBookingFlow();
        this.checkAndResumeDraft();
        this.loadProviders();
        this.loadServices();
      });
    } else {
      this.resolveTenantId();
      this.checkUserAuth();
      this.initRoleBookingFlow();
      this.checkAndResumeDraft();
      this.loadProviders();
      this.loadServices();
    }
  }

  loadServices(): void {
    this.resolveTenantId();
    this.isLoadingServices.set(true);
    this.serviceCatalogService.getActiveServices(this.tenantId).subscribe({
      next: (services) => {
        const list = services || [];
        const filtered = (this.tenantId && this.tenantId > 0)
          ? list.filter(s => !s.tenantId || s.tenantId === Number(this.tenantId))
          : list;
        this.catalogServices.set(filtered);
        this.isLoadingServices.set(false);

        const currentIds = this.selectedServiceIds();
        if (currentIds.length > 0) {
          const match = list.find(s => s.id === currentIds[0]);
          if (match) {
            this.selectedService = match;
          }
          if (this.currentStep === 2) {
            this.fetchSlots();
          }
          return;
        }

        // Auto-select provider's services only if a provider was pre-loaded from outside and user is not service provider
        if (!this.authService.isServiceProvider() && this.selectedProvider && this.selectedServiceIds().length === 0) {
          this.autoSelectProviderServices(this.selectedProvider);
        }

        if (this.selectedServiceIds().length > 0) {
          const firstId = this.selectedServiceIds()[0];
          const match = list.find(s => s.id === Number(firstId));
          if (match) {
            this.selectedService = match;
            this.selectedServiceId = match.id;
          }
          if (this.currentStep < 2 && !this.isAdmin()) {
            this.currentStep = 2;
            this.fetchSlots();
          }
        } else {
          const targetServiceId = this.selectedServiceId || this.initialServiceId;
          if (targetServiceId) {
            const match = list.find(s => s.id === Number(targetServiceId));
            if (match) {
              this.bookingMode = 'broadcast';
              this.selectedServiceIds.set([match.id]);
              this.selectedServiceId = match.id;
              this.selectedService = match;
              if (this.currentStep < 2 && !this.isAdmin()) {
                this.currentStep = 2;
                this.fetchSlots();
              }
            }
          }
        }
      },
      error: (err) => {
        this.isLoadingServices.set(false);
        console.error('Failed to load active services for booking dropdown', err);
      }
    });
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
      if (draft && (draft.slot || draft.selectedSlot || draft.serviceId || draft.serviceIds || draft.providerId)) {
        const slot = draft.slot || draft.selectedSlot;
        if (slot) {
          this.selectedSlot = slot;
        }
        if (draft.selectedDate) {
          this.selectedDate = draft.selectedDate;
        }

        if (draft.bookingMode) {
          this.bookingMode = draft.bookingMode;
        }

        if (draft.serviceIds && Array.isArray(draft.serviceIds) && draft.serviceIds.length > 0) {
          this.bookingMode = 'broadcast';
          const ids = draft.serviceIds.map(Number);
          this.selectedServiceIds.set(ids);
          this.selectedServiceId = ids[0];
          this.selectedService = draft.service || this.selectedService;
        } else if (draft.serviceId) {
          this.bookingMode = 'broadcast';
          const id = Number(draft.serviceId);
          this.selectedServiceIds.set([id]);
          this.selectedServiceId = id;
          this.selectedService = draft.service || this.selectedService;
        }

        if (draft.providerId) {
          this.selectedProviderId = Number(draft.providerId);
          this.selectedProvider = draft.provider || this.selectedProvider;
        }

        if (draft.bookingNotes) {
          this.bookingNotes = draft.bookingNotes;
        }

        // Fill customer details from newly logged-in customer account
        this.checkUserAuth();

        if (!this.customerName && draft.customerName) this.customerName = draft.customerName;
        if (!this.customerEmail && draft.customerEmail) this.customerEmail = draft.customerEmail;
        if (!this.customerPhone && draft.customerPhone) this.customerPhone = draft.customerPhone;
        if (!this.customerAddress && draft.customerAddress) this.customerAddress = draft.customerAddress;

        // Navigate directly to the step the customer was on or heading to!
        const targetStep = draft.targetStep || draft.currentStep || 3;
        this.currentStep = (targetStep >= 2 && targetStep <= 4) ? targetStep : 3;

        // Ensure availableSlots contains the draft slot so slot summary displays properly
        if (slot && (!this.availableSlots || this.availableSlots.length === 0)) {
          this.availableSlots = [slot];
        }
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
    this.resolveTenantId();
    this.isLoadingProviders.set(true);
    // Public active providers endpoint filtered by tenant if available
    this.providerService.getActiveProviders(this.tenantId).subscribe({
      next: (activeProviders) => {
        const list = activeProviders || [];
        const filtered = this.tenantId
          ? list.filter(p => p.tenantId === Number(this.tenantId))
          : list;
        this.providers.set(filtered);
        this.isLoadingProviders.set(false);

        const targetId = this.selectedProviderId || this.initialProviderId;
        if (targetId) {
          const match = list.find(p => p.id === Number(targetId));
          if (match) {
            this.selectedProviderId = match.id;
            this.selectedProvider = match;
            if (!this.authService.isServiceProvider()) {
              this.autoSelectProviderServices(match);
            }
            if (this.currentStep === 2) {
              this.fetchSlots();
            }
          }
        } else if (this.authService.isServiceProvider()) {
          const currentUser = this.authService.currentUser();
          if (currentUser) {
            const myProvider = list.find(p =>
              (p.userId && currentUser.id && Number(p.userId) === Number(currentUser.id)) ||
              (p.email && currentUser.email && p.email.trim().toLowerCase() === currentUser.email.toLowerCase().trim()) ||
              (p.phoneNumber && currentUser.phoneNumber && p.phoneNumber.replace(/\D/g, '') === currentUser.phoneNumber.replace(/\D/g, '')) ||
              (p.name && currentUser.fullName && p.name.trim().toLowerCase() === currentUser.fullName.toLowerCase().trim())
            );
            if (myProvider) {
              this.selectedProviderId = myProvider.id;
              this.selectedProvider = myProvider;
              this.bookingMode = 'direct';
              this.step1SubState.set('services');
              if (!this.tenantId && myProvider.tenantId) {
                this.tenantId = myProvider.tenantId;
              }
              if (this.selectedServiceIds().length === 0) {
                this.currentStep = 1;
              } else if (this.currentStep === 2) {
                this.fetchSlots();
              }
            }
          }
        }
        this.cdr.markForCheck();
      },
      error: () => {
        this.isLoadingProviders.set(false);
        this.errorMessage.set('Failed to load active service providers.');
      }
    });
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    this.showSearchDropdown.set(false);
  }

  goToStep2(): void {
    if (this.authService.isServiceProvider()) {
      if (this.selectedServiceIds().length === 0 && !this.selectedServiceId) {
        this.errorMessage.set('Please select which service(s) to schedule for this customer.');
        return;
      }
      this.bookingMode = 'direct';
      if (!this.selectedProviderId) {
        const currentUser = this.authService.currentUser();
        if (currentUser) {
          const list = this.providers();
          const myProvider = list.find(p =>
            (p.userId && currentUser.id && Number(p.userId) === Number(currentUser.id)) ||
            (p.email && currentUser.email && p.email.trim().toLowerCase() === currentUser.email.toLowerCase().trim()) ||
            (p.name && currentUser.fullName && p.name.trim().toLowerCase() === currentUser.fullName.toLowerCase().trim())
          );
          if (myProvider) {
            this.selectedProviderId = myProvider.id;
            this.selectedProvider = myProvider;
          }
        }
      }
    } else if (this.isAdmin()) {
      if (this.selectedServiceIds().length === 0 && !this.selectedServiceId) {
        this.errorMessage.set('Please select at least one service to proceed.');
        return;
      }
      if (!this.selectedProviderId) {
        this.errorMessage.set('Please choose a specialist to assign this appointment to.');
        return;
      }
      this.bookingMode = 'direct';
    } else {
      // Customer: Always broadcast flow (first available certified specialist accepts)
      if (this.selectedServiceIds().length === 0 && !this.selectedServiceId) {
        this.errorMessage.set('Please select at least one service to proceed.');
        return;
      }
      this.bookingMode = 'broadcast';
      this.selectedProviderId = null;
      this.selectedProvider = null;
    }
    this.errorMessage.set('');
    this.currentStep = 2;
    this.fetchSlots();
  }

  // --- Step 2: Available Time Slots (Hide booked/unavailable and past time slots) ---
  fetchSlots(): void {
    if (!this.selectedDate) return;
    const sIds = this.selectedServiceIds();
    const primaryServiceId = sIds.length > 0 ? sIds[0] : this.selectedServiceId;

    if (this.bookingMode === 'broadcast' && !primaryServiceId) return;

    if (this.bookingMode === 'direct' && !this.selectedProviderId) {
      if (this.authService.isServiceProvider()) {
        const currentUser = this.authService.currentUser();
        if (currentUser) {
          const list = this.providers();
          const myProvider = list.find(p =>
            (p.userId && currentUser.id && Number(p.userId) === Number(currentUser.id)) ||
            (p.email && currentUser.email && p.email.trim().toLowerCase() === currentUser.email.toLowerCase().trim()) ||
            (p.name && currentUser.fullName && p.name.trim().toLowerCase() === currentUser.fullName.toLowerCase().trim())
          );
          if (myProvider) {
            this.selectedProviderId = myProvider.id;
            this.selectedProvider = myProvider;
          }
        }
      }
      if (!this.selectedProviderId) {
        return;
      }
    }

    this.isLoadingSlots.set(true);
    this.errorMessage.set('');

    const slots$ = this.bookingMode === 'broadcast'
      ? this.appointmentService.getAvailableSlotsByService(primaryServiceId!, this.selectedDate, this.tenantId, sIds.length > 0 ? sIds : undefined)
      : this.appointmentService.getAvailableSlots(this.selectedProviderId!, this.selectedDate, sIds.length > 0 ? sIds : undefined);

    slots$.subscribe({
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
      bookingMode: this.bookingMode,
      serviceId: this.selectedServiceId,
      serviceIds: this.selectedServiceIds(),
      service: this.selectedService,
      providerId: this.selectedProviderId,
      provider: this.selectedProvider,
      selectedDate: this.selectedDate,
      slot: this.selectedSlot,
      tenantId: this.tenantId,
      targetStep: this.currentStep === 4 ? 4 : 3,
      currentStep: this.currentStep,
      customerName: this.customerName,
      customerEmail: this.customerEmail,
      customerPhone: this.customerPhone,
      customerAddress: this.customerAddress,
      bookingNotes: this.bookingNotes,
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
      this.isEmailTouched = true;
      this.errorMessage.set(this.isStaffOrAdmin() ? 'Please enter customer\'s email address.' : 'Please enter your email address.');
      return;
    }
    if (!isValidEmail(this.customerEmail)) {
      this.isEmailTouched = true;
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
    // Reason for visit is optional
    this.errorMessage.set('');
    this.returnToStep = null;
    this.currentStep = 4;
  }

  editCustomerDetails(): void {
    this.returnToStep = 4;
    this.errorMessage.set('');
    this.currentStep = 3;
  }

  changeSlot(): void {
    this.returnToStep = 3;
    this.errorMessage.set('');
    this.currentStep = 2;
  }

  // --- Back Navigation ---
  goBack(): void {
    if (this.currentStep === 1) {
      if (this.isAdmin() && this.step1SubState() === 'specialist') {
        this.step1SubState.set('services');
      }
    }
    else if (this.currentStep === 2) {
      if (this.returnToStep) {
        const dest = this.returnToStep;
        this.returnToStep = null;
        this.currentStep = dest as (1 | 2 | 3 | 4 | 5);
      } else {
        this.backToStep1();
      }
    }
    else if (this.currentStep === 3) {
      if (this.returnToStep === 4) {
        this.returnToStep = null;
        this.currentStep = 4;
      } else {
        this.backToStep2();
      }
    }
    else if (this.currentStep === 4) this.backToStep3();
  }

  backToStep1(): void {
    this.errorMessage.set('');
    this.returnToStep = null;
    this.currentStep = 1;
    if (this.isAdmin()) {
      this.step1SubState.set('specialist');
    } else {
      this.step1SubState.set('services');
    }
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
    if (!this.selectedSlot) return;
    if (this.bookingMode === 'direct' && !this.selectedProviderId) return;
    const sIds = this.selectedServiceIds();
    const primaryServiceId = sIds.length > 0 ? sIds[0] : this.selectedServiceId;
    if (this.bookingMode === 'broadcast' && !primaryServiceId) return;

    if (!this.authService.isAuthenticated() && !this.isAdmin()) {
      this.showAuthPrompt.set(true);
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set('');

    const resolvedServiceId = (this.selectedProvider?.serviceId || (this.selectedService ? this.selectedService.id : (sIds.length > 0 ? sIds[0] : primaryServiceId))) || null;

    const req: BookAppointmentRequestDto = {
      serviceProviderId: this.bookingMode === 'direct' ? this.selectedProviderId : null,
      serviceId: resolvedServiceId,
      serviceIds: sIds.length > 0 ? sIds : (resolvedServiceId ? [resolvedServiceId] : []),
      appointmentDate: this.selectedDate,
      startTime: this.selectedSlot.startTime,
      endTime: this.selectedSlot.endTime,
      customerName: this.customerName.trim(),
      customerEmail: this.customerEmail.trim(),
      customerPhone: this.customerPhone.trim(),
      customerAddress: this.customerAddress.trim(),
      reason: this.bookingNotes.trim() || undefined
    };

    this.appointmentService.bookAppointment(req, this.tenantId).subscribe({
      next: (res: AppointmentDto) => {
        sessionStorage.removeItem('pending_booking_draft');
        this.isSubmitting.set(false);
        this.confirmedAppointment.set(res);
        this.booked.emit(res);
        this.currentStep = 5;
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

  finishAndClose(): void {
    this.close.emit();
    if (this.authService.isCustomer()) {
      this.router.navigate(['/my-appointments']);
    }
  }

  onModalClose(): void {
    if (!this.authService.isAuthenticated()) {
      sessionStorage.removeItem('pending_booking_draft');
    }
    if (this.currentStep === 5 && this.authService.isCustomer()) {
      this.finishAndClose();
      return;
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

