import { Component, OnInit, OnDestroy, inject, signal, computed, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AppointmentService, AppointmentFilterParams } from '../../core/services/appointment.service';
import { ProviderService } from '../../core/services/provider.service';
import { ServiceProviderDto } from '../../core/models/provider.models';
import {
  AppointmentDto,
  AppointmentStatsDto,
  AppointmentStatus,
  UpdateAppointmentStatusDto,
  AppointmentItemDto
} from '../../core/models/appointment.models';
import { BookingModalComponent } from './booking-modal/booking-modal.component';
import { RescheduleModalComponent } from './reschedule-modal/reschedule-modal.component';
import { CancelModalComponent } from './cancel-modal/cancel-modal.component';
import { ServiceDispatchModalComponent } from './service-dispatch-modal/service-dispatch-modal.component';
import { AppointmentDetailDrawerComponent } from './appointment-detail-drawer/appointment-detail-drawer.component';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { AppDatePipe, AppTimePipe, AppStatusBadgePipe, AppInitialsPipe, AppPhonePipe } from '../../shared/pipes';
import { getAvatarGradient, getInitials, isAppointmentPast, canModifyAppointment, canCancelAppointment, canCompleteAppointment, parseServerUtcDate } from '../../core/constants/app.constants';
import { ConfirmService } from '../../shared/components/confirmation-modal/confirm.service';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-appointments',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    BookingModalComponent,
    RescheduleModalComponent,
    CancelModalComponent,
    ServiceDispatchModalComponent,
    AppointmentDetailDrawerComponent,
    PaginationComponent,
    AppDatePipe,
    AppTimePipe,
    AppStatusBadgePipe,
    AppInitialsPipe,
    AppPhonePipe
  ],
  templateUrl: './appointments.component.html',
  styleUrls: ['./appointments.component.css']
})
export class AppointmentsComponent implements OnInit, OnDestroy {
  private readonly appointmentService = inject(AppointmentService);
  private readonly providerService = inject(ProviderService);
  private readonly confirmService = inject(ConfirmService);
  private readonly authService = inject(AuthService);
  readonly getAvatarGradient = getAvatarGradient;
  readonly getInitials = getInitials;
  readonly now = signal<number>(Date.now());
  private timerId: any = null;

  readonly isAdmin = this.authService.isAdmin;
  readonly activeTenantId = computed<number | undefined>(() => this.authService.currentUser()?.tenantId);

  get isServiceProvider(): boolean {
    const u = this.authService.currentUser();
    if (!u) return false;
    return u.role === 3 || (u.role as unknown) === '3' || u.roleName === 'ServiceProvider';
  }

  get isTenantAdmin(): boolean {
    return this.authService.currentUser()?.role === 2;
  }

  readonly appointments = signal<AppointmentDto[]>([]);
  readonly incomingRequests = signal<AppointmentDto[]>([]);
  readonly stats = signal<AppointmentStatsDto | null>(null);
  readonly providers = signal<ServiceProviderDto[]>([]);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string>('');
  readonly successMessage = signal<string>('');

  // Pagination State
  readonly currentPage = signal<number>(1);
  readonly pageSize = signal<number>(5);
  readonly totalCount = signal<number>(0);
  readonly totalPages = signal<number>(1);


  // Filters State
  selectedProviderId: number | null = null;
  selectedStatus: AppointmentStatus | null = null;
  filterStartDate = '';
  filterEndDate = '';
  searchQuery = '';

  // Custom Glassmorphism Dropdowns State
  isProviderDropdownOpen = false;
  isStatusDropdownOpen = false;

  get selectedProvider(): ServiceProviderDto | undefined {
    if (this.selectedProviderId === null || this.selectedProviderId === undefined) return undefined;
    return this.providers().find(p => p.id === Number(this.selectedProviderId));
  }

  get selectedProviderLabel(): string {
    const p = this.selectedProvider;
    return p ? `${p.name} (${p.serviceType})` : 'All Service Providers';
  }

  get selectedStatusLabel(): string {
    if (this.selectedStatus === AppointmentStatus.Booked) return 'Pending Approval';
    if (this.selectedStatus === AppointmentStatus.Approved) return 'Approved';
    if (this.selectedStatus === AppointmentStatus.RescheduleProposed) return 'Reschedule Proposed';
    if (this.selectedStatus === AppointmentStatus.Completed) return 'Completed';
    if (this.selectedStatus === AppointmentStatus.Cancelled) return 'Cancelled';
    return 'All Status';
  }

