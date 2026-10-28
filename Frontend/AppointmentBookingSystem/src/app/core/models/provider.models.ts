export enum ProviderStatus {
  Active = 1,
  Inactive = 2
}

export interface ServiceProviderDto {
  id: number;
  tenantId: number;
  name: string;
  serviceType: string;
  email: string;
  phoneNumber: string;
  address?: string;
  profilePictureUrl?: string;
  bio?: string;
  chargesAmount?: number;
  status: ProviderStatus;
  statusName: string;
  createdAt: string;
  totalAppointmentsCount: number;
}

export interface CreateServiceProviderRequestDto {
  name: string;
  serviceType: string;
  email: string;
  phoneNumber: string;
  password: string;
  chargesAmount: number;
  address?: string;
  bio?: string;
  profilePictureUrl?: string;
}

export interface UpdateServiceProviderRequestDto {
  name: string;
  serviceType: string;
  email: string;
  phoneNumber: string;
  chargesAmount?: number;
  address?: string;
  bio?: string;
  profilePictureUrl?: string;
  status: ProviderStatus;
}
