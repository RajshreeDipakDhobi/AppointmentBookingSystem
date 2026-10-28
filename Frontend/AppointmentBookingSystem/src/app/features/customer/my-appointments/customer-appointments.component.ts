import { Component, OnInit, OnDestroy, computed, inject, signal, ElementRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../../../core/services/auth.service';
import { AppointmentService } from '../../../core/services/appointment.service';
import { LayoutService } from '../../../core/services/layout.service';
import { AppointmentDto, AppointmentStatus, AppointmentStatsDto } from '../../../core/models/appointment.models';
import { BookingModalComponent } from '../../appointments/booking-modal/booking-modal.component';
import { BookingTicketComponent } from '../../../shared/components/booking-ticket/booking-ticket.component';
import { RescheduleModalComponent } from '../../appointments/reschedule-modal/reschedule-modal.component';
import { CancelModalComponent } from '../../appointments/cancel-modal/cancel-modal.component';
import { AppDatePipe, AppTimePipe, AppStatusBadgePipe, AppInitialsPipe } from '../../../shared/pipes';
import { CopyToClipboardDirective } from '../../../shared/directives';
import { getAvatarGradient, isAppointmentPast, canModifyAppointment, canCancelAppointment } from '../../../core/constants/app.constants';

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
  readonly selectedTicket = signal<AppointmentDto | null>(null);
  readonly activeRescheduleAppt = signal<AppointmentDto | null>(null);
  readonly activeCancelAppt = signal<AppointmentDto | null>(null);
  readonly copiedApptId = signal<string | null>(null);
  readonly isAppointmentPast = isAppointmentPast;
  readonly canModify = canModifyAppointment;
  readonly canCancel = canCancelAppointment;

  get user() {
    return this.authService.currentUser();
  }

  // --- Metrics computed from customer stats endpoint ---
  readonly totalBookingsCount = computed(() => this.stats()?.totalAppointments ?? this.totalCount());
  readonly upcomingCount = computed(() => this.stats()?.upcomingAppointments ?? 0);
  readonly completedCount = computed(() => this.stats()?.completedAppointments ?? 0);
  readonly cancelledCount = computed(() => this.stats()?.cancelledAppointments ?? 0);

  // For compatibility with template
  readonly filteredAppointments = computed(() => this.appointments());

  ngOnInit(): void {
    this.loadStats();
    this.loadAppointments();

    this.bookingModalSub = this.layoutService.openBookingModal$.subscribe(() => {
      this.openBookingModal();
    });
  }

  ngOnDestroy(): void {
    this.bookingModalSub?.unsubscribe();
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

  // --- Modal Openers ---
  openBookingModal(): void {
    this.isBookingModalOpen.set(true);
  }

  onAppointmentBooked(apt: AppointmentDto): void {
    this.isBookingModalOpen.set(false);
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
}
