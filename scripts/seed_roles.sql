-- Script to seed Identity roles into the database
-- This ensures that Admin, Supervisor, and Agent roles exist

DO $$
BEGIN
    -- Insert Admin role if it doesn't exist
    IF NOT EXISTS (SELECT 1 FROM "AspNetRoles" WHERE "Name" = 'Admin') THEN
        INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
        VALUES (
            gen_random_uuid()::text,
            'Admin',
            'ADMIN',
            gen_random_uuid()::text
        );
        RAISE NOTICE 'Created Admin role';
    ELSE
        RAISE NOTICE 'Admin role already exists';
    END IF;

    -- Insert Supervisor role if it doesn't exist
    IF NOT EXISTS (SELECT 1 FROM "AspNetRoles" WHERE "Name" = 'Supervisor') THEN
        INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
        VALUES (
            gen_random_uuid()::text,
            'Supervisor',
            'SUPERVISOR',
            gen_random_uuid()::text
        );
        RAISE NOTICE 'Created Supervisor role';
    ELSE
        RAISE NOTICE 'Supervisor role already exists';
    END IF;

    -- Insert Agent role if it doesn't exist
    IF NOT EXISTS (SELECT 1 FROM "AspNetRoles" WHERE "Name" = 'Agent') THEN
        INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
        VALUES (
            gen_random_uuid()::text,
            'Agent',
            'AGENT',
            gen_random_uuid()::text
        );
        RAISE NOTICE 'Created Agent role';
    ELSE
        RAISE NOTICE 'Agent role already exists';
    END IF;
END $$;

-- Display all roles
SELECT "Name", "NormalizedName" 
FROM "AspNetRoles" 
ORDER BY "Name";
