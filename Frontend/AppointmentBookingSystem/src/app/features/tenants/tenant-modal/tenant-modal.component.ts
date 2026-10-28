import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TenantDto, CreateTenantRequestDto, UpdateTenantRequestDto } from '../../../core/models/tenant.models';
import { TenantService } from '../../../core/services/tenant.service';

import { OnlyDigitsDirective } from '../../../shared/directives';
import { EMAIL_REGEX, PHONE_10_DIGIT_REGEX, filter10Digits } from '../../../core/constants/app.constants';

@Component({
  selector: 'app-tenant-modal',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, OnlyDigitsDirective],
  templateUrl: './tenant-modal.component.html',
  styleUrls: ['./tenant-modal.component.css']
})
export class TenantModalComponent implements OnChanges {
  private readonly fb = inject(FormBuilder);
  private readonly tenantService = inject(TenantService);

  @Input() isOpen = false;
  @Input() mode: 'create' | 'edit' = 'create';
  @Input() tenantToEdit: TenantDto | null = null;

  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<TenantDto>();

  tenantForm: FormGroup;
  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    this.tenantForm = this.fb.group({
      // Organization fields
      tenantName: ['', [Validators.required, Validators.minLength(2)]],
      contactEmail: ['', [Validators.required, Validators.pattern(EMAIL_REGEX)]],
      contactPhone: ['', [Validators.required, Validators.pattern(PHONE_10_DIGIT_REGEX)]],
      address: ['', [Validators.required]],

      // Admin Account fields
      adminFirstName: ['', [Validators.required]],
      adminLastName: ['', [Validators.required]]
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isOpen'] && this.isOpen) {
      this.resetAndInitForm();
    }
  }

  private resetAndInitForm(): void {
    this.errorMessage.set(null);
    this.isSubmitting.set(false);

    if (this.mode === 'edit' && this.tenantToEdit) {
      this.tenantForm.reset({
        tenantName: this.tenantToEdit.name,
        contactEmail: this.tenantToEdit.contactEmail || this.tenantToEdit.adminEmail || '',
        contactPhone: filter10Digits(this.tenantToEdit.contactPhone || ''),
        address: this.tenantToEdit.address || '',
        adminFirstName: this.tenantToEdit.adminFirstName || '',
        adminLastName: this.tenantToEdit.adminLastName || ''
      });
    } else {
      this.tenantForm.reset({
        tenantName: '',
        contactEmail: '',
        contactPhone: '',
        address: '',
        adminFirstName: '',
        adminLastName: ''
      });
    }
  }

  onBackdropClick(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('modal-backdrop')) {
      this.onClose();
    }
  }

  onClose(): void {
    this.errorMessage.set(null);
    this.close.emit();
  }

  onSubmit(): void {
    if (this.tenantForm.invalid) {
      this.tenantForm.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const val = this.tenantForm.value;

    if (this.mode === 'create') {
      const createDto: CreateTenantRequestDto = {
        tenantName: val.tenantName.trim(),
        contactEmail: val.contactEmail.trim(),
        contactPhone: val.contactPhone ? val.contactPhone.trim() : undefined,
        address: val.address ? val.address.trim() : undefined,
        adminFirstName: val.adminFirstName.trim(),
        adminLastName: val.adminLastName.trim()
      };

      this.tenantService.createTenant(createDto).subscribe({
        next: (created) => {
          this.isSubmitting.set(false);
          this.saved.emit(created);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err?.error?.message || 'Failed to create organization. Please try again.');
        }
      });
    } else if (this.mode === 'edit' && this.tenantToEdit) {
      const updateDto: UpdateTenantRequestDto = {
        tenantName: val.tenantName.trim(),
        contactEmail: val.contactEmail?.trim() || undefined,
        contactPhone: val.contactPhone ? val.contactPhone.trim() : undefined,
        address: val.address ? val.address.trim() : undefined,
        adminFirstName: val.adminFirstName ? val.adminFirstName.trim() : undefined,
        adminLastName: val.adminLastName ? val.adminLastName.trim() : undefined
      };

      this.tenantService.updateTenant(this.tenantToEdit.id, updateDto).subscribe({
        next: (updated) => {
          this.isSubmitting.set(false);
          this.saved.emit(updated);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err?.error?.message || 'Failed to update organization. Please try again.');
        }
      });
    }
  }
}
