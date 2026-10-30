import { Component, Input, Output, EventEmitter, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AppointmentDto, AppointmentStatus } from '../../../core/models/appointment.models';
import { AppDatePipe, AppTimePipe, AppStatusBadgePipe, AppPhonePipe, AppInitialsPipe } from '../../pipes';
import { CopyToClipboardDirective } from '../../directives';
import { isAppointmentPast, canCancelAppointment, canRescheduleAppointment } from '../../../core/constants/app.constants';

@Component({
  selector: 'app-booking-ticket',
  standalone: true,
  imports: [
    CommonModule,
    AppDatePipe,
    AppTimePipe,
    AppStatusBadgePipe,
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
}
