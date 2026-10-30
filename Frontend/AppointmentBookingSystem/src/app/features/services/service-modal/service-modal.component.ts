import { Component, EventEmitter, HostListener, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ServiceDto, CreateServiceRequestDto, UpdateServiceRequestDto } from '../../../core/models/service.models';
import { STANDARD_DURATION_OPTIONS, DurationOption } from '../../../core/constants/app.constants';
import { ServiceCatalogService } from '../../../core/services/service-catalog.service';

@Component({
  selector: 'app-service-modal',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './service-modal.component.html',
  styleUrls: ['./service-modal.component.css']
})
export class ServiceModalComponent implements OnChanges {
  private readonly fb = inject(FormBuilder);
  private readonly serviceCatalogService = inject(ServiceCatalogService);

  @Input() isOpen = false;
  @Input() mode: 'create' | 'edit' = 'create';
  @Input() serviceToEdit: ServiceDto | null = null;

  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<ServiceDto>();

  serviceForm: FormGroup;
  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);
  readonly isDurationDropdownOpen = signal<boolean>(false);

  readonly durationOptions: DurationOption[] = STANDARD_DURATION_OPTIONS;

  get selectedDurationLabel(): string {
    const val = Number(this.serviceForm?.get('durationMinutes')?.value);
    const match = this.durationOptions.find(o => o.value === val);
    return match ? match.label : (val ? `${val} Minutes` : '30 Minutes');
  }

  constructor() {
    this.serviceForm = this.fb.group({
      name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(150)]],
      description: ['', [Validators.maxLength(1000)]],
      durationMinutes: [30, [Validators.required, Validators.min(5), Validators.max(480)]],
      price: [0, [Validators.required, Validators.min(0)]],
      isActive: [true]
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isOpen'] || changes['serviceToEdit'] || changes['mode']) {
      if (this.isOpen) {
        this.resetAndInitForm();
      }
    }
  }

  private resetAndInitForm(): void {
    this.errorMessage.set(null);
    this.isSubmitting.set(false);
    this.isDurationDropdownOpen.set(false);

    if (this.mode === 'edit' && this.serviceToEdit) {
      this.serviceForm.reset({
        name: this.serviceToEdit.name || '',
        description: this.serviceToEdit.description || '',
        durationMinutes: this.serviceToEdit.durationMinutes ?? 30,
        price: this.serviceToEdit.price ?? 0,
        isActive: this.serviceToEdit.isActive ?? true
      });
    } else {
      this.serviceForm.reset({
        name: '',
        description: '',
        durationMinutes: 30,
        price: 0,
        isActive: true
      });
    }
  }

  toggleDurationDropdown(event: Event): void {
    event.stopPropagation();
    this.isDurationDropdownOpen.update(v => !v);
  }

  selectDuration(minutes: number, event?: Event): void {
    if (event) event.stopPropagation();
    this.serviceForm.patchValue({ durationMinutes: minutes });
    this.serviceForm.get('durationMinutes')?.markAsDirty();
    this.serviceForm.get('durationMinutes')?.markAsTouched();
    this.isDurationDropdownOpen.set(false);
  }

  selectDurationPreset(minutes: number): void {
    this.selectDuration(minutes);
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    this.isDurationDropdownOpen.set(false);
  }

  onClose(): void {
    if (this.isSubmitting()) return;
    this.isDurationDropdownOpen.set(false);
    this.close.emit();
  }

  onSubmit(): void {
    if (this.serviceForm.invalid) {
      this.serviceForm.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const formVal = this.serviceForm.value;

    if (this.mode === 'create') {
      const createDto: CreateServiceRequestDto = {
        name: formVal.name.trim(),
        description: formVal.description ? formVal.description.trim() : undefined,
        durationMinutes: Number(formVal.durationMinutes),
        price: Number(formVal.price),
        isActive: formVal.isActive !== false
      };

      this.serviceCatalogService.createService(createDto).subscribe({
        next: (created) => {
          this.isSubmitting.set(false);
          this.saved.emit(created);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err.error?.message || err.message || 'Failed to create service.');
        }
      });
    } else {
      if (!this.serviceToEdit) return;

      const updateDto: UpdateServiceRequestDto = {
        name: formVal.name.trim(),
        description: formVal.description ? formVal.description.trim() : undefined,
        durationMinutes: Number(formVal.durationMinutes),
        price: Number(formVal.price),
        isActive: formVal.isActive !== false
      };

      this.serviceCatalogService.updateService(this.serviceToEdit.id, updateDto).subscribe({
        next: (updated) => {
          this.isSubmitting.set(false);
          this.saved.emit(updated);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err.error?.message || err.message || 'Failed to update service.');
        }
      });
    }
  }
}

