import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ServiceProviderDto, CreateServiceProviderRequestDto, UpdateServiceProviderRequestDto } from '../../../core/models/provider.models';
import { ProviderService } from '../../../core/services/provider.service';

import { OnlyDigitsDirective } from '../../../shared/directives';
import { AppInitialsPipe } from '../../../shared/pipes';
import { EMAIL_REGEX, PHONE_10_DIGIT_REGEX, filter10Digits, getAvatarGradient } from '../../../core/constants/app.constants';

@Component({
  selector: 'app-provider-modal',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, OnlyDigitsDirective, AppInitialsPipe],
  templateUrl: './provider-modal.component.html',
  styleUrls: ['./provider-modal.component.css']
})
export class ProviderModalComponent implements OnChanges {
  private readonly fb = inject(FormBuilder);
  private readonly providerService = inject(ProviderService);
  readonly getAvatarGradient = getAvatarGradient;

  @Input() isOpen = false;
  @Input() mode: 'create' | 'edit' = 'create';
  @Input() providerToEdit: ServiceProviderDto | null = null;

  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<ServiceProviderDto>();

  providerForm: FormGroup;
  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);
  readonly previewAvatarUrl = signal<string | null>(null);
  readonly isUploadingAvatar = signal<boolean>(false);
  readonly avatarError = signal<string | null>(null);

  constructor() {
    this.providerForm = this.fb.group({
      name: ['', [Validators.required, Validators.minLength(2)]],
      serviceType: ['', [Validators.required]],
      email: ['', [Validators.required, Validators.pattern(EMAIL_REGEX)]],
      phoneNumber: ['', [Validators.required, Validators.pattern(PHONE_10_DIGIT_REGEX)]],
      chargesAmount: [0, [Validators.required, Validators.min(0)]],
      address: [''],
      bio: ['', [Validators.required, Validators.minLength(5)]],
      profilePictureUrl: ['']
    });
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isOpen'] || changes['providerToEdit'] || changes['mode']) {
      if (this.isOpen) {
        this.resetAndInitForm();
      }
    }
  }

  private resetAndInitForm(): void {
    this.errorMessage.set(null);
    this.avatarError.set(null);
    this.isSubmitting.set(false);
    this.isUploadingAvatar.set(false);

    if (this.mode === 'edit' && this.providerToEdit) {
      this.previewAvatarUrl.set(this.providerToEdit.profilePictureUrl || null);
      this.providerForm.reset({
        name: this.providerToEdit.name || '',
        serviceType: this.providerToEdit.serviceType || '',
        email: this.providerToEdit.email || '',
        phoneNumber: filter10Digits(this.providerToEdit.phoneNumber) || '',
        chargesAmount: this.providerToEdit.chargesAmount ?? 0,
        address: this.providerToEdit.address || '',
        bio: this.providerToEdit.bio || '',
        profilePictureUrl: this.providerToEdit.profilePictureUrl || ''
      });
    } else {
      this.previewAvatarUrl.set(null);
      this.providerForm.reset({
        name: '',
        serviceType: '',
        email: '',
        phoneNumber: '',
        chargesAmount: 0,
        address: '',
        bio: '',
        profilePictureUrl: ''
      });
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files || input.files.length === 0) return;

    const file = input.files[0];
    this.avatarError.set(null);

    // Limit to 5 MB
    if (file.size > 5 * 1024 * 1024) {
      this.avatarError.set('Image size exceeds 5 MB. Please select a smaller photo.');
      input.value = '';
      return;
    }

    // Validate type
    const validTypes = ['image/jpeg', 'image/png', 'image/webp', 'image/gif'];
    if (!validTypes.includes(file.type)) {
      this.avatarError.set('Invalid image format. Allowed formats: JPG, PNG, WEBP, GIF.');
      input.value = '';
      return;
    }

    // Set immediate client-side preview
    const reader = new FileReader();
    reader.onload = () => {
      this.previewAvatarUrl.set(reader.result as string);
    };
    reader.readAsDataURL(file);

    // Upload to server storage
    this.isUploadingAvatar.set(true);
    this.providerService.uploadAvatar(file).subscribe({
      next: (res) => {
        this.isUploadingAvatar.set(false);
        this.providerForm.patchValue({ profilePictureUrl: res.url });
        // Retain client-side base64 preview so the image always displays without 404
      },
      error: (err) => {
        this.isUploadingAvatar.set(false);
        this.avatarError.set(err?.error?.message || 'Failed to upload photo. Please try again.');
      }
    });
  }

  onAvatarImgError(): void {
    // If an image URL cannot be loaded by the browser, cleanly fall back to the placeholder icon
    this.previewAvatarUrl.set(null);
  }

  removeAvatar(): void {
    this.previewAvatarUrl.set(null);
    this.avatarError.set(null);
    this.providerForm.patchValue({ profilePictureUrl: '' });
  }

  onBackdropClick(_event: MouseEvent): void {
    // Modal only closes via explicit close button
  }

  onClose(): void {
    this.errorMessage.set(null);
    this.avatarError.set(null);
    this.close.emit();
  }

  onSubmit(): void {
    if (this.providerForm.invalid || this.isUploadingAvatar()) {
      this.providerForm.markAllAsTouched();
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const val = this.providerForm.value;
    const photoUrl = val.profilePictureUrl && val.profilePictureUrl.trim().length > 0 ? val.profilePictureUrl.trim() : undefined;

    if (this.mode === 'create') {
      const createDto: CreateServiceProviderRequestDto = {
        name: val.name.trim(),
        serviceType: val.serviceType.trim(),
        email: val.email.trim(),
        phoneNumber: val.phoneNumber.trim(),
        chargesAmount: val.chargesAmount != null ? Number(val.chargesAmount) : 0,
        address: val.address ? val.address.trim() : undefined,
        bio: val.bio ? val.bio.trim() : undefined,
        profilePictureUrl: photoUrl
      };

      this.providerService.createProvider(createDto).subscribe({
        next: (created) => {
          this.isSubmitting.set(false);
          this.saved.emit(created);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err?.error?.message || 'Failed to add service provider. Please try again.');
        }
      });
    } else if (this.mode === 'edit' && this.providerToEdit) {
      const updateDto: UpdateServiceProviderRequestDto = {
        name: val.name.trim(),
        serviceType: val.serviceType.trim(),
        email: val.email.trim(),
        phoneNumber: val.phoneNumber.trim(),
        chargesAmount: val.chargesAmount != null ? Number(val.chargesAmount) : 0,
        address: val.address ? val.address.trim() : undefined,
        bio: val.bio ? val.bio.trim() : undefined,
        profilePictureUrl: photoUrl,
        status: this.providerToEdit.status
      };

      this.providerService.updateProvider(this.providerToEdit.id, updateDto).subscribe({
        next: (updated) => {
          this.isSubmitting.set(false);
          this.saved.emit(updated);
        },
        error: (err) => {
          this.isSubmitting.set(false);
          this.errorMessage.set(err?.error?.message || 'Failed to update provider. Please try again.');
        }
      });
    }
  }
}