  toggleProviderDropdown(event: Event): void {
    event.stopPropagation();
    this.isStatusDropdownOpen = false;
    this.isProviderDropdownOpen = !this.isProviderDropdownOpen;
  }

  selectProvider(providerId: number | null, event?: Event): void {
    if (event) event.stopPropagation();
    this.selectedProviderId = providerId;
    this.isProviderDropdownOpen = false;
    this.onFilterChange();
  }

  clearProviderSelection(event: Event): void {
    event.stopPropagation();
    this.selectProvider(null, event);
  }

  toggleStatusDropdown(event: Event): void {
    event.stopPropagation();
    this.isProviderDropdownOpen = false;
    this.isStatusDropdownOpen = !this.isStatusDropdownOpen;
  }

  selectStatus(status: AppointmentStatus | null, event?: Event): void {
    if (event) event.stopPropagation();
    this.selectedStatus = status;
    this.isStatusDropdownOpen = false;
    this.onFilterChange();
  }

  clearStatusSelection(event: Event): void {
    event.stopPropagation();
    this.selectStatus(null, event);
  }

  readonly openActionDropdownId = signal<number | null>(null);

  toggleActionDropdown(appointmentId: number, event: Event): void {
    event.stopPropagation();
    this.isProviderDropdownOpen = false;
    this.isStatusDropdownOpen = false;
    this.openActionDropdownId.update(curr => curr === appointmentId ? null : appointmentId);
  }

  closeActionDropdown(): void {
    this.openActionDropdownId.set(null);
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    this.isProviderDropdownOpen = false;
    this.isStatusDropdownOpen = false;
    this.openActionDropdownId.set(null);
  }


  // Modals State
  showBookingModal = false;
  showDispatchModal = false;
  selectedDispatchRequest: AppointmentDto | null = null;
  selectedAppointmentForReschedule: AppointmentDto | null = null;
  selectedAppointmentForCancel: AppointmentDto | null = null;
  selectedAppointmentForDetail: AppointmentDto | null = null;
  selectedAppointmentForComplete: AppointmentDto | null = null;
  readonly isCompleting = signal<boolean>(false);

  readonly AppointmentStatus = AppointmentStatus;
  readonly isAppointmentPast = isAppointmentPast;

  canCancel(apt: AppointmentDto | null | undefined): boolean {
    if (!apt || this.isTerminalStatus(apt.status) || !apt.serviceProviderId) return false;
    if (apt.status !== AppointmentStatus.Booked && apt.status !== AppointmentStatus.Approved) return false;
    return canCancelAppointment(apt);
  }

  // Admin & Providers: Display appointments grouped into single unified rows for multi-service bookings
  readonly displayedAppointments = computed<AppointmentDto[]>(() => {
    let list = this.appointments();
    if (!this.isAdmin()) {
      list = list.filter(a => !!a.serviceProviderId);
    }
    return this.groupMultiPartRequests(list || []);
  });

  ngOnInit(): void {
    this.loadProviders();
    this.loadStats();
    this.loadAppointments();
    if (this.isServiceProvider) {
      this.loadIncomingRequests();
    }
    this.timerId = setInterval(() => {
      this.now.set(Date.now());
      if (this.isServiceProvider) {
        this.loadIncomingRequests();
      }
    }, 5000);
  }

  ngOnDestroy(): void {
    if (this.timerId) {
      clearInterval(this.timerId);
    }
  }

  isExpired(apt: AppointmentDto): boolean {
    if (apt.serviceProviderId) return false;
    if (apt.status === AppointmentStatus.Cancelled) {
      return !!(apt.cancellationReason && (apt.cancellationReason.includes('10 minutes') || apt.cancellationReason.includes('specialist')));
    }
    if (!apt.createdAt) return false;
    const createdTime = parseServerUtcDate(apt.createdAt);
    if (!createdTime) return false;
    return (this.now() - createdTime) >= 10 * 60 * 1000;
  }

