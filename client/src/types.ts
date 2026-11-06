// Mirrors the server DTOs in server/Celd.Api/Application/Dtos.cs.

export type Role = 'Student' | 'Staff';
export type ReservationStatus = 'Reserved' | 'CheckedOut' | 'Cancelled' | 'Returned';

export interface CurrentUser {
  id: string;
  displayName: string;
  role: Role;
}

export interface EquipmentSummary {
  id: string;
  assetCode: string;
  name: string;
  category: string;
  isAvailable: boolean;
}

export interface EquipmentDetail extends EquipmentSummary {
  description: string;
}

export interface Reservation {
  id: string;
  equipmentItemId: string;
  itemName: string;
  assetCode: string;
  studentId: string;
  studentName: string;
  status: ReservationStatus;
  isActive: boolean;
  createdAtUtc: string;
}

export interface HistoryEvent {
  eventType: string;
  resultingStatus: ReservationStatus;
  actorId: string;
  actorName: string;
  occurredAtUtc: string;
}

/** The FR-ERR-001 problem shape returned by every API error. */
export interface Problem {
  status: number;
  category: 'Validation' | 'Unauthenticated' | 'Forbidden' | 'NotFound' | 'Conflict' | 'ServerError';
  errorCode: string;
  detail: string;
}
