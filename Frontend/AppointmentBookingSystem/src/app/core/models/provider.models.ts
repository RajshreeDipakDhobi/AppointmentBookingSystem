export enum ProviderStatus {
  Active = 1,
  Inactive = 2
}

export interface ServiceProviderDto {
  id: number;
  tenantId: number;
  userId?: number;
  name: string;
  serviceId?: number;
  serviceName?: string;
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
  serviceId?: number;
  serviceType?: string;
  email: string;
  phoneNumber: string;
  chargesAmount?: number;
  address?: string;
  bio?: string;
  profilePictureUrl?: string;
}

export interface UpdateServiceProviderRequestDto {
  name: string;
  serviceId?: number;
  serviceType?: string;
  email: string;
  phoneNumber: string;
  chargesAmount?: number;
  address?: string;
  bio?: string;
  profilePictureUrl?: string;
  status: ProviderStatus;
}
