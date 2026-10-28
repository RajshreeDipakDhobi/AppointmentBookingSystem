import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { TenantDto, CreateTenantRequestDto, UpdateTenantRequestDto, TenantStatus } from '../models/tenant.models';
import { PagedResult, PaginationParams } from '../models/pagination.models';
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class TenantService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/tenants`;

  getAllTenants(params?: PaginationParams): Observable<PagedResult<TenantDto>> {
    let httpParams = new HttpParams();
    if (params?.pageNumber) httpParams = httpParams.set('pageNumber', params.pageNumber);
    if (params?.pageSize) httpParams = httpParams.set('pageSize', params.pageSize);
    if (params?.search) httpParams = httpParams.set('search', params.search);

    return this.http.get<PagedResult<TenantDto>>(this.apiUrl, { params: httpParams });
  }

  getActiveTenants(): Observable<TenantDto[]> {
    return this.http.get<TenantDto[]>(`${this.apiUrl}/active`);
  }

  getTenantById(id: number): Observable<TenantDto> {
    return this.http.get<TenantDto>(`${this.apiUrl}/${id}`);
  }


  createTenant(dto: CreateTenantRequestDto): Observable<TenantDto> {
    return this.http.post<TenantDto>(this.apiUrl, dto);
  }

  updateTenant(id: number, dto: UpdateTenantRequestDto): Observable<TenantDto> {
    return this.http.put<TenantDto>(`${this.apiUrl}/${id}`, dto);
  }

  updateTenantStatus(id: number, status: TenantStatus): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.apiUrl}/${id}/status`, { status });
  }
}
