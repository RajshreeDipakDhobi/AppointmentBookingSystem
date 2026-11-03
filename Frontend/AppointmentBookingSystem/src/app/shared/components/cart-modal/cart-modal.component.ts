import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface CartModalItem {
  id: number;
  name: string;
  price: number;
  durationMinutes: number;
  description?: string;
  quantity?: number;
}

@Component({
  selector: 'app-cart-modal',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './cart-modal.component.html',
  styleUrls: ['./cart-modal.component.css']
})
export class CartModalComponent {
  @Input() items: CartModalItem[] = [];
  @Input() title: string = 'Booking Cart';
  @Input() subtitle?: string;
  @Input() proceedButtonText: string = 'Proceed to Schedule Slot';
  @Input() showClearButton: boolean = true;
  @Input() showProceedButton: boolean = true;
  @Input() allowRemove: boolean = true;
  @Input() allowQuantity: boolean = true;

  @Output() close = new EventEmitter<void>();
  @Output() removeItem = new EventEmitter<number>();
  @Output() increment = new EventEmitter<number>();
  @Output() decrement = new EventEmitter<number>();
  @Output() clearAll = new EventEmitter<void>();
  @Output() proceed = new EventEmitter<void>();

  getItemQuantity(item: CartModalItem): number {
    return (item.quantity && item.quantity > 0) ? item.quantity : 1;
  }

  getItemTotal(item: CartModalItem): number {
    return (item.price || 0) * this.getItemQuantity(item);
  }

  getItemDuration(item: CartModalItem): number {
    return (item.durationMinutes || 0) * this.getItemQuantity(item);
  }

  get totalAmount(): number {
    return (this.items || []).reduce((sum, item) => sum + this.getItemTotal(item), 0);
  }

  get totalDuration(): number {
    return (this.items || []).reduce((sum, item) => sum + this.getItemDuration(item), 0);
  }

  get totalItemsCount(): number {
    return (this.items || []).reduce((sum, item) => sum + this.getItemQuantity(item), 0);
  }

  onIncrement(item: CartModalItem, event?: Event): void {
    if (event) event.stopPropagation();
    this.increment.emit(item.id);
  }

  onDecrement(item: CartModalItem, event?: Event): void {
    if (event) event.stopPropagation();
    const qty = this.getItemQuantity(item);
    if (qty > 1) {
      this.decrement.emit(item.id);
    } else {
      this.removeItem.emit(item.id);
    }
  }

  onRemove(id: number, event?: Event): void {
    if (event) event.stopPropagation();
    this.removeItem.emit(id);
  }

  onClear(event?: Event): void {
    if (event) event.stopPropagation();
    this.clearAll.emit();
  }

  onProceed(): void {
    this.proceed.emit();
  }

  onClose(): void {
    this.close.emit();
  }
}
