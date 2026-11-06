-- Hand-written mirror of the tables and constraints that CeldDbContext configures.
-- Purpose: test the DATABASE MECHANISMS behind BR-RES-002 and BR-RES-004 directly in PostgreSQL.
-- This is NOT the EF-generated schema. The integration tests in tests/Celd.Tests prove the real one.

CREATE TABLE "Users" (
    "Id" uuid PRIMARY KEY,
    "UserName" varchar(64) NOT NULL UNIQUE,
    "DisplayName" varchar(100) NOT NULL,
    "PasswordHash" text NOT NULL,
    "Role" varchar(20) NOT NULL
);

CREATE TABLE "EquipmentItems" (
    "Id" uuid PRIMARY KEY,
    "AssetCode" varchar(32) NOT NULL UNIQUE,
    "Name" varchar(120) NOT NULL,
    "Category" varchar(60) NOT NULL,
    "Description" varchar(1000) NOT NULL
);

CREATE TABLE "Reservations" (
    "Id" uuid PRIMARY KEY,
    "EquipmentItemId" uuid NOT NULL REFERENCES "EquipmentItems" ("Id") ON DELETE RESTRICT,
    "StudentId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    "Status" varchar(20) NOT NULL,
    "CreatedAtUtc" timestamptz NOT NULL,
    "Version" integer NOT NULL
);

-- BR-RES-002: the same filter string as CeldDbContext.OneActivePerItemFilter.
CREATE UNIQUE INDEX "IX_Reservations_OneActivePerItem"
    ON "Reservations" ("EquipmentItemId")
    WHERE "Status" IN ('Reserved', 'CheckedOut');

CREATE TABLE "HistoryEvents" (
    "Id" uuid PRIMARY KEY,
    "ReservationId" uuid NOT NULL REFERENCES "Reservations" ("Id") ON DELETE RESTRICT,
    "EventType" varchar(20) NOT NULL,
    "ResultingStatus" varchar(20) NOT NULL,
    "ActorId" uuid NOT NULL REFERENCES "Users" ("Id") ON DELETE RESTRICT,
    "OccurredAtUtc" timestamptz NOT NULL
);

INSERT INTO "Users" VALUES
  ('11111111-1111-1111-1111-111111111111', 'student1', 'Student One', 'x', 'Student'),
  ('22222222-2222-2222-2222-222222222222', 'student2', 'Student Two', 'x', 'Student');

INSERT INTO "EquipmentItems" VALUES
  ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'CAM-001', 'Mirrorless Camera Kit', 'Camera', 'probe'),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'PRJ-001', 'Portable Projector', 'Projector', 'probe');
