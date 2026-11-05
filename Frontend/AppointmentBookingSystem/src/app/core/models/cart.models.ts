export interface CartItemDto {
  id: number;
  serviceId: number;
  serviceName: string;
  serviceDescription?: string;
  price: number;
  durationMinutes: number;
  quantity: number;
}

export interface CartDto {
  id?: number;
  userId?: number;
  guestToken?: string;
  tenantId?: number;
  items: CartItemDto[];
  totalAmount?: number;
  totalPrice?: number;
  totalDurationMinutes: number;
  itemCount?: number;
  totalItemsCount?: number;
}

export interface AddToCartRequestDto {
  serviceId: number;
  tenantId?: number;
  guestToken?: string;
}

export interface MergeCartRequestDto {
  guestToken: string;
  tenantId?: number;
}

