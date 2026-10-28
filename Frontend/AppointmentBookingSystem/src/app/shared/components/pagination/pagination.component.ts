import { Component, Input, Output, EventEmitter, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-pagination',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './pagination.component.html',
  styleUrls: ['./pagination.component.css']
})
export class PaginationComponent {
  @Input() pageNumber: number = 1;
  @Input() set currentPage(val: number) {
    if (val !== undefined && val !== null) {
      this.pageNumber = val;
    }
  }
  get currentPage(): number {
    return this.pageNumber;
  }
  @Input() pageSize: number = 5;
  @Input() totalCount: number = 0;
  @Input() totalPages: number = 1;
  @Input() pageSizeOptions: number[] = [5, 10, 25, 50, 100];

  @Output() pageChange = new EventEmitter<number>();
  @Output() pageSizeChange = new EventEmitter<number>();

  isSizeDropdownOpen: boolean = false;
  openDirection: 'down' | 'up' = 'down';

  toggleSizeDropdown(event: Event): void {
    event.stopPropagation();
    if (!this.isSizeDropdownOpen) {
      const trigger = (event.currentTarget || event.target) as HTMLElement;
      if (trigger) {
        const rect = trigger.getBoundingClientRect();
        const viewportHeight = window.innerHeight || document.documentElement.clientHeight;
        const spaceBelow = viewportHeight - rect.bottom;
        const spaceAbove = rect.top;
        const estimatedDropdownHeight = 185;

        // Open downwards by default; only open upwards if space below is insufficient and there is more space above
        if (spaceBelow < estimatedDropdownHeight && spaceAbove > spaceBelow) {
          this.openDirection = 'up';
        } else {
          this.openDirection = 'down';
        }
      }
      this.isSizeDropdownOpen = true;
    } else {
      this.isSizeDropdownOpen = false;
    }
  }

  selectPageSize(size: number, event: Event): void {
    event.stopPropagation();
    this.isSizeDropdownOpen = false;
    if (size !== this.pageSize) {
      this.pageSize = size;
      this.pageSizeChange.emit(size);
    }
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    this.isSizeDropdownOpen = false;
  }

  get startRecord(): number {
    if (this.totalCount === 0) return 0;
    return (this.pageNumber - 1) * this.pageSize + 1;
  }

  get endRecord(): number {
    const end = this.pageNumber * this.pageSize;
    return end > this.totalCount ? this.totalCount : end;
  }

  get visiblePages(): (number | string)[] {
    const total = this.totalPages;
    const current = this.pageNumber;
    if (total <= 7) {
      return Array.from({ length: total }, (_, i) => i + 1);
    }

    const pages: (number | string)[] = [];
    pages.push(1);

    if (current > 3) {
      pages.push('...');
    }

    const start = Math.max(2, current - 1);
    const end = Math.min(total - 1, current + 1);

    for (let i = start; i <= end; i++) {
      pages.push(i);
    }

    if (current < total - 2) {
      pages.push('...');
    }

    pages.push(total);
    return pages;
  }

  goToPage(page: number | string): void {
    if (typeof page !== 'number' || page === this.pageNumber || page < 1 || page > this.totalPages) {
      return;
    }
    this.pageChange.emit(page);
  }

  onPageSizeChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    const size = parseInt(select.value, 10);
    if (!isNaN(size) && size > 0) {
      this.pageSize = size;
      this.pageSizeChange.emit(size);
    }
  }
}
