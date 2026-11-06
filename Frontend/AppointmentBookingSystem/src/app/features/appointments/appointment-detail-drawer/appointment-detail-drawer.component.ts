import { Component, EventEmitter, HostListener, Input, Output, inject, signal, computed, ChangeDetectorRef, NgZone, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AppointmentDto, AppointmentStatus } from '../../../core/models/appointment.models';
import { AuthService } from '../../../core/services/auth.service';
import {
  APPOINTMENT_STATUS_MAP,
  getAvatarGradient,
  getInitials,
  isAppointmentPast,
  canCancelAppointment,
  canCompleteAppointment
} from '../../../core/constants/app.constants';
import { AppDatePipe, AppTimePipe, AppPhonePipe } from '../../../shared/pipes';
import { CopyToClipboardDirective } from '../../../shared/directives';

@Component({
  selector: 'app-appointment-detail-drawer',
  standalone: true,
  imports: [
    CommonModule,
    AppDatePipe,
    AppTimePipe,
    AppPhonePipe,
    CopyToClipboardDirective
  ],
  templateUrl: './appointment-detail-drawer.component.html',
  styleUrls: ['./appointment-detail-drawer.component.css']
})
export class AppointmentDetailDrawerComponent implements OnChanges {
  private readonly authService = inject(AuthService);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly ngZone = inject(NgZone);

  private _appointment = signal<AppointmentDto | null>(null);

  @Input({ required: true })
  get appointment(): AppointmentDto | null {
    return this._appointment();
  }
  set appointment(val: AppointmentDto | null) {
    this._appointment.set(val);
    this.cdr.markForCheck();
  }

