import { Injectable, signal } from '@angular/core';

export interface ConfirmOptions {
  title: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  variant?: 'danger' | 'warning' | 'primary' | 'info';
  isAlertOnly?: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class ConfirmService {
  readonly isOpen = signal<boolean>(false);
  readonly options = signal<ConfirmOptions | null>(null);

  private resolver: ((value: boolean) => void) | null = null;

  confirm(options: ConfirmOptions): Promise<boolean> {
    return new Promise((resolve) => {
      this.resolver = resolve;
      this.options.set({
        confirmText: 'Yes',
        cancelText: 'No',
        variant: 'primary',
        isAlertOnly: false,
        ...options
      });
      this.isOpen.set(true);
    });
  }

  alert(options: { 
    title: string; 
    message: string; 
    variant?: 'danger' | 'warning' | 'primary' | 'info'; 
    buttonText?: string; 
  }): Promise<void> {
    return new Promise((resolve) => {
      this.resolver = () => resolve();
      this.options.set({
        title: options.title,
        message: options.message,
        confirmText: options.buttonText || 'OK',
        variant: options.variant || 'primary',
        isAlertOnly: true
      });
      this.isOpen.set(true);
    });
  }

  handleConfirm(): void {
    this.isOpen.set(false);
    if (this.resolver) {
      this.resolver(true);
      this.resolver = null;
    }
  }

  handleCancel(): void {
    this.isOpen.set(false);
    if (this.resolver) {
      this.resolver(false);
      this.resolver = null;
    }
  }
}
