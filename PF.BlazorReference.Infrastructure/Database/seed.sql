-- Development-only sample rows. Synthetic data on example.com. Raw SQL on purpose: it bypasses the
-- aggregate and the audit interceptor, so the timestamps are written by hand.
-- INSERT OR IGNORE plus the unique email index makes this safe to run on every startup.

INSERT OR IGNORE INTO DeveloperRegistrations (Id, Name, Email, Status, CreatedAtUtc, LastUpdatedUtc) VALUES
    ('3F2A9C10-6B1E-4D7A-9E54-0A1B2C3D4E01', 'Avery Quinn',   'avery.quinn@example.com',   'Submitted', '2026-10-01 09:15:00', '2026-10-01 09:15:00'),
    ('3F2A9C10-6B1E-4D7A-9E54-0A1B2C3D4E02', 'Jordan Blake',  'jordan.blake@example.com',  'Submitted', '2026-10-02 14:40:00', '2026-10-02 14:40:00'),
    ('3F2A9C10-6B1E-4D7A-9E54-0A1B2C3D4E03', 'Riley Morgan',  'riley.morgan@example.com',  'Submitted', '2026-10-03 11:05:00', '2026-10-03 11:05:00'),
    ('3F2A9C10-6B1E-4D7A-9E54-0A1B2C3D4E04', 'Casey Rivera',  'casey.rivera@example.com',  'Submitted', '2026-10-05 16:30:00', '2026-10-05 16:30:00');
