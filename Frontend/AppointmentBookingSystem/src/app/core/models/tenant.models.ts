export enum TenantStatus {
  Active = 1,
  Inactive = 2,
  Suspended = 3
}

export interface TenantDto {
  id: number;
  name: string;
  tenantCode?: string;
  contactEmail?: string;
  contactPhone?: string;
  address?: string;
  logoUrl?: string;
  status: TenantStatus;
  statusName: string;
  createdAt: string;
  totalProvidersCount: number;
  totalAppointmentsCount: number;
  adminFirstName?: string;
  adminLastName?: string;
  adminEmail?: string;
  adminFullName?: string;
  rating?: number;
  totalReviewsCount?: number;
  serviceTypes?: string[];
  slug?: string;
}

export interface CreateTenantRequestDto {
  tenantName: string;
  contactEmail: string;
  contactPhone?: string;
  address?: string;
  adminFirstName: string;
  adminLastName: string;
}

export interface UpdateTenantRequestDto {
  tenantName: string;
  contactEmail?: string;
  contactPhone?: string;
  address?: string;
  adminFirstName?: string;
  adminLastName?: string;
}

export interface UpdateTenantStatusDto {
  status: TenantStatus;
}
