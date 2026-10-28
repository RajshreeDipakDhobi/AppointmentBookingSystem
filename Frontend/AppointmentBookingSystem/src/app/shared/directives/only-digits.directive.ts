import { Directive, HostListener, Input } from '@angular/core';

@Directive({
  selector: '[appOnlyDigits]',
  standalone: true
})
export class OnlyDigitsDirective {
  @Input() maxDigits: number = 10;

  @HostListener('input', ['$event'])
  onInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input) return;
    const cleanDigits = input.value.replace(/\D/g, '').slice(0, this.maxDigits);
    if (input.value !== cleanDigits) {
      input.value = cleanDigits;
      input.dispatchEvent(new Event('input'));
    }
  }

  @HostListener('keydown', ['$event'])
  onKeyDown(event: KeyboardEvent): void {
    // Allow navigation keys, backspace, delete, tab, enter, and clipboard shortcuts
    const allowedKeys = ['Backspace', 'Delete', 'Tab', 'Escape', 'Enter', 'ArrowLeft', 'ArrowRight', 'Home', 'End'];
    if (allowedKeys.includes(event.key) || event.ctrlKey || event.metaKey) {
      return;
    }
    // Block non-numeric keystrokes
    if (!/^\d$/.test(event.key)) {
      event.preventDefault();
    }
  }

  @HostListener('paste', ['$event'])
  onPaste(event: ClipboardEvent): void {
    event.preventDefault();
    const pastedText = event.clipboardData?.getData('text') || '';
    const cleanDigits = pastedText.replace(/\D/g, '').slice(0, this.maxDigits);
    const input = event.target as HTMLInputElement;
    if (input) {
      input.value = cleanDigits;
      input.dispatchEvent(new Event('input'));
    }
  }
}