  getDispatchBadgeText(apt: AppointmentDto): string {
    if (this.isExpired(apt)) return 'Expired';
    if (!apt.createdAt) return 'Open Dispatch';
    const createdTime = parseServerUtcDate(apt.createdAt);
    if (!createdTime) return 'Open Dispatch';
    const remainingSec = Math.min(600, Math.max(0, 10 * 60 - Math.floor((this.now() - createdTime) / 1000)));
    const mins = Math.floor(remainingSec / 60);
    const secs = remainingSec % 60;
    return `${mins}:${secs.toString().padStart(2, '0')} left`;
  }

  getDispatchTooltip(apt: AppointmentDto): string {
    if (this.isExpired(apt)) {
      return 'Acceptance window has elapsed without specialist confirmation.';
    }
    return 'Broadcast request active for specialists to accept.';
  }

  private groupMultiPartRequests(list: AppointmentDto[]): AppointmentDto[] {
    if (!list || list.length === 0) return [];
    const result: AppointmentDto[] = [];
    const groupedIds = new Set<number>();

    for (const apt of list) {
      if (groupedIds.has(apt.id)) continue;

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

          const combinedItems: AppointmentItemDto[] = [];
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
            .filter((name): name is string => !!name && name.trim().length > 0 && name !== '—');
          const uniqueProviders = Array.from(new Set(distinctProviders));
          const hasAssignedProviders = parts.some(p => !!p.serviceProviderId);

          const unified: AppointmentDto = {
            ...firstPart,
            id: firstPart.id,
            serviceProviderId: hasAssignedProviders ? (firstPart.serviceProviderId || parts.find(p => !!p.serviceProviderId)?.serviceProviderId || 1) : null,
            serviceProviderName: uniqueProviders.length > 1 ? uniqueProviders.join(', ') : (uniqueProviders[0] || firstPart.serviceProviderName),
            serviceProviderType: uniqueProviders.length > 1 ? 'Multi-Specialist Session' : firstPart.serviceProviderType,
            startTime: firstPart.startTime,
            endTime: lastPart.endTime,
            totalAmount: sumAmount,
            totalDurationMinutes: sumDur,
            items: combinedItems,
            serviceNames: combinedNames,
            serviceName: combinedNames.join(' + '),
            journeyParts: parts
          };
          (unified as any).partIds = parts.map(p => p.id);

          result.push(unified);
          continue;
        }

      groupedIds.add(apt.id);
      result.push(apt);
    }

