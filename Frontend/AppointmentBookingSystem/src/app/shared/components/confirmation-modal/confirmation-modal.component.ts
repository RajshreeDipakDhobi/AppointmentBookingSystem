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

  onBackdropClick(_event: MouseEvent): void {
    // Modal only closes via explicit action buttons
  }
}
