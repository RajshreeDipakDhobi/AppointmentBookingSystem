import { Component, OnInit, OnDestroy, computed, inject, signal, ElementRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { AppointmentService } from '../../../core/services/appointment.service';
import { LayoutService } from '../../../core/services/layout.service';
import { AppointmentDto, AppointmentStatus, AppointmentStatsDto, AppointmentItemDto } from '../../../core/models/appointment.models';
import { BookingModalComponent } from '../../appointments/booking-modal/booking-modal.component';
import { BookingTicketComponent } from '../../../shared/components/booking-ticket/booking-ticket.component';
import { RescheduleModalComponent } from '../../appointments/reschedule-modal/reschedule-modal.component';
import { CancelModalComponent } from '../../appointments/cancel-modal/cancel-modal.component';
import { AppDatePipe, AppTimePipe, AppStatusBadgePipe, AppInitialsPipe } from '../../../shared/pipes';
import { CopyToClipboardDirective } from '../../../shared/directives';
import { getAvatarGradient, isAppointmentPast, canModifyAppointment, canCancelAppointment, parseServerUtcDate } from '../../../core/constants/app.constants';

@Component({
  selector: 'app-customer-appointments',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterModule,
    BookingModalComponent,
    BookingTicketComponent,
    RescheduleModalComponent,
    CancelModalComponent,
    AppDatePipe,
    AppTimePipe,
    AppStatusBadgePipe,
    AppInitialsPipe,
    CopyToClipboardDirective
  ],
  templateUrl: './customer-appointments.component.html',
  styleUrls: ['./customer-appointments.component.css']
})
export class CustomerAppointmentsComponent implements OnInit, OnDestroy {
  private readonly authService = inject(AuthService);
  private readonly appointmentService = inject(AppointmentService);
  private readonly layoutService = inject(LayoutService);
  private readonly elementRef = inject(ElementRef);

  private bookingModalSub?: Subscription;

