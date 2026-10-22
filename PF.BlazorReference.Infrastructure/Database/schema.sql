-- The schema source of truth. In the real app a SQL Server Database Project (.sqlproj) owns this
-- and ships as a DACPAC; this file is the SQLite stand-in. EF Core never creates or migrates it.
-- Written to be idempotent so it can run on every startup and in every test.

CREATE TABLE IF NOT EXISTS DeveloperRegistrations (
    Id             TEXT NOT NULL PRIMARY KEY,
    Name           TEXT NOT NULL CHECK (length(Name) BETWEEN 1 AND 100),
    -- Stored normalised (trimmed, lowercase) by the aggregate, so the unique index compares that form.
    Email          TEXT NOT NULL CHECK (length(Email) BETWEEN 3 AND 254),
    Status         TEXT NOT NULL CHECK (Status IN ('Pending')),
    CreatedAtUtc   TEXT NOT NULL,
    LastUpdatedUtc TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_DeveloperRegistrations_Email
    ON DeveloperRegistrations (Email);
