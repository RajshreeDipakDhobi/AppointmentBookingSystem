import { Component, Input, Output, EventEmitter, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AvailableSlotDto } from '../../../core/models/appointment.models';
import { formatTo12Hour } from '../../utils/formatters';

interface SlotGroup {
  title: string;
  icon: string;
  slots: AvailableSlotDto[];
}

@Component({
  selector: 'app-time-slot-picker',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './time-slot-picker.component.html',
  styleUrls: ['./time-slot-picker.component.css']
})
export class TimeSlotPickerComponent {
  private _slots = signal<AvailableSlotDto[]>([]);

  @Input()
  set slots(value: AvailableSlotDto[]) {
    this._slots.set(value || []);
  }
  get slots(): AvailableSlotDto[] {
    return this._slots();
  }

  @Input() selectedSlot: AvailableSlotDto | null = null;
  @Input() isLoading: boolean = false;
  @Input() emptyMessage: string = 'No time slots available for the selected date.';
  @Input() hideUnavailable: boolean = true;
  @Input() hideHeader: boolean = false;

  @Output() selectedSlotChange = new EventEmitter<AvailableSlotDto | null>();
  @Output() slotSelect = new EventEmitter<AvailableSlotDto>();

  // Group slots into Morning, Afternoon, Evening (Hide booked and unavailable slots)
  readonly groupedSlots = computed<SlotGroup[]>(() => {
    let list = this._slots();
    if (!list || list.length === 0) return [];

    if (this.hideUnavailable) {
      list = list.filter(s => s.isAvailable);
    }

    if (list.length === 0) return [];

    const morning: AvailableSlotDto[] = [];
    const afternoon: AvailableSlotDto[] = [];
    const evening: AvailableSlotDto[] = [];

    for (const slot of list) {
      const hour = parseInt(slot.startTime.split(':')[0], 10);
      if (hour < 12) {
        morning.push(slot);
      } else if (hour < 17) {
        afternoon.push(slot);
      } else {
        evening.push(slot);
      }
    }

    const groups: SlotGroup[] = [];
    if (morning.length > 0) {
      groups.push({ title: 'Morning', icon: '🌅', slots: morning });
    }
    if (afternoon.length > 0) {
      groups.push({ title: 'Afternoon', icon: '☀️', slots: afternoon });
    }
    if (evening.length > 0) {
      groups.push({ title: 'Evening', icon: '🌙', slots: evening });
    }

    return groups;
  });

  readonly availableCount = computed(() => {
    return this._slots().filter(s => s.isAvailable).length;
  });

  formatSlotTime(timeStr: string): string {
    return formatTo12Hour(timeStr);
  }

  onSelect(slot: AvailableSlotDto): void {
    if (!slot.isAvailable) return;
    this.selectedSlot = slot;
    this.selectedSlotChange.emit(slot);
    this.slotSelect.emit(slot);
  }

  isSelected(slot: AvailableSlotDto): boolean {
    if (!this.selectedSlot) return false;
    return this.selectedSlot.startTime === slot.startTime && this.selectedSlot.endTime === slot.endTime;
  }
}