  readonly isStatusDropdownOpen = signal<boolean>(false);

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.elementRef.nativeElement.contains(event.target)) {
      this.isStatusDropdownOpen.set(false);
    }
  }

  toggleStatusDropdown(event?: Event): void {
    if (event) {
      event.stopPropagation();
    }
    this.isStatusDropdownOpen.update(v => !v);
  }

  selectStatusFilter(filter: 'ALL' | 'UPCOMING' | 'COMPLETED' | 'CANCELLED'): void {
    this.isStatusDropdownOpen.set(false);
    this.onStatusFilterChange(filter);
  }

  resetStatusFilter(event?: Event): void {
    if (event) {
      event.stopPropagation();
    }
    this.isStatusDropdownOpen.set(false);
    this.onStatusFilterChange('ALL');
  }

  readonly appointments = signal<AppointmentDto[]>([]);
  readonly stats = signal<AppointmentStatsDto | null>(null);
  readonly isLoading = signal<boolean>(false);
  readonly successMessage = signal<string>('');
  readonly errorMessage = signal<string>('');
  readonly totalCount = signal<number>(0);

  // Filters State
  searchQuery = signal<string>('');
  selectedStatusFilter = signal<'ALL' | 'UPCOMING' | 'COMPLETED' | 'CANCELLED'>('ALL');

  readonly AppointmentStatus = AppointmentStatus;

  // Modals state
  readonly isBookingModalOpen = signal<boolean>(false);
  readonly selectedServiceIdForModal = signal<number | null>(null);
  readonly selectedServiceIdsForModal = signal<number[]>([]);
  readonly activeTenantId = computed<number>(() => this.authService.currentUser()?.tenantId || this.appointments()[0]?.tenantId || 1);
  readonly selectedTicket = signal<AppointmentDto | null>(null);
  readonly activeRescheduleAppt = signal<AppointmentDto | null>(null);
  readonly activeCancelAppt = signal<AppointmentDto | null>(null);
  readonly copiedApptId = signal<string | null>(null);
  readonly isAppointmentPast = isAppointmentPast;
  readonly canModify = canModifyAppointment;
  readonly canCancel = canCancelAppointment;
  readonly now = signal<number>(Date.now());
  private timerIntervalId: any = null;
  private pollIntervalId: any = null;

  isTerminalStatus(status: AppointmentStatus | number | null | undefined): boolean {
    return status === AppointmentStatus.Cancelled || status === AppointmentStatus.Completed;
  }

  getItemCount(apt: AppointmentDto): number {
    if (apt.items && apt.items.length > 0) return apt.items.length;
    if (apt.serviceNames && apt.serviceNames.length > 0) return apt.serviceNames.length;
    return 1;
  }

  getTotalDuration(apt: AppointmentDto): number {
    if (apt.totalDurationMinutes && apt.totalDurationMinutes > 0) return apt.totalDurationMinutes;
    if (apt.items && apt.items.length > 0) {
      return apt.items.reduce((sum, it) => sum + (it.durationMinutes || 30), 0);
    }
    return apt.serviceDurationMinutes || 30;
  }

  getTotalPrice(apt: AppointmentDto): number {
    if (apt.totalAmount && apt.totalAmount > 0) return apt.totalAmount;
    if (apt.items && apt.items.length > 0) {
      return apt.items.reduce((sum, it) => sum + (it.price || 0), 0);
    }
    return apt.servicePrice || 0;
  }

  get user() {
    return this.authService.currentUser();
  }

  // --- Metrics computed from customer stats endpoint ---
  readonly totalBookingsCount = computed(() => this.stats()?.totalAppointments ?? this.totalCount());
  readonly upcomingCount = computed(() => this.stats()?.upcomingAppointments ?? 0);
  readonly completedCount = computed(() => this.stats()?.completedAppointments ?? 0);
  readonly cancelledCount = computed(() => this.stats()?.cancelledAppointments ?? 0);

  readonly filteredAppointments = computed<AppointmentDto[]>(() => {
    return this.groupMultiPartAppointments(this.appointments() || []);
  });

  private groupMultiPartAppointments(list: AppointmentDto[]): AppointmentDto[] {
    if (!list || list.length === 0) return [];
    const result: AppointmentDto[] = [];
    const groupedIds = new Set<number>();

    for (const apt of list) {
      if (groupedIds.has(apt.id)) continue;

      if (apt.journeyParts && apt.journeyParts.length > 1) {
        groupedIds.add(apt.id);
        result.push(apt);
        continue;
      }

      const parts = list.filter(other =>
        other.appointmentDate === apt.appointmentDate &&
        other.customerId === apt.customerId &&
        Math.abs(new Date(other.createdAt).getTime() - new Date(apt.createdAt).getTime()) < 120000
      );

      if (parts.length > 1) {
        parts.forEach(p => groupedIds.add(p.id));
        parts.sort((a, b) => (a.startTime || '').localeCompare(b.startTime || ''));

        const firstPart = parts[0];
        const lastPart = parts[parts.length - 1];

        const combinedItems: any[] = [];
        const combinedNames: string[] = [];
        let sumAmount = 0;
        let sumDur = 0;

        parts.forEach(p => {
          sumAmount += p.totalAmount || p.servicePrice || 0;
          sumDur += p.totalDurationMinutes || p.serviceDurationMinutes || 30;

          if (p.items && p.items.length > 0) {
            p.items.forEach(it => {
              combinedItems.push(it);
              if (it.serviceName && !combinedNames.includes(it.serviceName)) {
                combinedNames.push(it.serviceName);
              }
            });
          } else if (p.serviceName) {
            combinedItems.push({
              id: 0,
              appointmentId: p.id,
              serviceId: p.serviceId || 0,
              serviceName: p.serviceName,
              price: p.servicePrice || 0,
              durationMinutes: p.serviceDurationMinutes || 30
            });
            if (!combinedNames.includes(p.serviceName)) {
              combinedNames.push(p.serviceName);
            }
          }
        });

        const distinctProviders = parts
          .map(p => p.serviceProviderName)
          .filter((name): name is string => !!name && name.trim().length > 0);
        const uniqueProviders = Array.from(new Set(distinctProviders));
        const hasAssignedProviders = parts.some(p => !!p.serviceProviderId);

        const unified: AppointmentDto = {
          ...firstPart,
          id: firstPart.id,
          serviceProviderId: hasAssignedProviders ? (firstPart.serviceProviderId || parts.find(p => !!p.serviceProviderId)?.serviceProviderId || null) : null,
          startTime: firstPart.startTime,
          endTime: lastPart.endTime,
          totalAmount: sumAmount,
          totalDurationMinutes: sumDur,
          items: combinedItems,
          serviceNames: combinedNames,
          serviceName: combinedNames.join(' + '),
          serviceProviderName: uniqueProviders.length > 1 ? uniqueProviders.join(' • ') : (uniqueProviders[0] || firstPart.serviceProviderName),
          serviceProviderType: uniqueProviders.length > 1 ? 'Multi-Specialist Session' : firstPart.serviceProviderType,
          journeyParts: parts
        };

        result.push(unified);
        continue;
      }

      groupedIds.add(apt.id);
      result.push(apt);
    }

    return result;
  }

  ngOnInit(): void {
    this.loadStats();
    this.loadAppointments();

    this.bookingModalSub = this.layoutService.openBookingModal$.subscribe(() => {
      this.openBookingModal();
    });

    this.timerIntervalId = setInterval(() => {
      this.now.set(Date.now());
    }, 1000);

    this.pollIntervalId = setInterval(() => {
      const hasAwaiting = this.appointments().some(a => this.isAwaitingSpecialist(a));
      if (hasAwaiting) {
        this.silentReloadAppointments();
      }
    }, 8000);
  }

  ngOnDestroy(): void {
    this.bookingModalSub?.unsubscribe();
    if (this.timerIntervalId) clearInterval(this.timerIntervalId);
    if (this.pollIntervalId) clearInterval(this.pollIntervalId);
  }

  silentReloadAppointments(): void {
    let status: AppointmentStatus | undefined;
    if (this.selectedStatusFilter() === 'UPCOMING') status = AppointmentStatus.Booked;
    else if (this.selectedStatusFilter() === 'COMPLETED') status = AppointmentStatus.Completed;
    else if (this.selectedStatusFilter() === 'CANCELLED') status = AppointmentStatus.Cancelled;

    this.appointmentService.getMyAppointments(
      {
        pageNumber: 1,
        pageSize: 100,
        search: this.searchQuery().trim() || undefined
      },
      status
    ).subscribe({
      next: (res) => {
        this.appointments.set(res.items);
        this.totalCount.set(res.totalCount);
      }
    });
  }

  getRemainingSeconds(apt: AppointmentDto): number {
    if (!apt.createdAt) return 0;
    const createdTime = parseServerUtcDate(apt.createdAt);
    if (!createdTime) return 0;
    const diffSeconds = Math.floor((this.now() - createdTime) / 1000);
    return Math.min(600, Math.max(0, 10 * 60 - diffSeconds));
  }

  getRemainingPercent(apt: AppointmentDto): number {
    const rem = this.getRemainingSeconds(apt);
    return Math.max(0, Math.min(100, Math.round((rem / 600) * 100)));
  }

  getRemainingTimeDisplay(apt: AppointmentDto): string {
    const rem = this.getRemainingSeconds(apt);
    const mins = Math.floor(rem / 60);
    const secs = rem % 60;
    return `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')} remaining`;
  }

  isAdminBooking(apt: AppointmentDto): boolean {
    if (apt.createdBy && (
      apt.createdBy.includes('Admin') || 
      apt.createdBy.includes('SuperAdmin') || 
      apt.createdBy.includes('Staff')
    )) {
      return true;
    }
    if (apt.journeyParts && apt.journeyParts.some(p => 
      p.createdBy && (
        p.createdBy.includes('Admin') || 
        p.createdBy.includes('SuperAdmin') || 
        p.createdBy.includes('Staff')
      )
    )) {
      return true;
    }
    return false;
  }

  isAwaitingSpecialist(apt: AppointmentDto): boolean {
    if (this.isAdminBooking(apt)) return false;
    if (apt.serviceProviderId) return false;
    if (apt.journeyParts && apt.journeyParts.some(p => !!p.serviceProviderId)) return false;
    if (apt.serviceProviderName && apt.serviceProviderName !== '—' && !apt.serviceProviderName.includes('Auto-Dispatch') && !apt.serviceProviderName.includes('Awaiting')) return false;
    if (apt.status !== AppointmentStatus.Booked) return false;
    return this.getRemainingSeconds(apt) > 0;
  }

  isExpired(apt: AppointmentDto): boolean {
    if (this.isAdminBooking(apt)) return false;
    if (apt.serviceProviderId) return false;
    if (apt.journeyParts && apt.journeyParts.some(p => !!p.serviceProviderId)) return false;
    if (apt.serviceProviderName && apt.serviceProviderName !== '—' && !apt.serviceProviderName.includes('Auto-Dispatch') && !apt.serviceProviderName.includes('Awaiting')) return false;
    if (apt.status === AppointmentStatus.Cancelled) {
      return !!(apt.cancellationReason && (apt.cancellationReason.includes('10 minutes') || apt.cancellationReason.includes('specialist')));
    }
    if (apt.status === AppointmentStatus.Booked) {
      return this.getRemainingSeconds(apt) <= 0;
    }
    return false;
  }

  canRebookOrChooseSlot(apt: AppointmentDto): boolean {
    if (this.hasPartialApproval(apt)) return false;
    // Admin bookings must NEVER give customer the "Choose Slot" / "Select Another Slot" option
    if (this.isAdminBooking(apt)) return false;
    // Provider-assigned bookings waiting for provider approval must NOT give choose slot
    if (apt.serviceProviderId && apt.status === AppointmentStatus.Booked) return false;
    if (apt.journeyParts && apt.journeyParts.some(p => !!p.serviceProviderId) && apt.status === AppointmentStatus.Booked) return false;
    if (apt.status === AppointmentStatus.RescheduleProposed) return false;
    if (apt.status === AppointmentStatus.Approved) return false;

    // Only allow for expired customer broadcast or cancelled due to specialist unavailability
    if (this.isExpired(apt)) return true;
    if (apt.status === AppointmentStatus.Cancelled) {
      return !!(apt.cancellationReason && (
        apt.cancellationReason.includes('10 minutes') || 
        apt.cancellationReason.includes('specialist') || 
        apt.cancellationReason.includes('unavailable')
      ));
    }
    return false;
  }

  rebookService(apt: AppointmentDto): void {
    sessionStorage.removeItem('pending_booking_draft');
    let ids: number[] = [];
    if (apt.items && apt.items.length > 0) {
      ids = apt.items.map(it => it.serviceId).filter(id => id > 0);
    } else if (apt.serviceId) {
      ids = [apt.serviceId];
    }
    this.selectedServiceIdsForModal.set(ids);
    this.selectedServiceIdForModal.set(ids.length > 0 ? ids[0] : null);
    this.isBookingModalOpen.set(true);
  }

  // --- Partial Acceptance Helpers ---
  hasPartialApproval(apt: AppointmentDto): boolean {
    return !!apt.hasPartialApproval && !!apt.journeyParts && apt.journeyParts.length > 1;
  }

  getApprovedParts(apt: AppointmentDto): AppointmentDto[] {
    return apt.journeyParts ? apt.journeyParts.filter(p => p.status === AppointmentStatus.Approved) : [];
  }

  getUnacceptedParts(apt: AppointmentDto): AppointmentDto[] {
    return apt.journeyParts ? apt.journeyParts.filter(p => p.status !== AppointmentStatus.Approved && !this.isAwaitingSpecialist(p)) : [];
  }

  getAwaitingParts(apt: AppointmentDto): AppointmentDto[] {
    return apt.journeyParts ? apt.journeyParts.filter(p => this.isAwaitingSpecialist(p)) : [];
  }

  getPartServicesDisplay(part: AppointmentDto): string {
    if (part.items && part.items.length > 0) {
      return part.items.map(it => it.serviceName).join(', ');
    }
    return part.serviceName || 'Service';
  }

  onRebookPartFromTicket(event: { part: AppointmentDto; parent: AppointmentDto }): void {
    this.closeTicket();
    this.rebookSpecificPart(event.part, event.parent);
  }

  rebookSpecificPart(part: AppointmentDto, parentApt: AppointmentDto): void {
    sessionStorage.removeItem('pending_booking_draft');
    const serviceIds: number[] = [];
    if (part.items && part.items.length > 0) {
      part.items.forEach(it => serviceIds.push(it.serviceId));
    } else if (part.serviceId) {
      serviceIds.push(part.serviceId);
    }

    if (serviceIds.length === 0) {
      this.rebookService(parentApt);
      return;
    }

    this.selectedServiceIdsForModal.set(serviceIds);
    this.selectedServiceIdForModal.set(serviceIds[0]);
    this.isBookingModalOpen.set(true);
  }

  openBookingModal(serviceId?: number): void {
    sessionStorage.removeItem('pending_booking_draft');
    this.selectedServiceIdsForModal.set(serviceId ? [serviceId] : []);
    this.selectedServiceIdForModal.set(serviceId || null);
    this.isBookingModalOpen.set(true);
  }

  closeBookingModal(): void {
    sessionStorage.removeItem('pending_booking_draft');
    this.selectedServiceIdsForModal.set([]);
    this.selectedServiceIdForModal.set(null);
    this.isBookingModalOpen.set(false);
  }

  loadStats(): void {
    this.appointmentService.getMyStats().subscribe({
      next: (s) => this.stats.set(s),
      error: () => console.error('Failed to load customer appointment stats.')
    });
  }

  loadAppointments(): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    let status: AppointmentStatus | undefined;
    if (this.selectedStatusFilter() === 'UPCOMING') status = AppointmentStatus.Booked;
    else if (this.selectedStatusFilter() === 'COMPLETED') status = AppointmentStatus.Completed;
    else if (this.selectedStatusFilter() === 'CANCELLED') status = AppointmentStatus.Cancelled;

    this.appointmentService.getMyAppointments(
      {
        pageNumber: 1,
        pageSize: 100,
        search: this.searchQuery().trim() || undefined
      },
      status
    ).subscribe({
      next: (res) => {
        this.appointments.set(res.items);
        this.totalCount.set(res.totalCount);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.errorMessage.set('Failed to load appointments. Please refresh the page.');
      }
    });
  }

  onStatusFilterChange(filter: 'ALL' | 'UPCOMING' | 'COMPLETED' | 'CANCELLED'): void {
    this.selectedStatusFilter.set(filter);
    this.loadAppointments();
  }

  onSearchChange(query: string): void {
    this.searchQuery.set(query);
    this.loadAppointments();
  }

  onAppointmentBooked(apt: AppointmentDto): void {
    this.closeBookingModal();
    this.successMessage.set(`Appointment ${apt.appointmentNumber} booked successfully!`);
    this.loadStats();
    this.loadAppointments();
    setTimeout(() => this.successMessage.set(''), 4000);
  }

  viewTicket(appt: AppointmentDto): void {
    this.selectedTicket.set(appt);
  }

  closeTicket(): void {
    this.selectedTicket.set(null);
  }

  // --- Reschedule Flow with Shared Component ---
  openReschedule(appt: AppointmentDto): void {
    if (!this.canModify(appt)) {
      this.errorMessage.set('This appointment has not been accepted by a specialist yet and cannot be rescheduled.');
      setTimeout(() => this.errorMessage.set(''), 4000);
      return;
    }
    this.activeRescheduleAppt.set(appt);
  }

  onRescheduled(updated: AppointmentDto): void {
    this.activeRescheduleAppt.set(null);
    this.successMessage.set(`Appointment ${updated.appointmentNumber} rescheduled successfully!`);
    this.loadStats();
    this.loadAppointments();
    setTimeout(() => this.successMessage.set(''), 4000);
  }

  // --- Cancel Flow with Shared Component ---
  openCancel(appt: AppointmentDto): void {
    if (!canCancelAppointment(appt)) {
      this.errorMessage.set('Appointments can only be cancelled at least 1 hour prior to the scheduled start time.');
      setTimeout(() => this.errorMessage.set(''), 4000);
      return;
    }
    this.activeCancelAppt.set(appt);
  }

  onCancelled(updated: AppointmentDto): void {
    this.activeCancelAppt.set(null);
    this.successMessage.set(`Appointment ${updated.appointmentNumber} has been cancelled.`);
    this.loadStats();
    this.loadAppointments();
    setTimeout(() => this.successMessage.set(''), 4000);
  }

  onCopied(ref: string): void {
    this.copiedApptId.set(ref);
    setTimeout(() => this.copiedApptId.set(null), 2000);
  }

  readonly isRespondingProposal = signal<boolean>(false);

  acceptProposal(appt: AppointmentDto): void {
    if (this.isRespondingProposal()) return;
    this.isRespondingProposal.set(true);
    this.appointmentService.respondToProposal(appt.id, true).subscribe({
      next: (res) => {
        this.isRespondingProposal.set(false);
        this.successMessage.set(`Proposed slot accepted! Appointment #${res.appointmentNumber} is confirmed.`);
        this.loadStats();
        this.loadAppointments();
        setTimeout(() => this.successMessage.set(''), 5000);
      },
      error: (err) => {
        this.isRespondingProposal.set(false);
        this.errorMessage.set(err.error?.message || 'Failed to accept proposal.');
        setTimeout(() => this.errorMessage.set(''), 5000);
      }
    });
  }

  declineProposal(appt: AppointmentDto): void {
    if (this.isRespondingProposal()) return;
    this.isRespondingProposal.set(true);
    this.appointmentService.respondToProposal(appt.id, false).subscribe({
      next: (res) => {
        this.isRespondingProposal.set(false);
        this.successMessage.set(`Reschedule proposal declined. Appointment #${res.appointmentNumber} has been cancelled.`);
        this.loadStats();
        this.loadAppointments();
        setTimeout(() => this.successMessage.set(''), 5000);
      },
      error: (err) => {
        this.isRespondingProposal.set(false);
        this.errorMessage.set(err.error?.message || 'Failed to decline proposal.');
        setTimeout(() => this.errorMessage.set(''), 5000);
      }
    });
  }

  getGradient(name: string): string {
    return getAvatarGradient(name);
  }

  getServiceDisplay(apt: AppointmentDto): string {
    const names = apt.serviceNames && apt.serviceNames.length > 0
      ? apt.serviceNames
      : (apt.items && apt.items.length > 0 ? apt.items.map(i => i.serviceName) : (apt.serviceName ? [apt.serviceName] : []));

    if (names.length === 0) {
      return apt.serviceProviderType || 'General Service';
    }

    if (names.length === 1) {
      return names[0];
    }

    return `${names[0]} + ${names.length - 1} more`;
  }

  getFullServiceNames(apt: AppointmentDto): string {
    const names = apt.serviceNames && apt.serviceNames.length > 0
      ? apt.serviceNames
      : (apt.items && apt.items.length > 0 ? apt.items.map(i => i.serviceName) : (apt.serviceName ? [apt.serviceName] : []));
    return names.join(', ');
  }

  isSpecialistAssigned(apt: AppointmentDto): boolean {
    if (apt.serviceProviderId) return true;
    if (apt.journeyParts && apt.journeyParts.some(p => !!p.serviceProviderId)) return true;
    const name = apt.serviceProviderName?.trim();
    if (!name || name.toLowerCase().includes('dispatching') || name === '—' || name.toLowerCase().includes('awaiting')) return false;
    return true;
  }
}
