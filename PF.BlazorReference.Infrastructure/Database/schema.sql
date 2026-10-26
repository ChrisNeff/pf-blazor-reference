-- The schema source of truth. In the real app a SQL Server Database Project (.sqlproj) owns this
-- and ships as a DACPAC; this file is the SQLite stand-in. EF Core never creates or migrates it.
-- Written to be idempotent so it can run on every startup and in every test.

CREATE TABLE IF NOT EXISTS DeveloperRegistrations (
    Id             TEXT NOT NULL PRIMARY KEY,
    Name           TEXT NOT NULL CHECK (length(Name) BETWEEN 1 AND 100),
    -- Stored normalised (trimmed, lowercase) by the aggregate, so the unique index compares that form.
    Email          TEXT NOT NULL CHECK (length(Email) BETWEEN 3 AND 254),
    Status         TEXT NOT NULL CHECK (Status IN ('Submitted', 'ProvisioningRequested', 'Invited', 'Activated', 'ProvisioningFailed')),
    CreatedAtUtc   TEXT NOT NULL,
    LastUpdatedUtc TEXT NOT NULL,
    -- Entra guest provisioning, written by the background worker. The object id is the guest account's,
    -- set when the invitation is sent.
    EntraObjectId        TEXT NULL CHECK (EntraObjectId IS NULL OR length(EntraObjectId) BETWEEN 1 AND 64),
    ProvisioningError    TEXT NULL CHECK (ProvisioningError IS NULL OR length(ProvisioningError) <= 500),
    ProvisioningAttempts INTEGER NOT NULL DEFAULT 0 CHECK (ProvisioningAttempts >= 0),
    -- Concurrency for several app hosts. Version is the optimistic concurrency token, incremented on every
    -- update (SQL Server would use rowversion). A worker claims a row before calling Entra; the claim
    -- lapses at ClaimedUntilUtc so a crashed host's row is picked up again.
    Version         INTEGER NOT NULL DEFAULT 0 CHECK (Version >= 0),
    ClaimedBy       TEXT NULL CHECK (ClaimedBy IS NULL OR length(ClaimedBy) BETWEEN 1 AND 200),
    ClaimedUntilUtc TEXT NULL,
    CHECK ((ClaimedBy IS NULL) = (ClaimedUntilUtc IS NULL)),
    CHECK (Status NOT IN ('Invited', 'Activated') OR EntraObjectId IS NOT NULL)
);

CREATE UNIQUE INDEX IF NOT EXISTS UX_DeveloperRegistrations_Email
    ON DeveloperRegistrations (Email);
