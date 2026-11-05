import { Component, Input, Output, EventEmitter, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AppointmentDto, AppointmentStatus } from '../../../core/models/appointment.models';
import { AppDatePipe, AppTimePipe, AppPhonePipe, AppInitialsPipe } from '../../pipes';
import { CopyToClipboardDirective } from '../../directives';
import { isAppointmentPast, canCancelAppointment, canRescheduleAppointment } from '../../../core/constants/app.constants';

@Component({
  selector: 'app-booking-ticket',
  standalone: true,
  imports: [
    CommonModule,
    AppDatePipe,
    AppTimePipe,
    AppPhonePipe,
    AppInitialsPipe,
    CopyToClipboardDirective
  ],
  templateUrl: './booking-ticket.component.html',
  styleUrls: ['./booking-ticket.component.css']
})
export class BookingTicketComponent {
  @Input() appointment: AppointmentDto | null = null;
  @Input() showActions: boolean = true;
  @Input() allowReschedule: boolean = true;
  @Input() allowCancel: boolean = true;

  @Output() reschedule = new EventEmitter<AppointmentDto>();
  @Output() cancel = new EventEmitter<AppointmentDto>();
  @Output() rebookPart = new EventEmitter<{ part: AppointmentDto; parent: AppointmentDto }>();

  readonly copied = signal<boolean>(false);

  AppointmentStatus = AppointmentStatus;

  get isActionable(): boolean {
    if (!this.appointment) return false;
    return this.appointment.status === AppointmentStatus.Booked;
  }

  get isPast(): boolean {
    return isAppointmentPast(this.appointment);
  }

  get canCancel(): boolean {
    return canCancelAppointment(this.appointment);
  }

  get canReschedule(): boolean {
    return canRescheduleAppointment(this.appointment);
  }

  printTicket(): void {
    window.print();
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

  isSpecialistAssigned(appointment: AppointmentDto | null): boolean {
    if (!appointment) return false;
    if (!appointment.serviceProviderId) return false;
    const name = appointment.serviceProviderName?.trim();
    if (!name || name.toLowerCase().includes('dispatching') || name === '—') return false;
    return true;
  }

  getCleanServiceNotes(reason?: string | null): string | null {
    if (!reason) return null;
    let text = reason.replace(/Part\s+\d+\s+of\s+\d+/gi, '').replace(/\(\s*\)/g, '').trim();
    if (text.includes('Rescheduled via')) return null;
    return text.length > 0 ? text : null;
  }

  getDisplayCancellationReason(appointment: AppointmentDto | null): string {
    if (!appointment) return 'Cancelled upon request';
    const raw = appointment.cleanCancellationReason || appointment.cancellationReason || '';
    const lower = raw.toLowerCase();
    if (!raw || lower.includes('cancelled upon request')) {
      return 'Cancelled upon customer request';
    }
    if (lower.includes('no specialist') || lower.includes('10 minutes') || lower.includes('unavailable') || lower.includes('cancelled by system')) {
      return 'No specialist was available for this requested slot.';
    }
    return raw;
  }

  isAwaiting(part: AppointmentDto): boolean {
    if (part.status === AppointmentStatus.Approved) return false;
    if (part.status === AppointmentStatus.Cancelled) return false;

    // Direct booking or Admin-assigned provider: awaiting provider approval
    if (part.serviceProviderId || (part.serviceProviderName && part.serviceProviderName !== '—' && !part.serviceProviderName.includes('Auto-Dispatch') && !part.serviceProviderName.includes('Awaiting'))) {
      return true;
    }

    // Created by Admin/Staff: always awaiting provider approval (never expired dispatch)
    if (part.createdBy && (part.createdBy.includes('Admin') || part.createdBy.includes('SuperAdmin') || part.createdBy.includes('Staff'))) {
      return true;
    }
    if (this.appointment?.createdBy && (this.appointment.createdBy.includes('Admin') || this.appointment.createdBy.includes('SuperAdmin') || this.appointment.createdBy.includes('Staff'))) {
      return true;
    }

    // Customer broadcast appointment: active 10-minute acceptance window
    const isDispatch = !part.serviceProviderId || part.isBroadcast || part.statusText?.toLowerCase().includes('awaiting');
    if (!isDispatch || !part.createdAt) return false;
    const createdTime = new Date(part.createdAt).getTime();
    return (Date.now() - createdTime) < 10 * 60 * 1000;
  }

  canSelectAnotherSlot(part: AppointmentDto): boolean {
    if (part.status === AppointmentStatus.Approved) return false;
    if (this.isAwaiting(part)) return false;

    // Admin bookings must NEVER give the customer "Select Another Slot"
    if (part.createdBy && (part.createdBy.includes('Admin') || part.createdBy.includes('SuperAdmin') || part.createdBy.includes('Staff'))) {
      return false;
    }
    if (this.appointment?.createdBy && (this.appointment.createdBy.includes('Admin') || this.appointment.createdBy.includes('SuperAdmin') || this.appointment.createdBy.includes('Staff'))) {
      return false;
    }

    // If a provider is assigned, the booking is waiting on that provider, not for customer re-slot
    if (part.serviceProviderId) return false;

    return true;
  }

  getPartServicesText(part: AppointmentDto): string {
    if (part.items && part.items.length > 0) {
      return part.items.map(it => it.serviceName).join(', ');
    }
    return part.serviceName || 'Service';
  }

  onSelectAnotherSlot(part: AppointmentDto): void {
    if (this.appointment) {
      this.rebookPart.emit({ part, parent: this.appointment });
    }
  }
}
