export enum UserRole {
  SuperAdmin = 1,
  Admin = 2,
  ServiceProvider = 3,
  Customer = 4
}

export interface UserDto {
  id: number;
  firstName: string;
  lastName: string;
  fullName: string;
  email: string;
  phoneNumber?: string;
  role: UserRole;
  roleName?: string;
  tenantId?: number;
  tenantName?: string;

  // Profile & Address
  profilePictureUrl?: string;
  address?: string;
  city?: string;
  state?: string;
  postalCode?: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterCustomerRequest {
  fullName: string;
  email: string;
  phoneNumber: string;
  password: string;
  address?: string;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: UserDto;
}
