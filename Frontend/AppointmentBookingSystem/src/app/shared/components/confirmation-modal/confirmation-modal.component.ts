import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ConfirmService } from './confirm.service';

@Component({
  selector: 'app-confirmation-modal',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './confirmation-modal.component.html',
  styleUrls: ['./confirmation-modal.component.css']
})
export class ConfirmationModalComponent {
  readonly confirmService = inject(ConfirmService);

  onConfirm(): void {
    this.confirmService.handleConfirm();
  }

  onCancel(): void {
    this.confirmService.handleCancel();
  }

  onBackdropClick(event: MouseEvent): void {
    const opts = this.confirmService.options();
    // Allow closing by clicking backdrop only if it's an alert or optional dismiss
    if (opts?.isAlertOnly) {
      this.confirmService.handleConfirm();
    } else {
      this.confirmService.handleCancel();
    }
  }
}