  @Input() canManage: boolean = false;
  @Input() showActions: boolean = true;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['appointment'] && changes['appointment'].currentValue) {
      this._appointment.set(changes['appointment'].currentValue);
      this.cdr.markForCheck();
    }
  }

  @Output() close = new EventEmitter<void>();
  @Output() reschedule = new EventEmitter<AppointmentDto>();
  @Output() cancel = new EventEmitter<AppointmentDto>();
  @Output() approve = new EventEmitter<AppointmentDto>();
  @Output() complete = new EventEmitter<AppointmentDto>();
  @Output() sessionSwitch = new EventEmitter<AppointmentDto>();

  readonly copied = signal<boolean>(false);
  readonly AppointmentStatus = AppointmentStatus;

  get isServiceProvider(): boolean {
    const user = this.authService.currentUser();
    if (!user) return false;
    return user.role === 3 || (user.role as unknown) === '3' || user.roleName === 'ServiceProvider';
  }

  get isAdmin(): boolean {
    return this.authService.isAdmin();
  }

  get effectiveCanManage(): boolean {
    if (this.canManage) return true;
    if (this.isServiceProvider && (this.appointment?.canManage ?? true)) return true;
    return false;
  }

  get isPending(): boolean {
    return this.appointment?.status === AppointmentStatus.Pending;
  }

  get isConfirmed(): boolean {
    return this.appointment?.status === AppointmentStatus.Approved;
  }

  get canComplete(): boolean {
    if (!this.appointment || !this.effectiveCanManage) return false;
    if (this.appointment.status !== AppointmentStatus.Approved) return false;
    if (this.isAdmin) return true;
    return canCompleteAppointment(this.appointment);
  }

  get isCompleted(): boolean {
    return this.appointment?.status === AppointmentStatus.Completed;
  }

  get isCancelled(): boolean {
    return this.appointment?.status === AppointmentStatus.Cancelled;
  }

  get isRescheduleProposed(): boolean {
    return this.appointment?.status === AppointmentStatus.RescheduleProposed;
  }

  get isPast(): boolean {
    return isAppointmentPast(this.appointment);
  }

  get durationText(): string {
    const mins = this.appointment?.totalDurationMinutes || this.appointment?.serviceDurationMinutes;
    if (!mins) return '';
    if (mins < 60) return `${mins}m`;
    const h = Math.floor(mins / 60);
    const m = mins % 60;
    return m > 0 ? `${h}h ${m}m` : `${h}h`;
  }

  get validJourneyParts(): AppointmentDto[] {
    if (!this.appointment?.journeyParts) return [];
    return this.appointment.journeyParts.filter(p => !this.isSessionUnavailable(p));
  }

  get isMultiSession(): boolean {
    return this.isAdmin && this.validJourneyParts.length > 1;
  }

  get currentSessionNumber(): number {
    if (!this.isMultiSession) return 1;
    const idx = this.validJourneyParts.findIndex(p => Number(p.id) === Number(this.appointment?.id));
    return idx >= 0 ? idx + 1 : 1;
  }

  get totalSessionsCount(): number {
    return this.validJourneyParts.length || 1;
  }

  get multiPartBadgeText(): string | null {
    if (!this.isAdmin || !this.isMultiSession) return null;
    return `Session ${this.currentSessionNumber} of ${this.totalSessionsCount}`;
  }

  readonly otherSessionsSignal = computed(() => {
    const apt = this._appointment();
    if (!this.isAdmin || !apt?.journeyParts || apt.journeyParts.length <= 1) return [];
    const validParts = apt.journeyParts.filter(p => !this.isSessionUnavailable(p));
    if (validParts.length <= 1) return [];
    return validParts
      .map((part, index) => ({ sessionNum: index + 1, part }))
      .filter(item => Number(item.part.id) !== Number(apt.id));
  });

  get otherSessions(): { sessionNum: number; part: AppointmentDto }[] {
    return this.otherSessionsSignal();
  }

  trackBySessionId(_index: number, item: { sessionNum: number; part: AppointmentDto }): number {
    return item.part.id;
  }

  trackByJourneyPartId(_index: number, part: AppointmentDto): number {
    return part.id;
  }

  get currentSessionSubtotal(): number {
    if (!this.appointment) return 0;
    if (this.appointment.items && this.appointment.items.length > 0) {
      return this.appointment.items.reduce((sum, item) => sum + (item.price || 0), 0);
    }
    return this.appointment.totalAmount || this.appointment.servicePrice || 0;
  }

  get totalVisitAmount(): number {
    if (this.isMultiSession && this.validJourneyParts.length > 1) {
      return this.validJourneyParts.reduce((sum, p) => {
        if (p.items && p.items.length > 0) {
          return sum + p.items.reduce((s, it) => s + (it.price || 0), 0);
        }
        return sum + (p.totalAmount || p.servicePrice || 0);
      }, 0);
    }
    return this.currentSessionSubtotal;
  }

  getSessionServiceTitle(part: AppointmentDto): string {
    if (part.items && part.items.length > 0) {
      const names = part.items.map(i => i.serviceName);
      if (names.length === 1) return names[0];
      if (names.length === 2) return `${names[0]} + ${names[1]}`;
      return `${names[0]} + ${names.length - 1} more`;
    }
    if (part.serviceNames && part.serviceNames.length > 0) {
      if (part.serviceNames.length === 1) return part.serviceNames[0];
      if (part.serviceNames.length === 2) return part.serviceNames.join(' + ');
      return `${part.serviceNames[0]} + ${part.serviceNames.length - 1} more`;
    }
    return part.serviceName || 'Service';
  }

  isSessionUnavailable(part?: AppointmentDto | null): boolean {
    if (!part) return true;
    const reason = ((part as any).cancellationReason || (part as any).cancelReason || '').toLowerCase();
    if (reason.includes('no specialist') || reason.includes('specialist unavailable')) return true;
    if (this.getSessionStatusLabel(part) === 'Specialist Unavailable') return true;
    const isCancelled = part.status === AppointmentStatus.Cancelled
      || (part.status as any) === 3
      || String(part.status).toLowerCase() === 'cancelled'
      || part.statusText?.toLowerCase().includes('unavailable');
    if (!part.serviceProviderId && isCancelled) {
      return true;
    }
    return false;
  }

  switchSession(part: AppointmentDto, event?: Event): void {
    if (event) {
      event.preventDefault();
      event.stopPropagation();
    }
    if (!this.appointment || !this.isAdmin) return;
    if (Number(part.id) === Number(this.appointment.id)) return;

    const existingJourneyParts = (this.appointment.journeyParts || []).filter(p => !this.isSessionUnavailable(p));
    const switched: AppointmentDto = {
      ...part,
      journeyParts: existingJourneyParts
    };
    this.ngZone.run(() => {
      this._appointment.set(switched);
      this.cdr.markForCheck();
      this.cdr.detectChanges();
      this.sessionSwitch.emit(switched);
    });
  }

  getSessionStatusLabel(part: AppointmentDto): string {
    const isCancelled = part.status === AppointmentStatus.Cancelled
      || (part.status as any) === 3
      || String(part.status).toLowerCase() === 'cancelled';
    if (!part.serviceProviderId && isCancelled) {
      return 'Specialist Unavailable';
    }
    if (!part.serviceProviderId) {
      return 'Awaiting Specialist';
    }

    const isPending = (part.status as any) === AppointmentStatus.Booked
      || (part.status as any) === 1
      || String(part.status).toLowerCase() === 'pending'
      || String(part.status).toLowerCase() === 'booked';

    if (isPending) {
      if (this.isServiceProvider) {
        const currentProviderId = this.authService.currentUser()?.serviceProviderId;
        const isMine = currentProviderId && Number(part.serviceProviderId) === Number(currentProviderId);
        return isMine ? 'Awaiting your approval' : 'Awaiting approval';
      }
      return 'Awaiting approval';
    }

    return this.getStatusLabel(part.status);
  }

  getSessionStatusClass(part: AppointmentDto): string {
    const isCancelled = part.status === AppointmentStatus.Cancelled
      || (part.status as any) === 3
      || String(part.status).toLowerCase() === 'cancelled';
    if (!part.serviceProviderId && isCancelled) {
      return 'badge-cancelled';
    }
    if (!part.serviceProviderId) {
      return 'badge-reschedule-proposed';
    }
    return this.getStatusBadgeClass(part.status);
  }

  get canModify(): boolean {
    if (!this.appointment || this.isPast) return false;
    return this.appointment.status === AppointmentStatus.Booked || this.appointment.status === AppointmentStatus.Approved;
  }

  get canCancel(): boolean {
    return canCancelAppointment(this.appointment);
  }

  getAvatarGradient(name?: string | null): string {
    return getAvatarGradient(name);
  }

  getInitials(name?: string | null): string {
    return getInitials(name);
  }

  getStatusBadgeClass(status?: AppointmentStatus): string {
    if (status === undefined || status === null) return 'badge-booked';
    return APPOINTMENT_STATUS_MAP[status]?.badgeClass || 'badge-booked';
  }

  getStatusLabel(status?: AppointmentStatus): string {
    const isPending = (status as any) === AppointmentStatus.Booked
      || (status as any) === 1
      || String(status).toLowerCase() === 'pending'
      || String(status).toLowerCase() === 'booked';

    if (isPending) {
      if (this.isServiceProvider) {
        const currentProviderId = this.authService.currentUser()?.serviceProviderId;
        const isMine = !this.appointment?.serviceProviderId
          || (currentProviderId && Number(this.appointment.serviceProviderId) === Number(currentProviderId))
          || (this.appointment?.canManage ?? true);

        return isMine ? 'Awaiting your approval' : 'Awaiting approval';
      }
      return 'Awaiting approval';
    }

    if (status === undefined || status === null) return 'Awaiting approval';
    return APPOINTMENT_STATUS_MAP[status]?.label || 'Awaiting approval';
  }

  getCleanCancellationReason(reason?: string | null): string | null {
    if (!reason) return null;
    return reason.replace(/\(Cancelled by [^)]+\)/gi, '').trim() || null;
  }

  getCleanReason(reason?: string | null): string | null {
    if (!reason) return null;
    const clean = reason.replace(/Part\s+\d+\s+of\s+\d+/gi, '').replace(/\(\s*\)/g, '').trim();
    if (clean.includes('Rescheduled via') || clean.length === 0) return null;
    return clean;
  }

  onIdCopied(): void {
    this.copied.set(true);
    setTimeout(() => this.copied.set(false), 2500);
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('drawer-backdrop')) {
      this.closeDrawer();
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.closeDrawer();
  }

  closeDrawer(): void {
    this.close.emit();
  }

  triggerReschedule(): void {
    if (this.appointment) {
      this.reschedule.emit(this.appointment);
    }
  }

  triggerCancel(): void {
    if (this.appointment) {
      this.cancel.emit(this.appointment);
    }
  }

  triggerApprove(): void {
    if (this.appointment) {
      this.approve.emit(this.appointment);
    }
  }

  triggerComplete(): void {
    if (this.appointment) {
      this.complete.emit(this.appointment);
    }
  }
}
