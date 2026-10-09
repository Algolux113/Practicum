-- Обновление ранее созданной через EnsureCreated схемы без удаления данных.
ALTER TABLE "Events"
    ADD COLUMN IF NOT EXISTS "IsDeleted" boolean NOT NULL DEFAULT FALSE;
