import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BackupProgressComponent } from './backup-progress.component';

describe('BackupProgressComponent', () => {
  let component: BackupProgressComponent;
  let fixture: ComponentFixture<BackupProgressComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [BackupProgressComponent]
    });
    fixture = TestBed.createComponent(BackupProgressComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
