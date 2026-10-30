import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AppointmentService } from '../../../core/services/appointment.service';
import { AppointmentDto } from '../../../core/models/appointment.models';
import { AppDatePipe, AppTimePipe, AppPhonePipe } from '../../../shared/pipes';
import { ConfirmService } from '../../../shared/components/confirmation-modal/confirm.service';

@Component({
  selector: 'app-service-dispatch-modal',
  standalone: true,
  imports: [CommonModule, AppDatePipe, AppTimePipe, AppPhonePipe],
  templateUrl: './service-dispatch-modal.component.html',
  styleUrls: ['./service-dispatch-modal.component.css']
})
export class ServiceDispatchModalComponent {
  private readonly appointmentService = inject(AppointmentService);
  private readonly confirmService = inject(ConfirmService);

  @Input() requests: AppointmentDto[] = [];
  @Input() selectedRequest: AppointmentDto | null = null;
  @Output() close = new EventEmitter<void>();
  @Output() accepted = new EventEmitter<AppointmentDto>();

  readonly isAcceptingId = signal<number | null>(null);
  readonly errorMessage = signal<string>('');

  get activeRequest(): AppointmentDto | null {
    if (this.selectedRequest) return this.selectedRequest;
    return this.requests.length > 0 ? this.requests[0] : null;
  }

  selectRequest(req: AppointmentDto): void {
    this.selectedRequest = req;
    this.errorMessage.set('');
  }

  declineCurrent(): void {
    if (!this.activeRequest) {
      this.onClose();
      return;
    }
    const currentId = this.activeRequest.id;
    this.requests = this.requests.filter(r => r.id !== currentId);
    if (this.requests.length > 0) {
      this.selectedRequest = this.requests[0];
    } else {
      this.selectedRequest = null;
      this.onClose();
    }
  }

  acceptRequest(req: AppointmentDto): void {
    this.isAcceptingId.set(req.id);
    this.errorMessage.set('');

    this.appointmentService.acceptBroadcastAppointment(req.id).subscribe({
      next: (res: AppointmentDto) => {
        this.isAcceptingId.set(null);
        this.accepted.emit(res);
      },
      error: (err) => {
        this.isAcceptingId.set(null);
        const errorMsg = err.error?.message || 'This request could not be accepted. It may have already been accepted by another specialist.';
        this.errorMessage.set(errorMsg);
        this.confirmService.alert({
          title: 'Request Unavailable',
          message: errorMsg,
          variant: 'warning'
        });
      }
    });
  }

  onClose(): void {
    this.close.emit();
  }
}
