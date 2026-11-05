import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { forkJoin } from 'rxjs';
import { AppointmentService } from '../../../core/services/appointment.service';
import { AppointmentDto } from '../../../core/models/appointment.models';
import { AppDatePipe, AppTimePipe, AppPhonePipe } from '../../../shared/pipes';
import { ConfirmService } from '../../../shared/components/confirmation-modal/confirm.service';
import { parseServerUtcDate } from '../../../core/constants/app.constants';

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

  isRequestExpired(req: AppointmentDto): boolean {
    if (!req.createdAt) return false;
    const createdTime = parseServerUtcDate(req.createdAt);
    if (!createdTime) return false;
    return (Date.now() - createdTime) >= 10 * 60 * 1000;
  }

  get activeRequestsList(): AppointmentDto[] {
    return (this.requests || []).filter(r => !this.isRequestExpired(r));
  }

  get activeRequest(): AppointmentDto | null {
    const list = this.activeRequestsList;
    if (this.selectedRequest && !this.isRequestExpired(this.selectedRequest) && list.some(r => r.id === this.selectedRequest!.id)) {
      return this.selectedRequest;
    }
    return list.length > 0 ? list[0] : null;
  }

  selectRequest(req: AppointmentDto): void {
    if (this.isRequestExpired(req)) {
      this.declineCurrent();
      return;
    }
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
    const remaining = this.activeRequestsList;
    if (remaining.length > 0) {
      this.selectedRequest = remaining[0];
    } else {
      this.selectedRequest = null;
      this.onClose();
    }
  }

  acceptRequest(req: AppointmentDto): void {
    if (this.isRequestExpired(req)) {
      const errorMsg = 'This request has expired. The specialist acceptance window has elapsed.';
      this.requests = this.requests.filter(r => r.id !== req.id);
      const remaining = this.activeRequestsList;
      if (remaining.length > 0) {
        this.selectedRequest = remaining[0];
        this.errorMessage.set(errorMsg);
      } else {
        this.selectedRequest = null;
        this.onClose();
      }
      this.confirmService.alert({
        title: 'Request Expired',
        message: errorMsg,
        variant: 'warning'
      });
      return;
    }

    this.isAcceptingId.set(req.id);
    this.errorMessage.set('');

    const partIds: number[] = (req as any).partIds || [req.id];
    const acceptCalls = partIds.map(id => this.appointmentService.acceptBroadcastAppointment(id));

    forkJoin(acceptCalls).subscribe({
      next: (results: AppointmentDto[]) => {
        this.isAcceptingId.set(null);
        this.accepted.emit(results[0]);
      },
      error: (err) => {
        this.isAcceptingId.set(null);
        const errorMsg = err.error?.message || 'This appointment request has already been accepted and confirmed by another specialist.';
        
        // Immediately remove this already-accepted request from the UI
        this.requests = this.requests.filter(r => !partIds.includes(r.id));
        const remaining = this.activeRequestsList;
        if (remaining.length > 0) {
          this.selectedRequest = remaining[0];
          this.errorMessage.set(errorMsg);
        } else {
          this.selectedRequest = null;
          this.onClose();
        }

        // Show prominent warning modal on top
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

  getCleanReason(reason?: string | null): string | null {
    if (!reason) return null;
    const clean = reason.replace(/Part\s+\d+\s+of\s+\d+/gi, '').replace(/\(\s*\)/g, '').trim();
    if (clean.includes('Rescheduled via') || clean.length === 0) return null;
    return clean;
  }
}
