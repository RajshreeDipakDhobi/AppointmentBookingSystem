import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AppointmentService } from '../../../core/services/appointment.service';
import { AppointmentDto, CancelAppointmentRequestDto } from '../../../core/models/appointment.models';

import { AppPhonePipe, AppInitialsPipe } from '../../../shared/pipes';
import { isAppointmentPast, canCancelAppointment } from '../../../core/constants/app.constants';
import { ConfirmService } from '../../../shared/components/confirmation-modal/confirm.service';

@Component({
  selector: 'app-cancel-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, AppPhonePipe, AppInitialsPipe],
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
