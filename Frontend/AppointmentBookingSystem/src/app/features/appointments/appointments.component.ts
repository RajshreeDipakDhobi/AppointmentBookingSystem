import { Component, OnInit, inject, signal, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AppointmentService, AppointmentFilterParams } from '../../core/services/appointment.service';
import { ProviderService } from '../../core/services/provider.service';
import { ServiceProviderDto } from '../../core/models/provider.models';
import {
  AppointmentDto,
  AppointmentStatsDto,
  AppointmentStatus,
  UpdateAppointmentStatusDto
} from '../../core/models/appointment.models';
import { BookingModalComponent } from './booking-modal/booking-modal.component';
import { RescheduleModalComponent } from './reschedule-modal/reschedule-modal.component';
import { CancelModalComponent } from './cancel-modal/cancel-modal.component';
import { ServiceDispatchModalComponent } from './service-dispatch-modal/service-dispatch-modal.component';
import { PaginationComponent } from '../../shared/components/pagination/pagination.component';
import { AppDatePipe, AppTimePipe, AppStatusBadgePipe, AppInitialsPipe, AppPhonePipe } from '../../shared/pipes';
import { CopyToClipboardDirective } from '../../shared/directives';
import { getAvatarGradient, getInitials, isAppointmentPast, canModifyAppointment, canCancelAppointment } from '../../core/constants/app.constants';
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
    PaginationComponent,
    AppDatePipe,
    AppTimePipe,
    AppStatusBadgePipe,
    AppInitialsPipe,
    AppPhonePipe,
    CopyToClipboardDirective
  ],
  templateUrl: './appointments.component.html',
  styleUrls: ['./appointments.component.css']
})
export class AppointmentsComponent implements OnInit {
  private readonly appointmentService = inject(AppointmentService);
  private readonly providerService = inject(ProviderService);
  private readonly confirmService = inject(ConfirmService);
  private readonly authService = inject(AuthService);
  readonly getAvatarGradient = getAvatarGradient;
  readonly getInitials = getInitials;

  readonly isAdmin = this.authService.isAdmin;

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
    if (this.selectedStatus === AppointmentStatus.Rejected) return 'Rejected';
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

  // Reject Modal State
  selectedAppointmentForReject: AppointmentDto | null = null;
  rejectionReason = '';
  readonly isRejecting = signal<boolean>(false);

  readonly AppointmentStatus = AppointmentStatus;
  readonly isAppointmentPast = isAppointmentPast;
  readonly canCancel = canCancelAppointment;

  ngOnInit(): void {
    this.loadProviders();
    this.loadStats();
    this.loadAppointments();
    if (this.isServiceProvider) {
      this.loadIncomingRequests();
    }
  }

  loadIncomingRequests(): void {
    if (!this.isServiceProvider) return;
    this.appointmentService.getIncomingBroadcastRequests().subscribe({
      next: (requests) => this.incomingRequests.set(requests || []),
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

  openRejectModal(apt: AppointmentDto): void {
    this.closeActionDropdown();
    this.selectedAppointmentForReject = apt;
    this.rejectionReason = '';
  }

  closeRejectModal(): void {
    if (!this.isRejecting()) {
      this.selectedAppointmentForReject = null;
      this.rejectionReason = '';
    }
  }

  confirmReject(): void {
    const apt = this.selectedAppointmentForReject;
    if (!apt || this.isRejecting()) return;

    if (!this.rejectionReason.trim()) {
      this.errorMessage.set('Please provide a reason for rejecting the appointment.');
      setTimeout(() => this.errorMessage.set(''), 4000);
      return;
    }

    this.isRejecting.set(true);
    this.appointmentService.updateStatus(apt.id, {
      status: AppointmentStatus.Rejected,
      notes: this.rejectionReason.trim()
    }).subscribe({
      next: () => {
        this.isRejecting.set(false);
        this.selectedAppointmentForReject = null;
        this.rejectionReason = '';
        this.showSuccessNotification(`Appointment #${apt.appointmentNumber} rejected.`);
        this.loadStats();
        this.loadAppointments();
      },
      error: (err) => {
        this.isRejecting.set(false);
        const errorMsg = err.error?.message || 'Failed to reject appointment.';
        this.errorMessage.set(errorMsg);
      }
    });
  }

  viewDetails(apt: AppointmentDto): void {
    this.closeActionDropdown();
    this.selectedAppointmentForDetail = apt;
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

  // Helper checks for row action buttons
  canModify(apt: AppointmentDto): boolean {
    return canModifyAppointment(apt);
  }

  isTerminalStatus(status: AppointmentStatus | number | null | undefined): boolean {
    return status === AppointmentStatus.Cancelled ||
           status === AppointmentStatus.Completed ||
           status === AppointmentStatus.Rejected;
  }
}
