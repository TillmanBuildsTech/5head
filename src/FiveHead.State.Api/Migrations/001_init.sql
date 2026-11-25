-- Shared matter state. events is append-only; the trigger rejects UPDATE and DELETE.

CREATE TABLE matters (
    id uuid PRIMARY KEY,
    title text NOT NULL,
    owner text NOT NULL,
    status text NOT NULL,
    next_step text NULL,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    vector_ref text NULL,
    version bigint NOT NULL DEFAULT 1,
    claimed_by text NULL,
    claim_expires_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT matters_version_positive CHECK (version >= 1),
    CONSTRAINT matters_metadata_object CHECK (jsonb_typeof(metadata) = 'object')
);

CREATE INDEX matters_claim_idx ON matters (status, claim_expires_at, created_at);

CREATE TABLE events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    matter_id uuid NOT NULL REFERENCES matters (id),
    agent text NOT NULL,
    kind text NOT NULL,
    summary text NOT NULL,
    payload jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT events_payload_object CHECK (jsonb_typeof(payload) = 'object')
);

CREATE INDEX events_matter_idx ON events (matter_id, created_at, id);

CREATE OR REPLACE FUNCTION events_reject_mutation() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'events are append-only';
END;
$$;

DROP TRIGGER IF EXISTS events_no_update ON events;
CREATE TRIGGER events_no_update
    BEFORE UPDATE OR DELETE ON events
    FOR EACH ROW EXECUTE FUNCTION events_reject_mutation();