    return result;
  }

  loadIncomingRequests(): void {
    if (!this.isServiceProvider) return;
    this.appointmentService.getIncomingBroadcastRequests().subscribe({
      next: (requests) => {
        const activeRequests = (requests || []).filter(r => !this.isExpired(r));
        this.incomingRequests.set(activeRequests);
      },
      error: (err) => console.error('Failed to load incoming broadcast requests', err)
    });
  }

  openDispatchModal(req?: AppointmentDto): void {
    this.selectedDispatchRequest = req || null;
    this.showDispatchModal = true;
  }

  closeDispatchModal(): void {
    this.showDispatchModal = false;
    this.selectedDispatchRequest = null;
    this.loadIncomingRequests();
  }

  onDispatchAccepted(apt: AppointmentDto): void {
    this.showDispatchModal = false;
    this.selectedDispatchRequest = null;
    this.showSuccessNotification(`Appointment #${apt.appointmentNumber} accepted! It has been added to your schedule.`);
    this.loadIncomingRequests();
    this.loadStats();
    this.loadAppointments();
  }

  loadProviders(): void {
    if (!this.isAdmin()) return;
    this.providerService.getAllProviders().subscribe({
      next: (data) => this.providers.set(data),
      error: () => console.error('Failed to load providers for filters.')
    });
  }

  loadStats(): void {
    this.appointmentService.getStats().subscribe({
      next: (data) => this.stats.set(data),
      error: (err) => console.error('Failed to load stats', err)
    });
  }

  loadAppointments(): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    const filters: AppointmentFilterParams = {
      pageNumber: this.currentPage(),
      pageSize: this.pageSize()
    };
    if (this.selectedProviderId) filters.providerId = Number(this.selectedProviderId);
    if (this.selectedStatus) filters.status = Number(this.selectedStatus);
    if (this.filterStartDate) filters.startDate = this.filterStartDate;
    if (this.filterEndDate) filters.endDate = this.filterEndDate;
    if (this.searchQuery.trim()) filters.search = this.searchQuery.trim();

    this.appointmentService.getAppointments(filters).subscribe({
      next: (res) => {
        this.appointments.set(res.items);
        this.totalCount.set(res.totalCount);
        this.totalPages.set(res.totalPages);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.errorMessage.set(err.error?.message || 'Failed to load appointments.');
      }
    });
  }

  onFilterChange(): void {
    this.currentPage.set(1);
    this.loadAppointments();
  }

  setStatusFilter(status: AppointmentStatus | null): void {
    this.selectedStatus = status;
    this.currentPage.set(1);
    this.loadAppointments();
  }

  onPageChange(page: number): void {
    this.currentPage.set(page);
    this.loadAppointments();
  }

  onPageSizeChange(size: number): void {
    this.pageSize.set(size);
    this.currentPage.set(1);
    this.loadAppointments();
  }

  clearFilters(): void {
    this.selectedProviderId = null;
    this.selectedStatus = null;
    this.filterStartDate = '';
    this.filterEndDate = '';
    this.searchQuery = '';
    this.isProviderDropdownOpen = false;
    this.isStatusDropdownOpen = false;
    this.currentPage.set(1);
    this.loadAppointments();
  }

  // Action Triggers
  openBookingModal(): void {
    this.showBookingModal = true;
  }

  onAppointmentBooked(apt: AppointmentDto): void {
    this.showBookingModal = false;
    this.showSuccessNotification(`Appointment #${apt.appointmentNumber} scheduled successfully.`);
    this.loadStats();
    this.loadAppointments();
  }

  openRescheduleModal(apt: AppointmentDto): void {
    if (apt.status !== AppointmentStatus.Booked && apt.status !== AppointmentStatus.Approved) {
      this.errorMessage.set(`Cannot reschedule appointment #${apt.appointmentNumber} because its status is ${apt.statusText}.`);
      setTimeout(() => this.errorMessage.set(''), 6000);
      return;
    }
    if (!this.canModify(apt)) {
      this.errorMessage.set(`Cannot reschedule appointment #${apt.appointmentNumber}: Appointments can only be rescheduled at least 1 hour prior to the scheduled start time.`);
      setTimeout(() => this.errorMessage.set(''), 6000);
      return;
    }
    this.selectedAppointmentForReschedule = apt;
  }

  onAppointmentRescheduled(apt: AppointmentDto): void {
    this.selectedAppointmentForReschedule = null;
    const msg = apt.status === AppointmentStatus.RescheduleProposed
      ? `Proposed new slot for appointment #${apt.appointmentNumber} sent to customer for review.`
      : `Appointment #${apt.appointmentNumber} rescheduled to ${apt.formattedDate} at ${apt.timeSlotText}.`;
    this.showSuccessNotification(msg);
    this.loadStats();
    this.loadAppointments();
  }

  openCancelModal(apt: AppointmentDto): void {
    if (!canCancelAppointment(apt)) {
      this.errorMessage.set(`Cannot cancel appointment #${apt.appointmentNumber}: Cancellations must be made at least 1 hour prior to the scheduled time.`);
      setTimeout(() => this.errorMessage.set(''), 6000);
      return;
    }
    this.selectedAppointmentForCancel = apt;
  }

  onAppointmentCancelled(apt: AppointmentDto): void {
    this.selectedAppointmentForCancel = null;
    this.showSuccessNotification(`Appointment ${apt.appointmentNumber} cancelled and slot released.`);
    this.loadStats();
    this.loadAppointments();
  }

  markAsCompleted(apt: AppointmentDto): void {
    this.selectedAppointmentForComplete = apt;
  }

  closeCompleteModal(): void {
    if (!this.isCompleting()) {
      this.selectedAppointmentForComplete = null;
    }
  }

  confirmAndMarkCompleted(): void {
    const apt = this.selectedAppointmentForComplete;
    if (!apt || this.isCompleting()) return;

    this.isCompleting.set(true);
    const req: UpdateAppointmentStatusDto = {
      status: AppointmentStatus.Completed
    };

    this.appointmentService.updateStatus(apt.id, req).subscribe({
      next: () => {
        this.isCompleting.set(false);
        this.selectedAppointmentForComplete = null;
        this.showSuccessNotification(`Appointment ${apt.appointmentNumber} marked as completed.`);
        this.loadStats();
        this.loadAppointments();
      },
      error: (err) => {
        this.isCompleting.set(false);
        const errorMsg = err.error?.message || 'Failed to update appointment status.';
        this.errorMessage.set(errorMsg);
        this.confirmService.alert({
          title: 'Update Status Notice',
          message: errorMsg,
          variant: 'danger',
          buttonText: 'OK'
        });
      }
    });
  }

  approveAppointment(apt: AppointmentDto): void {
    this.closeActionDropdown();
    this.appointmentService.updateStatus(apt.id, { status: AppointmentStatus.Approved }).subscribe({
      next: () => {
        this.showSuccessNotification(`Appointment #${apt.appointmentNumber} has been approved.`);
        this.loadStats();
        this.loadAppointments();
      },
      error: (err) => {
        const errorMsg = err.error?.message || 'Failed to approve appointment.';
        this.errorMessage.set(errorMsg);
      }
    });
  }

  getLinkedGroupAppointments(apt: AppointmentDto): AppointmentDto[] {
    if (!apt) return [];
    if (apt.bookingGroupId) {
      const groupSiblings = this.appointments().filter(other => other.bookingGroupId === apt.bookingGroupId);
      if (groupSiblings.length > 1) {
        return groupSiblings.sort((a, b) => (a.startTime || '').localeCompare(b.startTime || ''));
      }
    }
    return [apt];
  }

  viewDetails(apt: AppointmentDto): void {
    this.closeActionDropdown();
    const siblings = this.getLinkedGroupAppointments(apt);
    const detailApt: AppointmentDto = siblings.length > 1 ? {
      ...apt,
      journeyParts: siblings
    } : apt;
    this.selectedAppointmentForDetail = detailApt;
  }

  onDrawerSessionSwitch(switchedApt: AppointmentDto): void {
    if (!switchedApt) return;
    const full = this.appointments().find(a => a.id === switchedApt.id);
    const toSet: AppointmentDto = full ? {
      ...full,
      journeyParts: switchedApt.journeyParts
    } : switchedApt;
    this.selectedAppointmentForDetail = toSet;
  }

  closeDetails(): void {
    this.selectedAppointmentForDetail = null;
  }

  onIdCopied(id: string): void {
    this.showSuccessNotification(`Copied appointment ID #${id} to clipboard.`);
  }

  private showSuccessNotification(msg: string): void {
    this.successMessage.set(msg);
    setTimeout(() => this.successMessage.set(''), 5000);
  }

  // Helper checks for row action buttons (Service Provider only; Admin strictly views)
  canApprove(apt: AppointmentDto): boolean {
    if (!apt || !this.isServiceProvider || !apt.serviceProviderId) return false;
    return apt.status === AppointmentStatus.Booked && !this.isAppointmentPast(apt);
  }

  canComplete(apt: AppointmentDto): boolean {
    if (!apt || !this.isServiceProvider || !apt.serviceProviderId) return false;
    return canCompleteAppointment(apt);
  }

  hasManageableActions(apt: AppointmentDto): boolean {
    if (!this.isServiceProvider || !apt || this.isTerminalStatus(apt.status) || !apt.serviceProviderId) return false;
    return this.canApprove(apt) || this.canComplete(apt) || this.canModify(apt) || this.canCancel(apt);
  }

  canModify(apt: AppointmentDto): boolean {
    if (!apt || !this.isServiceProvider || this.isTerminalStatus(apt.status) || !apt.serviceProviderId) return false;
    if (apt.status !== AppointmentStatus.Booked && apt.status !== AppointmentStatus.Approved) return false;
    return canModifyAppointment(apt);
  }

  isTerminalStatus(status: AppointmentStatus | number | null | undefined): boolean {
    return status === AppointmentStatus.Cancelled ||
           status === AppointmentStatus.Completed;
  }

  getCleanReason(reason?: string | null): string | null {
    if (!reason) return null;
    const clean = reason.replace(/Part\s+\d+\s+of\s+\d+/gi, '').replace(/\(\s*\)/g, '').trim();
    if (clean.includes('Rescheduled via') || clean.length === 0) return null;
    return clean;
  }
}
