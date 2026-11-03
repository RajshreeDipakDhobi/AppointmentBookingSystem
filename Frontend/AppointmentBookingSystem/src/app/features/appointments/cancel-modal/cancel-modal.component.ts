import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AppointmentService } from '../../../core/services/appointment.service';
import { AppointmentDto, CancelAppointmentRequestDto } from '../../../core/models/appointment.models';

import { AppPhonePipe, AppInitialsPipe, AppDatePipe, AppTimePipe } from '../../../shared/pipes';
import { isAppointmentPast, canCancelAppointment } from '../../../core/constants/app.constants';
import { ConfirmService } from '../../../shared/components/confirmation-modal/confirm.service';

@Component({
  selector: 'app-cancel-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, AppPhonePipe, AppInitialsPipe, AppDatePipe, AppTimePipe],
  templateUrl: './cancel-modal.component.html',
  styleUrls: ['./cancel-modal.component.css']
})
export class CancelModalComponent {
  private readonly appointmentService = inject(AppointmentService);
  private readonly confirmService = inject(ConfirmService);

  @Input({ required: true }) appointment!: AppointmentDto;
  @Output() close = new EventEmitter<void>();
  @Output() cancelled = new EventEmitter<AppointmentDto>();

  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string>('');
  readonly isAppointmentPast = isAppointmentPast;
  readonly canCancel = canCancelAppointment;

  cancellationReason = '';

  get serviceNames(): string[] {
    if (this.appointment?.serviceNames && this.appointment.serviceNames.length > 0) {
      return this.appointment.serviceNames;
    }
    if (this.appointment?.items && this.appointment.items.length > 0) {
      return this.appointment.items.map(i => i.serviceName);
    }
    if (this.appointment?.serviceName) {
      return [this.appointment.serviceName];
    }
    return [];
  }

  get serviceNamesSummary(): string {
    const names = this.serviceNames;
    return names.length > 0 ? names.join(' + ') : 'Assigned Services';
  }

  get totalDurationMinutes(): number {
    if (this.appointment?.totalDurationMinutes && this.appointment.totalDurationMinutes > 0) {
      return this.appointment.totalDurationMinutes;
    }
    if (this.appointment?.items && this.appointment.items.length > 0) {
      return this.appointment.items.reduce((sum, item) => sum + (item.durationMinutes || 30), 0);
    }
    return this.appointment?.serviceDurationMinutes || 30;
  }

  get totalAmount(): number {
    if (this.appointment?.totalAmount && this.appointment.totalAmount > 0) {
      return this.appointment.totalAmount;
    }
    if (this.appointment?.items && this.appointment.items.length > 0) {
      return this.appointment.items.reduce((sum, item) => sum + (item.price || 0), 0);
    }
    return this.appointment?.servicePrice || 0;
  }

  confirmCancel(): void {
    if (!canCancelAppointment(this.appointment)) {
      this.errorMessage.set('Appointments can only be cancelled at least 1 hour prior to the scheduled start time.');
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set('');

    const req: CancelAppointmentRequestDto = {
      cancellationReason: this.cancellationReason.trim() || undefined,
      customerContact: this.appointment?.customerPhone || this.appointment?.customerEmail
    };

    this.appointmentService.cancelAppointment(this.appointment.id, req).subscribe({
      next: (res: AppointmentDto) => {
        this.isSubmitting.set(false);
        this.cancelled.emit(res);
      },
      error: (err) => {
        this.isSubmitting.set(false);
        const errorMsg = err.error?.message || 'Failed to cancel appointment.';
        this.errorMessage.set(errorMsg);
        this.confirmService.alert({
          title: 'Cancellation Notice',
          message: errorMsg,
          variant: 'danger',
          buttonText: 'OK'
        });
      }
    });
  }

  onClose(): void {
    this.close.emit();
  }
}
