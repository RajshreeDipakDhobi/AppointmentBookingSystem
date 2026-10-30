import { Component, OnInit, OnDestroy, computed, inject, signal, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { TenantService } from '../../core/services/tenant.service';
import { AppointmentService } from '../../core/services/appointment.service';
import { ProviderService } from '../../core/services/provider.service';
import { LayoutService } from '../../core/services/layout.service';
import { TenantDto, TenantStatus } from '../../core/models/tenant.models';
import { AppointmentDto, AppointmentStatsDto, AppointmentStatus, AvailableSlotDto } from '../../core/models/appointment.models';
import { ServiceProviderDto } from '../../core/models/provider.models';
import { TenantModalComponent } from '../tenants/tenant-modal/tenant-modal.component';
import { TenantProfileModalComponent } from '../tenants/tenant-profile-modal/tenant-profile-modal.component';
import { BookingModalComponent } from '../appointments/booking-modal/booking-modal.component';
import { CustomerAppointmentsComponent } from '../customer/my-appointments/customer-appointments.component';
import { CalendarComponent } from '../calendar/calendar.component';
import { isAppointmentPast, canCancelAppointment } from '../../core/constants/app.constants';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterModule,
    TenantModalComponent,
    TenantProfileModalComponent,
    BookingModalComponent,
    CustomerAppointmentsComponent,
    CalendarComponent
  ],
  templateUrl: './dashboard.component.html',
  styleUrls: ['./dashboard.component.css']
})
export class DashboardComponent implements OnInit, OnDestroy {
  @ViewChild('adminCalendar') adminCalendar?: CalendarComponent;
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);
  private readonly tenantService = inject(TenantService);
  private readonly appointmentService = inject(AppointmentService);
  private readonly providerService = inject(ProviderService);
  private readonly layoutService = inject(LayoutService);
  private bookingModalSub?: Subscription;

  readonly tenants = signal<TenantDto[]>([]);
  readonly providers = signal<ServiceProviderDto[]>([]);
  readonly recentAppointments = signal<AppointmentDto[]>([]);
  readonly tenantStats = signal<AppointmentStatsDto | null>(null);

  readonly isLoading = signal<boolean>(false);
  readonly isModalOpen = signal<boolean>(false);
  readonly isProfileModalOpen = signal<boolean>(false);
  readonly selectedTenantForView = signal<TenantDto | null>(null);
  readonly isBookingModalOpen = signal<boolean>(false);
  readonly successMessage = signal<string>('');

  readonly AppointmentStatus = AppointmentStatus;

  get user() {
    return this.authService.currentUser();
  }

  get isSuperAdmin(): boolean {
    return this.user?.role === 1;
  }

  get isTenantAdmin(): boolean {
    return this.user?.role === 2;
  }

  get isServiceProvider(): boolean {
    return this.user?.role === 3;
  }

  get isCustomer(): boolean {
    return this.user?.role === 4;
  }

  // SuperAdmin Platform Metrics computed from real data
  readonly totalTenantsCount = computed(() => this.tenants().length);
  readonly activeTenantsCount = computed(() =>
    this.tenants().filter(t => t.status === TenantStatus.Active).length
  );
  readonly totalProvidersCount = computed(() =>
    this.tenants().reduce((acc, t) => acc + (t.totalProvidersCount || 0), 0)
  );
  readonly totalBookingsCount = computed(() =>
    this.tenants().reduce((acc, t) => acc + (t.totalAppointmentsCount || 0), 0)
  );
  readonly recentTenants = computed(() => this.tenants().slice(0, 5));

  // --- Tenant Admin Operational Summary (Directly Above Calendar) ---
  readonly adminTotalAppointments = computed(() => {
    const stats = this.tenantStats();
    if (stats && stats.totalAppointments !== undefined) {
      return stats.totalAppointments;
    }
    return this.recentAppointments().length;
  });

  readonly adminTodayAppointments = computed(() => {
    const stats = this.tenantStats();
    if (stats && stats.todayAppointments !== undefined) {
      return stats.todayAppointments;
    }
    const todayStr = new Date().toISOString().slice(0, 10);
    return this.recentAppointments().filter(a => {
      const d = a.appointmentDate?.slice(0, 10);
      return d === todayStr && a.status !== AppointmentStatus.Cancelled;
    }).length;
  });

  readonly adminUpcomingAppointments = computed(() => {
    const stats = this.tenantStats();
    if (stats && stats.upcomingAppointments !== undefined) {
      return stats.upcomingAppointments;
    }
    const todayStr = new Date().toISOString().slice(0, 10);
    return this.recentAppointments().filter(a => {
      const d = a.appointmentDate?.slice(0, 10);
      return d >= todayStr && (a.status === AppointmentStatus.Booked || a.status === AppointmentStatus.Approved);
    }).length;
  });

  readonly adminCancelledAppointments = computed(() => {
    const stats = this.tenantStats();
    if (stats && stats.cancelledAppointments !== undefined) {
      return stats.cancelledAppointments;
    }
    return this.recentAppointments().filter(a => a.status === AppointmentStatus.Cancelled).length;
  });

  // --- Customer Portal Specific Signals ---
  readonly customerAppointments = signal<AppointmentDto[]>([]);
  readonly activeCustomerTab = signal<'upcoming' | 'history'>('upcoming');

  readonly upcomingAppointments = computed(() => {
    return this.customerAppointments().filter(a => a.status === AppointmentStatus.Booked);
  });

  readonly historyAppointments = computed(() => {
    return this.customerAppointments().filter(a => a.status !== AppointmentStatus.Booked);
  });

  // Ticket modal
  readonly selectedTicket = signal<AppointmentDto | null>(null);

  // Reschedule modal
  readonly activeRescheduleAppt = signal<AppointmentDto | null>(null);
  rescheduleDate: string = '';
  minDate: string = '';
  readonly rescheduleSlots = signal<AvailableSlotDto[]>([]);
  selectedRescheduleSlot: AvailableSlotDto | null = null;
  readonly isLoadingRescheduleSlots = signal<boolean>(false);
  readonly isSubmittingReschedule = signal<boolean>(false);
  rescheduleReason: string = '';
  readonly rescheduleError = signal<string | null>(null);

  // Cancel modal
  readonly activeCancelAppt = signal<AppointmentDto | null>(null);
  cancellationReason: string = '';
  readonly isSubmittingCancel = signal<boolean>(false);
  readonly cancelError = signal<string | null>(null);

  // --- Modern Analytics & Charts Computations (Tenant Admin) ---
  readonly isAppointmentPast = isAppointmentPast;
  readonly hoveredChartPoint = signal<{ day: string; count: number; x: number; y: number } | null>(null);

  // 7-Day Activity Trend Data (Monday to Sunday)
  readonly weeklyActivity = computed(() => {
    const apts = this.recentAppointments();
    const dayNames = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
    const counts = [0, 0, 0, 0, 0, 0, 0];

    apts.forEach(apt => {
      try {
        const d = new Date(apt.appointmentDate);
        if (!isNaN(d.getTime())) {
          const dayIndex = (d.getDay() + 6) % 7; // Convert 0(Sun)-6(Sat) to 0(Mon)-6(Sun)
          counts[dayIndex]++;
        }
      } catch { }
    });

    const maxVal = Math.max(...counts, 1);
    const w = 520;
    const h = 160;
    const paddingX = 40;
    const paddingY = 24;
    const stepX = (w - 2 * paddingX) / 6;

    return dayNames.map((name, i) => {
      const count = counts[i];
      const x = Math.round(paddingX + i * stepX);
      const ratio = count / maxVal;
      const y = Math.round(h - paddingY - (ratio * (h - 2 * paddingY)));
      return {
        day: name,
        count: count,
        x,
        y,
        percentage: Math.round(ratio * 100)
      };
    });
  });

  // SVG Line & Area Paths for the Trend Chart
  readonly chartLinePath = computed(() => {
    const pts = this.weeklyActivity();
    if (pts.length === 0) return '';
    return pts.reduce((acc, p, i) => {
      if (i === 0) return `M ${p.x} ${p.y}`;
      const prev = pts[i - 1];
      const cx1 = prev.x + (p.x - prev.x) / 2;
      const cy1 = prev.y;
      const cx2 = prev.x + (p.x - prev.x) / 2;
      const cy2 = p.y;
      return `${acc} C ${cx1} ${cy1}, ${cx2} ${cy2}, ${p.x} ${p.y}`;
    }, '');
  });

  readonly chartAreaPath = computed(() => {
    const pts = this.weeklyActivity();
    if (pts.length === 0) return '';
    const line = this.chartLinePath();
    const last = pts[pts.length - 1];
    const first = pts[0];
    const bottomY = 150;
    return `${line} L ${last.x} ${bottomY} L ${first.x} ${bottomY} Z`;
  });

  // Status Breakdown Percentages
  readonly statusAnalytics = computed(() => {
    const stats = this.tenantStats();
    const total = stats?.totalAppointments || this.recentAppointments().length || 1;
    const booked = stats?.upcomingAppointments ?? this.recentAppointments().filter(a => a.status === AppointmentStatus.Booked).length;
    const completed = stats?.completedAppointments ?? this.recentAppointments().filter(a => a.status === AppointmentStatus.Completed).length;
    const cancelled = stats?.cancelledAppointments ?? this.recentAppointments().filter(a => a.status === AppointmentStatus.Cancelled).length;

    const bookedPct = Math.min(100, Math.round((booked / total) * 100)) || 0;
    const completedPct = Math.min(100, Math.round((completed / total) * 100)) || 0;
    const cancelledPct = Math.min(100, Math.round((cancelled / total) * 100)) || 0;

    return {
      total,
      booked,
      bookedPct,
      completed,
      completedPct,
      cancelled,
      cancelledPct,
      fulfillmentRate: (completed + booked > 0) ? Math.round((completed / (completed + cancelled || 1)) * 100) : 100
    };
  });

  // Up to 5 priority appointments for the visual feed
  readonly displayAppointments = computed(() => {
    return this.recentAppointments().slice(0, 5);
  });

  // Top active specialists for the roster showcase
  readonly displayProviders = computed(() => {
    return this.providers().slice(0, 4);
  });

  ngOnInit(): void {
    if (this.isServiceProvider) {
      this.router.navigate(['/appointments']);
      return;
    }
    if (this.isSuperAdmin) {
      this.loadPlatformData();
    } else if (this.isTenantAdmin) {
      this.loadTenantAdminData();
    } else if (this.isCustomer) {
      this.loadCustomerData();
    }

    this.bookingModalSub = this.layoutService.openBookingModal$.subscribe(() => {
      this.openBookingModal();
    });
  }

  ngOnDestroy(): void {
    this.bookingModalSub?.unsubscribe();
  }

  loadPlatformData(): void {
    this.isLoading.set(true);
    this.tenantService.getAllTenants({ pageNumber: 1, pageSize: 50 }).subscribe({
      next: (data) => {
        this.tenants.set(data.items);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      }
    });
  }

  loadTenantAdminData(): void {
    this.isLoading.set(true);

    this.appointmentService.getStats().subscribe({
      next: (stats) => this.tenantStats.set(stats),
      error: (err) => console.error('Failed to load tenant stats', err)
    });

    this.providerService.getAllProviders().subscribe({
      next: (data) => this.providers.set(data),
      error: (err) => console.error('Failed to load providers', err)
    });

    this.appointmentService.getAppointments({ pageNumber: 1, pageSize: 25 }).subscribe({
      next: (data) => {
        this.recentAppointments.set(data.items);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      }
    });
  }

  loadCustomerData(): void {
    this.isLoading.set(true);
    this.appointmentService.getMyAppointments({ pageNumber: 1, pageSize: 5 }).subscribe({
      next: (data) => {
        this.customerAppointments.set(data.items);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      }
    });
  }

  openCreateModal(): void {
    this.isModalOpen.set(true);
  }

  onModalClose(): void {
    this.isModalOpen.set(false);
  }

  openViewModal(tenant: TenantDto): void {
    this.selectedTenantForView.set(tenant);
    this.isProfileModalOpen.set(true);
  }

  onProfileModalClose(): void {
    this.isProfileModalOpen.set(false);
    this.selectedTenantForView.set(null);
  }

  onTenantSaved(newTenant: TenantDto): void {
    this.isModalOpen.set(false);
    this.successMessage.set(`Organization "${newTenant.name}" registered successfully!`);
    this.loadPlatformData();
    setTimeout(() => this.successMessage.set(''), 4000);
  }

  openBookingModal(): void {
    this.isBookingModalOpen.set(true);
  }

  onAppointmentBooked(apt: AppointmentDto): void {
    this.isBookingModalOpen.set(false);
    this.successMessage.set(`Appointment ${apt.appointmentNumber} booked successfully!`);
    if (this.isCustomer) {
      this.loadCustomerData();
    } else {
      this.loadTenantAdminData();
      this.adminCalendar?.fetchAppointments();
    }
    setTimeout(() => this.successMessage.set(''), 4000);
  }

  // --- Customer Actions ---
  viewTicket(appt: AppointmentDto): void {
    this.selectedTicket.set(appt);
  }

  closeTicket(): void {
    this.selectedTicket.set(null);
  }

  openReschedule(appt: AppointmentDto): void {
    this.activeRescheduleAppt.set(appt);
    this.rescheduleError.set(null);
    this.rescheduleSlots.set([]);
    this.selectedRescheduleSlot = null;
    this.rescheduleReason = '';

    const today = new Date();
    this.minDate = today.toISOString().split('T')[0];
    this.rescheduleDate = this.minDate;
    this.onRescheduleDateChange();
  }

  closeReschedule(): void {
    this.activeRescheduleAppt.set(null);
  }

  onRescheduleDateChange(): void {
    const appt = this.activeRescheduleAppt();
    if (!appt || !this.rescheduleDate) return;

    this.isLoadingRescheduleSlots.set(true);
    this.selectedRescheduleSlot = null;

    const slots$ = appt.serviceProviderId
      ? this.appointmentService.getAvailableSlots(appt.serviceProviderId, this.rescheduleDate)
      : (appt.serviceId
        ? this.appointmentService.getAvailableSlotsByService(appt.serviceId, this.rescheduleDate)
        : null);

    if (!slots$) {
      this.rescheduleSlots.set([]);
      this.isLoadingRescheduleSlots.set(false);
      return;
    }

    slots$.subscribe({
      next: (res) => {
        this.rescheduleSlots.set(res?.slots || []);
        this.isLoadingRescheduleSlots.set(false);
      },
      error: () => {
        this.rescheduleSlots.set([]);
        this.isLoadingRescheduleSlots.set(false);
      }
    });
  }

  onRescheduleSlotSelect(slot: AvailableSlotDto): void {
    this.selectedRescheduleSlot = slot;
  }

  confirmReschedule(): void {
    const appt = this.activeRescheduleAppt();
    if (!appt || !this.selectedRescheduleSlot) return;

    this.isSubmittingReschedule.set(true);
    this.rescheduleError.set(null);

    this.appointmentService.rescheduleAppointment(appt.id, {
      newDate: this.rescheduleDate,
      newStartTime: this.selectedRescheduleSlot.startTime,
      newEndTime: this.selectedRescheduleSlot.endTime,
      reason: this.rescheduleReason || 'Rescheduled by customer'
    }).subscribe({
      next: () => {
        this.isSubmittingReschedule.set(false);
        this.closeReschedule();
        this.successMessage.set(`Appointment ${appt.appointmentNumber} rescheduled successfully!`);
        this.loadCustomerData();
        setTimeout(() => this.successMessage.set(''), 4000);
      },
      error: (err) => {
        this.isSubmittingReschedule.set(false);
        this.rescheduleError.set(err.error?.message || 'Failed to reschedule. Please try a different slot.');
      }
    });
  }

  openCancel(appt: AppointmentDto): void {
    if (!canCancelAppointment(appt)) {
      this.cancelError.set('Appointments can only be cancelled at least 1 hour prior to scheduled time.');
      return;
    }

    this.activeCancelAppt.set(appt);
    this.cancellationReason = '';
    this.cancelError.set(null);
  }

  closeCancel(): void {
    this.activeCancelAppt.set(null);
  }

  confirmCancel(): void {
    const appt = this.activeCancelAppt();
    if (!appt) return;

    if (!canCancelAppointment(appt)) {
      this.cancelError.set('Appointments can only be cancelled at least 1 hour prior to scheduled time.');
      return;
    }

    this.isSubmittingCancel.set(true);
    this.cancelError.set(null);

    this.appointmentService.cancelAppointment(appt.id, {
      cancellationReason: this.cancellationReason || 'Cancelled by customer'
    }).subscribe({
      next: () => {
        this.isSubmittingCancel.set(false);
        this.closeCancel();
        this.successMessage.set(`Appointment ${appt.appointmentNumber} has been cancelled.`);
        this.loadCustomerData();
        setTimeout(() => this.successMessage.set(''), 4000);
      },
      error: (err) => {
        this.isSubmittingCancel.set(false);
        this.cancelError.set(err.error?.message || 'Failed to cancel appointment.');
      }
    });
  }

  readonly copiedApptId = signal<string | null>(null);

  get customerFirstName(): string {
    if (!this.user?.fullName) return 'Customer';
    return this.user.fullName.trim().split(/\s+/)[0];
  }

  copyAppointmentRef(ref: string, event: MouseEvent): void {
    event.stopPropagation();
    if (navigator?.clipboard?.writeText) {
      navigator.clipboard.writeText(ref).then(() => {
        this.copiedApptId.set(ref);
        setTimeout(() => this.copiedApptId.set(null), 2000);
      }).catch(() => {
        this.fallbackCopy(ref);
      });
    } else {
      this.fallbackCopy(ref);
    }
  }

  private fallbackCopy(text: string): void {
    const el = document.createElement('textarea');
    el.value = text;
    document.body.appendChild(el);
    el.select();
    document.execCommand('copy');
    document.body.removeChild(el);
    this.copiedApptId.set(text);
    setTimeout(() => this.copiedApptId.set(null), 2000);
  }

  getAvatarGradient(name: string): string {
    const gradients = [
      'linear-gradient(135deg, #6366f1 0%, #a855f7 100%)',
      'linear-gradient(135deg, #06b6d4 0%, #3b82f6 100%)',
      'linear-gradient(135deg, #ec4899 0%, #f43f5e 100%)',
      'linear-gradient(135deg, #10b981 0%, #059669 100%)',
      'linear-gradient(135deg, #f59e0b 0%, #d97706 100%)',
      'linear-gradient(135deg, #8b5cf6 0%, #ec4899 100%)'
    ];
    let hash = 0;
    const str = name || 'Provider';
    for (let i = 0; i < str.length; i++) {
      hash = str.charCodeAt(i) + ((hash << 5) - hash);
    }
    const index = Math.abs(hash) % gradients.length;
    return gradients[index];
  }

  getInitials(name: string): string {
    if (!name) return 'SP';
    const parts = name.trim().split(/\s+/);
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return name.slice(0, 2).toUpperCase();
  }
}
